using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;

using System;
using System.Collections.Generic;
using System.Text;

using static Game.Server.FilterMultiple;

namespace Game.Server;

public class Message : PointEntity
{
	[Flags]
	public enum SF
	{
		Once = 0x0001,
		All = 0x0002,
	}

	public override void Spawn() {
		Precache();

		SetSolid(SolidType.None);
		SetMoveType(Source.MoveType.None);

		switch (MessageAttenuation) {
			case 1: // Medium radius
				Radius = ATTN_STATIC;
				break;

			case 2: // Large radius
				Radius = ATTN_NORM;
				break;

			case 3: //EVERYWHERE
				Radius = ATTN_NONE;
				break;

			default:
			case 0: // Small radius
				Radius = (int)SoundLevel.LvlIdle;
				break;
		}

		MessageAttenuation = 0;

		// Remap volume from [0,10] to [0,1].
		MessageVolume *= 0.1f;

		// No volume, use normal
		if (MessageVolume <= 0)
			MessageVolume = 1.0f;
	}

	public override void Precache() {
		if (Noise != null)
			PrecacheScriptSound(Noise);
	}

	public void SetMessage(ReadOnlySpan<char> message) {
		MessageText = new(message.SliceNullTerminatedString());
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		InputData inputdata = default;

		inputdata.Activator = null;
		inputdata.Caller = null;

		InputShowMessage(ref inputdata);
	}

	private void InputShowMessage(ref InputData inputData) {
		BaseEntity? player = null;

		if (HasSpawnFlags(SF.All))
			Util.ShowMessageAll(MessageText);
		else {
			if (inputData.Activator != null && inputData.Activator.IsPlayer())
				player = inputData.Activator;
			else
				player = (gpGlobals.MaxClients > 1) ? null : Util.GetLocalPlayer();

			if (player != null && player.IsPlayer())
				Util.ShowMessage(MessageText, ToBasePlayer(player));
		}

		if (Noise != null) {

			PASAttenuationFilter filter = new(this);

			EmitSound_t ep = default;
			ep.Channel = (int)SoundEntityChannel.Body;
			ep.SoundName = Noise;
			ep.Volume = MessageVolume;
			ep.SoundLevel = ATTN_TO_SNDLVL(Radius);

			EmitSound(filter, EntIndex(), ep);
		}

		if (HasSpawnFlags(SF.Once))
			Util.Remove(this);

		OnShowMessage.FireOutput(inputData.Activator, this);
	}

	private string MessageText = "";
	private float MessageVolume;
	private int MessageAttenuation;
	private float Radius;

	private string? Noise;
	private readonly OutputEvent OnShowMessage = new();
}

[LinkEntityToClass("env_credits")]
public class Credits : PointEntity
{
	public override void Spawn() {
		SetSolid(SolidType.None);
		SetMoveType(Source.MoveType.None);
	}

	[ConCommand("creditsdone")]
	static void CreditsDone_f() {
		Credits? credits = (Credits?)gEntList.FindEntityByClassname(null, "env_credits");
		credits?.OnCreditsDone.FireOutput(credits, credits);
	}

	public void InputRollCredits(ref InputData inputdata) {
		BasePlayer player = Util.GetLocalPlayer()!;

		SingleUserRecipientFilter user = new(player);
		user.MakeReliable();

		UserMessageBegin(user, "CreditsMsg");
		WRITE_BYTE(2);
		MessageEnd();
	}
	public void InputRollOutroCredits(ref InputData inputdata) {
		RollOutroCredits();

		RolledOutroCredits = true;

		gamestats.Event_Credits();
	}
	public void InputShowLogo(ref InputData inputdata) {
		BasePlayer player = Util.GetLocalPlayer()!;

		SingleUserRecipientFilter user = new(player);
		user.MakeReliable();

		if (LogoLength) {
			UserMessageBegin(user, "LogoTimeMsg");
			WRITE_FLOAT(LogoLength ? 1 : 0);
			MessageEnd();
		}
		else {
			UserMessageBegin(user, "CreditsMsg");
			WRITE_BYTE(1);
			MessageEnd();
		}
	}
	public void InputSetLogoLength(ref InputData inputdata) {
		LogoLength = inputdata.Value.Float() != 0;
	}

	public readonly OutputEvent OnCreditsDone = new();

	public override void OnRestore() {
		base.OnRestore();
		if (RolledOutroCredits)
			RollOutroCredits();
	}

	private void RollOutroCredits() {
		sv_unlockedchapters.SetValue("15");

		BasePlayer player = Util.GetLocalPlayer()!;

		SingleUserRecipientFilter user = new(player);
		user.MakeReliable();

		UserMessageBegin(user, "CreditsMsg");
		WRITE_BYTE(3);
		MessageEnd();
	}

	private bool RolledOutroCredits;
	private bool LogoLength;

	public static readonly new DataMap DataDesc = new(typeof(FilterMultiple), BaseFilter.DataDesc, [
		DEFINE<Credits>.INPUTFUNC( FieldType.Void, "RollCredits", nameof(InputRollCredits), (INPUTFUNCPTR)((self, data) => ((Credits)self).InputRollCredits(ref data))),
		DEFINE<Credits>.INPUTFUNC( FieldType.Void, "RollOutroCredits", nameof(InputRollOutroCredits), (INPUTFUNCPTR)((self, data) => ((Credits)self).InputRollOutroCredits(ref data))),
		DEFINE<Credits>.INPUTFUNC( FieldType.Void, "ShowLogo", nameof(InputShowLogo), (INPUTFUNCPTR)((self, data) => ((Credits)self).InputShowLogo(ref data))),
		DEFINE<Credits>.INPUTFUNC( FieldType.Float, "SetLogoLength", nameof(InputSetLogoLength), (INPUTFUNCPTR)((self, data) => ((Credits)self).InputSetLogoLength(ref data))),
		DEFINE<Credits>.OUTPUT( nameof(OnCreditsDone), "OnCreditsDone", eventFuncs),
		DEFINE<Credits>.FIELD( nameof(RolledOutroCredits), FieldType.Boolean ),
		DEFINE<Credits>.FIELD( nameof(LogoLength), FieldType.Float )
	]);
	public override DataMap? GetDataDescMap() => DataDesc;
}
