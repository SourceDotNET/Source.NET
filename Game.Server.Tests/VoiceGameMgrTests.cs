using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Server;

using System.Reflection;
using System.Reflection.Emit;

using static Game.Server.VoiceGameMgrGlobals;

namespace Game.Server.Tests;

public static class FakeEngineCalls
{
	public static readonly Edict?[] Edicts = new Edict?[130];
	public static readonly List<(string Name, byte[] Data)> Messages = [];
	public static readonly List<string> ServerCommands = [];
	static string? messageName;
	static byte[]? messageData;
	static bf_write? messageBuffer;

	public static Edict? PEntityOfEntIndex(int index) => index >= 0 && index < Edicts.Length ? Edicts[index] : null;
	public static int IndexOfEdict(Edict? edict) => edict?.EdictIndex ?? 0;
	public static int GetPlayerUserId(Edict e) => 100 + e.EdictIndex;
	public static void ServerCommand(ReadOnlySpan<char> str) => ServerCommands.Add(str.ToString());
	public static Source.Common.GarrysMod.IGMODDataTable GMOD_CreateDataTable() => null!;

	public static bf_write UserMessageBegin(in IRecipientFilter filter, int msgType) {
		messageName = SourceDllMain.usermessages.GetUserMessageName(msgType).ToString();
		messageData = new byte[256];
		messageBuffer = new bf_write(messageData, messageData.Length);
		return messageBuffer;
	}

	public static void MessageEnd() {
		Messages.Add((messageName!, messageData![..messageBuffer!.BytesWritten]));
		messageName = null;
		messageData = null;
		messageBuffer = null;
	}

	public static void Reset() {
		Array.Clear(Edicts);
		Messages.Clear();
		ServerCommands.Clear();
	}
}

// Mocking libraries cannot implement ReadOnlySpan parameters, so each method calls the same static method in calls or throws
public static class FakeInterface
{
	public static T Create<T>(Type calls) where T : class {
		string name = $"Fake{typeof(T).Name}";
		AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
		ModuleBuilder module = assembly.DefineDynamicModule(name);
		TypeBuilder type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed, typeof(object), [typeof(T)]);

		foreach (Type iface in typeof(T).GetInterfaces().Prepend(typeof(T))) {
			foreach (MethodInfo method in iface.GetMethods()) {
				ParameterInfo[] parameters = method.GetParameters();
				Type[] types = [.. parameters.Select(p => p.ParameterType)];
				MethodBuilder impl = type.DefineMethod($"{iface.Name}.{method.Name}",
					MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
					CallingConventions.HasThis);

				Type[] genericArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : [];
				GenericTypeParameterBuilder[] genericParams = genericArgs.Length > 0 ? impl.DefineGenericParameters([.. genericArgs.Select(g => g.Name)]) : [];
				for (int i = 0; i < genericParams.Length; i++) {
					genericParams[i].SetGenericParameterAttributes(genericArgs[i].GenericParameterAttributes);
					genericParams[i].SetInterfaceConstraints([.. genericArgs[i].GetGenericParameterConstraints()]);
				}

				Type Map(Type t) => t.IsByRef ? Map(t.GetElementType()!).MakeByRefType() : t.IsGenericMethodParameter ? genericParams[t.GenericParameterPosition] : t;

				impl.SetSignature(Map(method.ReturnType),
					method.ReturnParameter.GetRequiredCustomModifiers(), method.ReturnParameter.GetOptionalCustomModifiers(),
					[.. types.Select(Map)], [.. parameters.Select(p => p.GetRequiredCustomModifiers())], [.. parameters.Select(p => p.GetOptionalCustomModifiers())]);

				ILGenerator il = impl.GetILGenerator();
				MethodInfo? target = genericArgs.Length > 0 ? null : calls.GetMethod(method.Name, BindingFlags.Public | BindingFlags.Static, types);
				if (target != null && target.ReturnType == method.ReturnType) {
					for (int i = 0; i < parameters.Length; i++)
						il.Emit(OpCodes.Ldarg, i + 1);
					il.Emit(OpCodes.Call, target);
					il.Emit(OpCodes.Ret);
				}
				else {
					il.Emit(OpCodes.Ldstr, $"{iface.Name}.{method.Name}");
					il.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor([typeof(string)])!);
					il.Emit(OpCodes.Throw);
				}

				type.DefineMethodOverride(impl, method);
			}
		}

		return (T)Activator.CreateInstance(type.CreateType())!;
	}
}

