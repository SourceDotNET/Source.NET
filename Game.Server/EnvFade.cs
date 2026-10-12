using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;

namespace Game.Server;

using DEFINE = Source.DEFINE<EnvFade>;

[LinkEntityToClass("env_fade")]
public class EnvFade : LogicalEntity
{
	float Duration;
	float HoldTime;

	public OutputEvent OnBeginFade = new();

	public static readonly new DataMap DataDesc = new(typeof(EnvFade), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Duration), FieldType.Float, "duration"),
		DEFINE.KEYFIELD(nameof(HoldTime), FieldType.Float, "holdtime"),

		DEFINE.INPUTFUNC(FieldType.Void, "Fade", nameof(InputFade), (INPUTFUNCPTR)((self, data) => ((EnvFade)self).InputFade(data))),

		DEFINE.OUTPUT(nameof(OnBeginFade), "OnBeginFade", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public float GetDuration() => Duration;
	public float GetHoldTime() => HoldTime;

	public void SetDuration(float duration) => Duration = duration;
	public void SetHoldTime(float hold) => HoldTime = hold;

	[Flags]
	public enum SF
	{
		/// <summary>
		/// Fade in, not out
		/// </summary>
		In = 0x0001,
		/// <summary>
		/// Modulate, don't blend
		/// </summary>
		Modulate = 0x0002,
		OnlyOne = 0x0004,
		StayOut = 0x0008,
	}

	public override void Spawn() { }

	/// <summary>
	/// Input handler that does the screen fade.
	/// </summary>
	public void InputFade(InputData inputdata) {
		FadeFlags fadeFlags = 0;

		if (HasSpawnFlags(SF.In))
			fadeFlags |= FadeFlags.In;
		else
			fadeFlags |= FadeFlags.Out;

		if (HasSpawnFlags(SF.Modulate))
			fadeFlags |= FadeFlags.Modulate;

		if (HasSpawnFlags(SF.StayOut))
			fadeFlags |= FadeFlags.StayOut;

		if (HasSpawnFlags(SF.OnlyOne)) {
			if (inputdata.Activator != null && inputdata.Activator.IsNetClient())
				Util.ScreenFade(inputdata.Activator, ColorRender, GetDuration(), GetHoldTime(), fadeFlags);
		}
		else
			Util.ScreenFadeAll(ColorRender, GetDuration(), GetHoldTime(), fadeFlags | FadeFlags.Purge);

		OnBeginFade.FireOutput(inputdata.Activator, this);
	}

	/// <summary>
	/// Fetches the arguments from the command line for the fadein and fadeout
	/// console commands.
	/// </summary>
	/// <param name="time">Returns the fade time in seconds (the time to fade in or out)</param>
	/// <param name="clrFade">Returns the color to fade to or from.</param>
	static void GetFadeParms(in TokenizedCommand args, out float time, out Color clrFade) {
		time = 2.0f;

		if (args.ArgC() > 1)
			time = strtof(args[1], out _);

		int r = 0, g = 0, b = 0, a = 255;

		if (args.ArgC() > 4) {
			r = atoi(args[2]);
			g = atoi(args[3]);
			b = atoi(args[4]);

			if (args.ArgC() == 5)
				a = atoi(args[5]);
		}

		clrFade = new(r, g, b, a);
	}

	/// <summary>
	/// Console command to fade out to a given color.
	/// </summary>
	[ConCommand("fadeout", "fadeout {time r g b}: Fades the screen to black or to the specified color over the given number of seconds.", FCvar.Cheat)]
	static void CC_FadeOut(in TokenizedCommand args) {
		GetFadeParms(in args, out float time, out Color clrFade);

		BasePlayer? player = Util.GetCommandClient();
		Util.ScreenFade(player, clrFade, time, 0, FadeFlags.Out | FadeFlags.Purge | FadeFlags.StayOut);
	}

	/// <summary>
	/// Console command to fade in from a given color.
	/// </summary>
	[ConCommand("fadein", "fadein {time r g b}: Fades the screen in from black or from the specified color over the given number of seconds.", FCvar.Cheat)]
	static void CC_FadeIn(in TokenizedCommand args) {
		GetFadeParms(in args, out float time, out Color clrFade);

		BasePlayer? player = Util.GetCommandClient();
		Util.ScreenFade(player, clrFade, time, 0, FadeFlags.In | FadeFlags.Purge);
	}
}
