using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server.GarrysMod;

public class QueuedMessage
{
	public RecipientFilter? Filter;
	public bf_write? Buffer;
	public byte[] Data = new byte[512];
	public InlineArray16<char> Name;
	public uint Player = 0xFFFFFFFF;
	public bool SingleUser;
}

public class UserMessageManager : AutoGameSystemPerFrame
{
	public static readonly UserMessageManager g_UserMessageManager = new();

	static readonly ConVar sv_usermessage_maxsize = new("sv_usermessage_maxsize", "1024", 0, "The max number of bytes to output per tick");

	static int NumPlayers;

	readonly List<QueuedMessage?>[] PlayerMessages = new List<QueuedMessage?>[Constants.ABSOLUTE_PLAYER_LIMIT];
	readonly List<QueuedMessage?> GlobalMessages = [];
	int LuaUserMessage = -1;

	public UserMessageManager() : base(null) {
		for (int i = 0; i < PlayerMessages.Length; i++)
			PlayerMessages[i] = [];
	}

	public override ReadOnlySpan<char> Name() => "UserMessageManager";

	public void AddToQueue(QueuedMessage msg) {
		if (!msg.SingleUser) {
			if (5000 < GlobalMessages.Count)
				Warning($"Warning: {GlobalMessages.Count} global queued usermessages!\n");
			GlobalMessages.Add(msg);
			return;
		}

		int index;
		BaseEntity? ent = msg.Player == 0xFFFFFFFF ? null : gEntList.GetBaseEntity(new BaseHandle(msg.Player));
		if (ent == null)
			index = -100;
		else {
			Edict? edict = ent.Edict();
			if (edict == null)
				index = -1;
			else {
				index = edict.EdictIndex - 1;
				if ((uint)index < 129) {
					if (5000 < PlayerMessages[index].Count)
						Warning($"Warning: Player {index} has {PlayerMessages[index].Count} queued usermessages!\n");
					PlayerMessages[index].Add(msg);
					return;
				}
			}
		}
		Warning($"Tried to send message to invalid or non existent player! ({index})\n");
	}

	void SendMessage(QueuedMessage msg) {
		if (LuaUserMessage == -1)
			LuaUserMessage = usermessages.LookupUserMessage("LuaUserMessage");

		if (msg.Filter == null)
			return;
		if (msg.Filter.GetRecipientCount() <= 0)
			return;

		bf_write buf = engine.UserMessageBegin(msg.Filter, LuaUserMessage);
		int bits = msg.Buffer!.BitsWritten;
		if (bits < buf.MaxBits) {
			msg.Buffer.BaseArray.AsSpan(0, bits + 7 >> 3).CopyTo(buf.BaseArray);
			buf.Seek(bits);
			engine.MessageEnd();
			return;
		}
		engine.MessageEnd();
		Msg($"Error sending usermessage - too large ({((ReadOnlySpan<char>)msg.Name).SliceNullTerminatedString()})");
	}

	static void Destroy(QueuedMessage? msg) {
		if (msg == null)
			return;
		msg.Buffer = null;
		msg.Filter = null;
	}

	public override void LevelShutdownPreEntity() {
		for (int i = 0; i < GlobalMessages.Count; i++) {
			Destroy(GlobalMessages[i]);
			GlobalMessages[i] = null;
		}
		GlobalMessages.Clear();

		for (int p = 0; p < PlayerMessages.Length; p++) {
			List<QueuedMessage?> queue = PlayerMessages[p];
			for (int i = 0; i < queue.Count; i++) {
				Destroy(queue[i]);
				queue[i] = null;
			}
			queue.Clear();
		}
	}

	public override void PreClientUpdate() {
		int players = 0;
		int i = 0;
		do {
			i++;
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player != null && player.Connected != PlayerConnectedState.Disconnected)
				players++;
		} while (i < 128);

		if (NumPlayers != players)
			NumPlayers = players;

		for (int p = 1; p <= 128; p++) {
			List<QueuedMessage?> queue = PlayerMessages[p - 1];
			BasePlayer? player = Util.PlayerByIndex(p);
			if (player == null || player.Connected == PlayerConnectedState.Disconnected) {
				queue.Clear();
				continue;
			}

			int bytes = 0;
			for (int j = 0; j < queue.Count; j++) {
				QueuedMessage? msg = queue[j];
				if (msg == null)
					continue;

				int size = msg.Buffer!.BitsWritten + 7 >> 3;
				bytes += size;
				if (300 < size)
					Msg("Warning: Usermessage was larger than 256 bytes. This will cause problems.\n");
				SendMessage(msg);
				Destroy(msg);
				queue[j] = null;
				if (sv_usermessage_maxsize.GetInt() < bytes)
					break;
			}

			if (bytes <= sv_usermessage_maxsize.GetInt())
				queue.Clear();
		}