public static class FakeCvarCalls
{
	public static void CallGlobalChangeCallbacks(ConVar var, in ConVarChangeContext ctx) { }
}

public class FakeVoiceServer : IVoiceServer
{
	public readonly bool[,] Listening = new bool[130, 130];
	public readonly bool[,] Proximity = new bool[130, 130];
	public readonly List<(int Receiver, int Sender)> Queries = [];

	public bool GetClientListening(int receiver, int sender) {
		Queries.Add((receiver, sender));
		return Listening[receiver, sender];
	}

	public bool GetClientProximity(int receiver, int sender) => Proximity[receiver, sender];

	public bool SetClientListening(int receiver, int sender, bool listen) {
		Listening[receiver, sender] = listen;
		return true;
	}

	public bool SetClientProximity(int receiver, int sender, bool useProximity) {
		Proximity[receiver, sender] = useProximity;
		return true;
	}
}

public class FakeVoiceHelper : IVoiceGameMgrHelper
{
	public readonly List<(double Time, int Listener, int Talker)> Calls = [];
	public bool CanHear = true;
	public bool UseProximity;

	public bool CanPlayerHearPlayer(BasePlayer listener, BasePlayer talker, ref bool proximity) {
		Calls.Add((SourceDllMain.gpGlobals.CurTime, listener.EntIndex(), talker.EntIndex()));
		proximity = UseProximity;
		return CanHear;
	}
}

[CollectionDefinition(nameof(ServerGlobalsCollection), DisableParallelization = true)]
public class ServerGlobalsCollection;

[Collection(nameof(ServerGlobalsCollection))]
public class VoiceGameMgrTests
{
	static readonly IEngineServer FakeEngine = FakeInterface.Create<IEngineServer>(typeof(FakeEngineCalls));
	static readonly GlobalEntityList EntityList = new();
	static readonly IServiceProvider Provider = new ServiceCollection()
		.AddSingleton<Game.Shared.BaseEntityList>(EntityList)
		.AddSingleton(EntityList)
		.AddSingleton(FakeInterface.Create<ICvar>(typeof(FakeCvarCalls)))
		.BuildServiceProvider();

	readonly VoiceGameMgr Mgr = new();
	readonly FakeVoiceHelper Helper = new();
	readonly FakeVoiceServer VoiceServer = new();
	readonly ServerGlobalVariables Globals = new();
	readonly BasePlayer[] Players;

	public VoiceGameMgrTests() {
		FakeEngineCalls.Reset();
		GameRules.g_pGameRules = (GameRules)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GarrysMod.GMODGameRules));
		typeof(Source.Common.Engine.ImportUtils).GetProperty("EngineProvider", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Provider);
		SetGlobal(nameof(SourceDllMain.engine), FakeEngine);
		SetGlobal(nameof(SourceDllMain.gpGlobals), Globals);
		SetGlobal(nameof(SourceDllMain.g_pVoiceServer), VoiceServer);
		SetGlobal(nameof(SourceDllMain.usermessages), new Game.Shared.UserMessages());

		g_PlayerModEnable = default;
		g_bWantModEnable = default;
		Array.Clear(g_BanMasks);
		Array.Clear(g_SentGameRulesMasks);
		Array.Clear(g_SentBanMasks);
		sv_alltalk.SetValue(0);

		Globals.MaxClients = 2;
		Globals.CurTime = 100;
		Players = [CreatePlayer(1), CreatePlayer(2)];
		g_PlayerModEnable.Set(0, true);
		g_PlayerModEnable.Set(1, true);

		Mgr.Init(Helper, Globals.MaxClients);
	}

	static void SetGlobal(string name, object value) => typeof(SourceDllMain).GetProperty(name)!.SetValue(null, value);

	static BasePlayer CreatePlayer(int index) {
		Edict edict = new() { EdictIndex = index };
		BasePlayer player = new();
		player.NetworkProp().AttachEdict(edict);
		FakeEngineCalls.Edicts[index] = edict;
		return player;
	}

	void Advance(double seconds) {
		Globals.CurTime += seconds;
		Mgr.Update(seconds);
	}

	void ReceiveVoicePacket(int index) => Mgr.OnReceivedVoicePacket(Players[index - 1]);

	static (int Listener, int Talker)[] Pairs(IEnumerable<(double Time, int Listener, int Talker)> calls)
		=> [.. calls.Select(c => (c.Listener, c.Talker))];

	[Fact]
	public void NobodyTalking_FullUpdateEveryFiveSeconds() {
		for (int i = 0; i < 80; i++)
			Advance(0.125);

		Assert.Equal([105.0, 105.0, 105.0, 105.0, 110.0, 110.0, 110.0, 110.0], Helper.Calls.Select(c => c.Time));
		Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], Pairs(Helper.Calls.Take(4)));
	}

	[Fact]
	public void FirstVoicePacket_UpdatesRightAway_OnlyForTheTalker() {
		ReceiveVoicePacket(1);

		Assert.Equal([(1, 1), (2, 1)], Pairs(Helper.Calls));
		Assert.Equal([(1, 2), (2, 2)], VoiceServer.Queries);
	}

	[Fact]
	public void VoicePacketWithinPointThreeSeconds_DoesNotUpdateAgain() {
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();

		Globals.CurTime += 0.25;
		ReceiveVoicePacket(1);

		Assert.Empty(Helper.Calls);
	}

	[Fact]
	public void Talking_PartialUpdateAfterPointThreeSeconds() {
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();
		VoiceServer.Queries.Clear();

		Advance(0.25);
		ReceiveVoicePacket(1);
		Assert.Empty(Helper.Calls);

		Advance(0.25);
		Assert.Equal([(1, 1), (2, 1)], Pairs(Helper.Calls));
		Assert.Equal([(1, 2), (2, 2)], VoiceServer.Queries);
	}

	[Fact]
	public void SilenceAfterTalking_NoPartialUpdates() {
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();

		for (int i = 0; i < 32; i++)
			Advance(0.125);

		Assert.Empty(Helper.Calls);
	}

	[Fact]
	public void PlayerWhoTalkedWithinOneSecond_IsStillAsked() {
		ReceiveVoicePacket(2);
		Advance(0.25);
		ReceiveVoicePacket(1);
		Advance(0.25);
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();
		VoiceServer.Queries.Clear();

		Advance(0.25);

		Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], Pairs(Helper.Calls));
		Assert.Empty(VoiceServer.Queries);
	}

	[Fact]
	public void PlayerWhoTalkedOneSecondAgo_UsesTheEngineState() {
		ReceiveVoicePacket(2);
		Advance(1.0);
		ReceiveVoicePacket(1);
		Advance(0.25);
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();
		VoiceServer.Queries.Clear();

		Advance(0.25);

		Assert.Equal([(1, 1), (2, 1)], Pairs(Helper.Calls));
		Assert.Equal([(1, 2), (2, 2)], VoiceServer.Queries);
	}

	[Fact]
	public void Talking_FullUpdateEveryFiveSeconds() {
		for (int i = 0; i < 24; i++) {
			ReceiveVoicePacket(1);
			Advance(0.25);
		}

		Assert.Contains(Helper.Calls, c => c.Talker == 2 && c.Time == 105.5);
		Assert.Single(Helper.Calls, c => c.Listener == 1 && c.Talker == 2);
	}

	[Fact]
	public void MoreThanThreeSecondsSinceLastUpdate_IsAFullUpdate() {
		ReceiveVoicePacket(1);
		Helper.Calls.Clear();

		Globals.CurTime += 0.25;
		Mgr.Update(3.25);

		Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], Pairs(Helper.Calls));
	}

	[Fact]
	public void PartialUpdate_KeepsWhatTheEngineHas() {
		VoiceServer.Listening[1, 2] = true;
		VoiceServer.Proximity[1, 2] = true;
		VoiceServer.Listening[2, 2] = false;

		ReceiveVoicePacket(1);

		Assert.True(VoiceServer.Listening[1, 2]);
		Assert.True(VoiceServer.Proximity[1, 2]);
		Assert.False(VoiceServer.Listening[2, 2]);
	}

	[Fact]
	public void AllTalk_IsNotChecked() {
		sv_alltalk.SetValue(1);
		Helper.CanHear = false;

		for (int i = 0; i < 40; i++)
			Advance(0.125);

		Assert.False(VoiceServer.Listening[1, 2]);
		Assert.False(VoiceServer.Listening[2, 1]);
	}

	[Fact]
	public void VoiceMask_SentOnlyWhenItChanges() {
		Helper.UseProximity = true;
		for (int i = 0; i < 80; i++)
			Advance(0.125);

		(string Name, byte[] Data)[] masks = [.. FakeEngineCalls.Messages.Where(m => m.Name == "VoiceMask")];
		Assert.Equal(2, masks.Length);
		Assert.Equal(33, masks[0].Data.Length);
		Assert.Equal(0b11, BitConverter.ToInt32(masks[0].Data, 0));
		Assert.Equal(0, BitConverter.ToInt32(masks[0].Data, 4));
		Assert.Equal(1, masks[0].Data[32]);
		Assert.True(VoiceServer.Proximity[1, 2]);
	}

	[Fact]
	public void RequestState_SentOnceAfterConnect() {
		Mgr.ClientConnected(Players[0].Edict());

		for (int i = 0; i < 80; i++)
			Advance(0.125);

		Assert.Single(FakeEngineCalls.Messages, m => m.Name == "RequestState");
	}

	[Fact]
	public void ClientConnected_IgnoresNullAndOutOfRangeEdicts() {
		Mgr.ClientConnected(null);
		Mgr.ClientConnected(new Edict { EdictIndex = 129 });
		Mgr.ClientConnected(new Edict { EdictIndex = 0 });

		Assert.True(g_bWantModEnable.IsAllClear());
	}

	[Fact]
	public void VoicePacketFromNonPlayer_IsIgnored() {
		Mgr.OnReceivedVoicePacket(null);

		Assert.Empty(Helper.Calls);
	}

	[Fact]
	public void TooManyVoicePacketsInOneFrame_KicksOnce() {
		for (int i = 0; i < 48; i++)
			ReceiveVoicePacket(1);
		Assert.Empty(FakeEngineCalls.ServerCommands);

		ReceiveVoicePacket(1);
		ReceiveVoicePacket(1);

		Assert.Equal(["kickid 101 \"Too many voice packets (49) in a single frame!\"\n"], FakeEngineCalls.ServerCommands);
		Assert.True(Players[0].Kicked);
	}

	[Fact]
	public void VoicePacketCount_ResetsEveryFrame() {
		for (int frame = 0; frame < 10; frame++) {
			for (int i = 0; i < 48; i++)
				ReceiveVoicePacket(1);
			Globals.CurTime += 0.015;
		}

		Assert.Empty(FakeEngineCalls.ServerCommands);
	}

	[Fact]
	public void VoicePacketLimitZero_NeverKicks() {
		ConVar limit = (ConVar)typeof(BasePlayer).GetField("sv_maxvoicepacketsperframe", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
		limit.SetValue(0);
		try {
			for (int i = 0; i < 100; i++)
				ReceiveVoicePacket(1);
		}
		finally {
			limit.SetValue(48);
		}

		Assert.Empty(FakeEngineCalls.ServerCommands);
	}
}