		int globalBytes = 0;
		for (int j = 0; j < GlobalMessages.Count; j++) {
			QueuedMessage? msg = GlobalMessages[j];
			if (msg == null)
				continue;

			int size = msg.Buffer!.BitsWritten + 7 >> 3;
			globalBytes += size;
			if (300 < size)
				Msg("Warning: Usermessage was larger than 256 bytes. This will cause problems.\n");
			SendMessage(msg);
			Destroy(msg);
			GlobalMessages[j] = null;
			if (sv_usermessage_maxsize.GetInt() < globalBytes)
				return;
		}
		GlobalMessages.Clear();
	}
}

public static partial class LuaUserMessages
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_umsg = new("umsg");

	static QueuedMessage? CurrentMessage;

	public static void RegisterUserMessages(UserMessages messages) => messages.Register("LuaUserMessage", -1);

	[LuaFunction]
	static int Start(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		if (CurrentMessage != null) {
			lua.ErrorFromLua("umsg.Start: Starting new message without ending the old one!\n");
			CurrentMessage.Buffer = null;
			CurrentMessage.Filter = null;
			CurrentMessage = null;
		}

		CurrentMessage = new();
		CurrentMessage.Buffer = new bf_write();
		CurrentMessage.Buffer.StartWriting(CurrentMessage.Data, 512, 0, -1);

		if (g_Lua.isUserData(2)) {
			if (g_Lua.GetType(2) == LuaType.Entity) {
				BasePlayer? player = LuaPlayer.Get_Player(2, false);
				CurrentMessage.Filter = new SingleUserRecipientFilter(player!);
				CurrentMessage.Player = player != null ? player.GetRefEHandle().Index : 0xFFFFFFFF;
				CurrentMessage.SingleUser = true;
			}
			else {
				RecipientFilter? filter = LuaRecipientFilter.LC_RecipientFilter.Get(2) as RecipientFilter;
				if (filter == null) {
					CurrentMessage.Buffer = null;
					CurrentMessage = null;
					g_Lua.ErrorFromLua("umsg.Start: Error! Invalid Recipient Filter!\n");
					return 0;
				}
				CurrentMessage.Filter = new RecipientFilter();
				CurrentMessage.Filter.CopyFrom(filter);
			}

			if (CurrentMessage.Filter == null) {
				g_Lua.ErrorFromLua("umsg.Start: Recipient Filter Is NULL!? - making it BROADCAST\n");
				CurrentMessage.Filter = new BroadcastRecipientFilter();
			}
		}
		else
			CurrentMessage.Filter = new BroadcastRecipientFilter();

		CurrentMessage.Filter.MakeReliable();
		CurrentMessage.Filter.IgnorePredictionCull();
		strcpy(CurrentMessage.Name, name);
		CurrentMessage.Buffer.WriteString(name);
		return 0;
	}

	[LuaFunction]
	static int End(ILuaInterface lua) {
		if (CurrentMessage == null) {
			g_Lua!.ErrorFromLua("Calling umsg.End without a valid message!\n");
			return 0;
		}

		if (CurrentMessage.Filter == null) {
			g_Lua!.ErrorFromLua("Calling umsg.End without a valid RecipientFilter!?\n");
			CurrentMessage = null;
			return 0;
		}

		UserMessageManager.g_UserMessageManager.AddToQueue(CurrentMessage);
		CurrentMessage = null;
		return 0;
	}

	[LuaFunction]
	static int Angle(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteBitAngles(LuaAngle.Get_Angle(1));
		return 0;
	}

	[LuaFunction]
	static int Bool(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteOneBit(g_Lua!.GetBool(1) ? 1 : 0);
		return 0;
	}

	[LuaFunction]
	static int Char(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteChar((int)g_Lua!.GetNumber(1));
		return 0;
	}

	[LuaFunction]
	static int Entity(ILuaInterface lua) {
		if (CurrentMessage != null) {
			BaseEntity? ent = LuaEntity.Get_Entity(1, true);
			uint value = 0x7FFFFF;
			if (ent != null) {
				uint handle = ent.GetRefEHandle().Index;
				value = (handle >> 14 & 0x3FF) << 13 | handle & 0x3FFF;
			}
			CurrentMessage.Buffer!.WriteLong((int)value);
		}
		return 0;
	}

	[LuaFunction]
	static int Float(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteFloat((float)g_Lua!.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int Long(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteLong((int)g_Lua!.GetNumber(1));
		return 0;
	}

	[LuaFunction]
	static int PoolString(ILuaInterface lua) {
		NetworkString.Add(g_Lua!.CheckString(1));
		return 0;
	}

	[LuaFunction]
	static int Short(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteShort((int)g_Lua!.GetNumber(1));
		return 0;
	}

	[LuaFunction]
	static int String(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteString(g_Lua!.GetString(1));
		return 0;
	}

	[LuaFunction]
	static int Vector(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteBitVec3Coord(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int VectorNormal(ILuaInterface lua) {
		if (CurrentMessage != null)
			CurrentMessage.Buffer!.WriteBitVec3Normal(LuaVector.Get_Vector(1));
		return 0;
	}
}
