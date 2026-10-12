using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

using FIELD = Source.FIELD<Game.Client.C_BaseFlex>;
using EventType = Game.Shared.ChoreoEvent.EventType;

namespace Game.Client;

public class FlexSceneFileManager() : AutoGameSystem("CFlexSceneFileManager")
{
	public static readonly FlexSceneFileManager g_FlexSceneFileManager = new();

	class FlexSceneFile
	{
		public const int MAX_FLEX_FILENAME = 128;

		public string Filename = "";
		public FlexSettingHdr? Buffer;
	}

	readonly List<FlexSceneFile> FileList = [];

	public override bool Init() {
		FindSceneFile(null, "phonemes", true);
		FindSceneFile(null, "phonemes_weak", true);
		FindSceneFile(null, "phonemes_strong", true);

#if HL2_DLL
		FindSceneFile(null, "random", true);
		FindSceneFile(null, "randomAlert", true);
#endif

		string[] tfClasses = [
			"scout",
			"sniper",
			"soldier",
			"demo",
			"medic",
			"heavy",
			"pyro",
			"spy",
			"engineer",
		];

		for (int i = 0; i < tfClasses.Length; ++i) {
			FindSceneFile(null, $"player/{tfClasses[i]}/phonemes/phonemes", true);
			FindSceneFile(null, $"player/{tfClasses[i]}/phonemes/phonemes_weak", true);
			FindSceneFile(null, $"player/{tfClasses[i]}/phonemes/phonemes_strong", true);

			FindSceneFile(null, $"player/hwm/{tfClasses[i]}/phonemes/phonemes", true);
			FindSceneFile(null, $"player/hwm/{tfClasses[i]}/phonemes/phonemes_weak", true);
			FindSceneFile(null, $"player/hwm/{tfClasses[i]}/phonemes/phonemes_strong", true);

			FindSceneFile(null, $"player/{tfClasses[i]}/emotion/emotion", true);
			FindSceneFile(null, $"player/hwm/{tfClasses[i]}/emotion/emotion", true);
		}

		return true;
	}

	public override void Shutdown() => DeleteSceneFiles();

	static void EnsureTranslations(IHasLocalToGlobalFlexSettings? instance, FlexSettingHdr settinghdr) {
		instance?.EnsureTranslations(settinghdr);
	}

	public FlexSettingHdr? FindSceneFile(IHasLocalToGlobalFlexSettings? instance, ReadOnlySpan<char> filename, bool allowBlockingIO) {
		Span<char> fixedFilename = stackalloc char[filename.Length];
		filename.CopyTo(fixedFilename);
		StrTools.FixSlashes(fixedFilename);
		ReadOnlySpan<char> szFilename = fixedFilename;

		int i;
		for (i = 0; i < FileList.Count; i++) {
			FlexSceneFile? file = FileList[i];
			if (file != null && stricmp(file.Filename, szFilename) == 0) {
				EnsureTranslations(instance, file.Buffer!);
				return file.Buffer;
			}
		}

		if (!allowBlockingIO)
			return null;

		byte[] buffer;
		using (IFileHandle? handle = filesystem.Open($"expressions/{szFilename}.vfe", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME")) {
			if (handle == null)
				return null;

			buffer = new byte[handle.Stream.Length];
			handle.Stream.ReadExactly(buffer);
		}

		if (buffer.Length == 0)
			return null;

		FlexSceneFile pfile = new();
		pfile.Filename = new(szFilename[..Math.Min(szFilename.Length, FlexSceneFile.MAX_FLEX_FILENAME - 1)]);
		pfile.Buffer = new FlexSettingHdr(buffer);
		FileList.Add(pfile);

		EnsureTranslations(instance, pfile.Buffer);

		return pfile.Buffer;
	}

	void DeleteSceneFiles() => FileList.Clear();
}

public enum PhonemeClass
{
	Weak = 0,
	Normal,
	Strong,

	NumPhonemeClasses
}

public interface IHasLocalToGlobalFlexSettings
{
	void EnsureTranslations(FlexSettingHdr settinghdr);
}

public class EmphasizedPhoneme
{
	public string Classname = "";
	public bool Required;
	public bool BaseChecked;
	public FlexSettingHdr? Base;
	public FlexSetting? Exp;
	public bool Valid;
	public float Amount;
}

[NetworkName("CBaseFlex")]
public partial class C_BaseFlex : C_BaseAnimatingOverlay, IHasLocalToGlobalFlexSettings
{
	static readonly ConVar g_CV_PhonemeDelay = new("phonemedelay", "0", FCvar.None, "Phoneme delay to account for sound system latency.");
	static readonly ConVar g_CV_PhonemeFilter = new("phonemefilter", "0.08", FCvar.None, "Time duration of box filter to pass over phonemes.");
	static readonly ConVar g_CV_FlexRules = new("flex_rules", "1", FCvar.None, "Allow flex animation rules to run.");
	static readonly ConVar g_CV_BlinkDuration = new("blink_duration", "0.2", FCvar.None, "How many seconds an eye blink will last.");
	static readonly ConVar g_CV_FlexSmooth = new("flex_smooth", "1", FCvar.None, "Applies smoothing/decay curve to flex animation controller changes.");
	static readonly ConVar g_CV_PhonemeSnap = new("phonemesnap", "2", FCvar.None, "Lod at level at which visemes stops always considering two phonemes, regardless of duration.");

	const float STRONG_CROSSFADE_START = 0.60f;
	const float WEAK_CROSSFADE_START = 0.40f;

	readonly Source.Common.Audio.MouthInfo mouth = new();
	public override Source.Common.Audio.MouthInfo? GetMouth() => mouth;

	public static readonly RecvTable DT_BaseFlex = new(DT_BaseAnimatingOverlay, [
		RecvPropArray3  (FIELD.OF_ARRAY(nameof(FlexWeight)), RecvPropFloat(FIELD.OF_ARRAY(nameof(FlexWeight)))),
		RecvPropInt     (FIELD.OF(nameof(BlinkToggle))),
		RecvPropVector  (FIELD.OF(nameof(ViewTarget))),

		RecvPropFloat   ( FIELD.OF_VECTORELEM(nameof(ViewOffset), 0)),
		RecvPropFloat   ( FIELD.OF_VECTORELEM(nameof(ViewOffset), 1)),
		RecvPropFloat   ( FIELD.OF_VECTORELEM(nameof(ViewOffset), 2)),

		RecvPropVector  ( FIELD.OF(nameof(Lean))),
		RecvPropVector  ( FIELD.OF(nameof(Shift))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_BaseFlex);
	[NetworkName("m_flexWeight")]
	public InlineArray96<float> FlexWeight;
	[NetworkName("m_blinktoggle")]
	public int BlinkToggle;
	[NetworkName("m_viewtarget")]
	public Vector3 ViewTarget;
	[NetworkName("m_vecLean")]
	public Vector3 Lean;
	[NetworkName("m_vecShift")]
	public Vector3 Shift;

	static readonly DynamicAccessor DA_ViewTarget = FIELD.OF(nameof(ViewTarget));
	static readonly DynamicAccessor DA_FlexWeight = FIELD.OF_ARRAY(nameof(FlexWeight));
#if HL2_DLL
	static readonly DynamicAccessor DA_Lean = FIELD.OF(nameof(Lean));
	static readonly DynamicAccessor DA_Shift = FIELD.OF(nameof(Shift));
#endif

	public readonly InterpolatedVar<Vector3> IV_ViewTarget = new($"{nameof(C_BaseFlex)}.{nameof(IV_ViewTarget)}");
	public readonly InterpolatedVarArray<float> IV_FlexWeight = new(Studio.MAXSTUDIOFLEXCTRL, $"{nameof(C_BaseFlex)}.{nameof(IV_FlexWeight)}");
#if HL2_DLL
	public readonly InterpolatedVar<Vector3> IV_Lean = new($"{nameof(C_BaseFlex)}.{nameof(IV_Lean)}");
	public readonly InterpolatedVar<Vector3> IV_Shift = new($"{nameof(C_BaseFlex)}.{nameof(IV_Shift)}");
#endif

	readonly Dictionary<FlexSettingHdr, int[]> LocalToGlobal = [];
	readonly List<SceneEventInfo> SceneEvents = [];
	readonly List<ChoreoScene> ActiveChoreoScenes = [];

	TimeUnit_t BlinkTime;
	int PrevBlinkToggle;

	int Blink;
	LocalFlexController EyeUpdown;
	LocalFlexController EyeRightleft;
	bool SearchedForEyeFlexes;
	int MouthAttachment;

	TimeUnit_t FlexDelayTime;
	float[]? FlexDelayedWeight;
	int FlexDelayedWeightCount;

	static int g_numflexcontrollers;
	static readonly string[] g_flexcontroller = new string[Studio.MAXSTUDIOFLEXCTRL * 4];
	static readonly float[] g_flexweight = new float[Studio.MAXSTUDIOFLEXDESC];

	public readonly EmphasizedPhoneme[] PhonemeClasses = new EmphasizedPhoneme[(int)PhonemeClass.NumPhonemeClasses];

	public C_BaseFlex() {
		AddVar(this, DA_ViewTarget, IV_ViewTarget, LatchFlags.LatchAnimationVar | LatchFlags.InterpolateLinearOnly, true);
		AddVar(this, DA_FlexWeight, IV_FlexWeight, LatchFlags.LatchAnimationVar, true);

		SetupMappings("phonemes");

#if HL2_DLL
		AddVar(this, DA_Lean, IV_Lean, LatchFlags.LatchAnimationVar, true);
		AddVar(this, DA_Shift, IV_Shift, LatchFlags.LatchAnimationVar, true);
#endif
	}

	public override void Spawn() {
		base.Spawn();

		InitPhonemeMappings();
	}

	public virtual void InitPhonemeMappings() {
		SetupMappings("phonemes");
	}

	public void SetupMappings(ReadOnlySpan<char> fileRoot) {
		for (int i = 0; i < PhonemeClasses.Length; i++)
			PhonemeClasses[i] = new();

		EmphasizedPhoneme normal = PhonemeClasses[(int)PhonemeClass.Normal];
		normal.Classname = new(fileRoot);
		normal.Required = true;

		EmphasizedPhoneme weak = PhonemeClasses[(int)PhonemeClass.Weak];
		weak.Classname = $"{fileRoot}_weak";
		EmphasizedPhoneme strong = PhonemeClasses[(int)PhonemeClass.Strong];
		strong.Classname = $"{fileRoot}_strong";
	}

	protected override StudioHdr? OnNewModel() {
		StudioHdr? hdr = base.OnNewModel();

		Blink = -1;
		EyeUpdown = (LocalFlexController)(-1);
		EyeRightleft = (LocalFlexController)(-1);
		SearchedForEyeFlexes = false;
		MouthAttachment = 0;

		FlexDelayedWeight = null;
		FlexDelayedWeightCount = 0;

		if (hdr != null) {
			if (hdr.NumFlexDesc() != 0) {
				FlexDelayedWeightCount = hdr.NumFlexDesc();
				FlexDelayedWeight = new float[FlexDelayedWeightCount];
			}

			IV_FlexWeight.SetMaxCount((int)hdr.NumFlexControllers());

			MouthAttachment = LookupAttachment("mouth");

			LinkToGlobalFlexControllers(hdr);
		}

		return hdr;
	}

	public override void StandardBlendingRules(StudioHdr hdr, Span<Vector3> pos, Span<Quaternion> q, TimeUnit_t currentTime, int boneMask) {
		base.StandardBlendingRules(hdr, pos, q, currentTime, boneMask);

#if HL2_DLL
		if (hdr.GetNumIKChains() != 0 && (Shift.X != 0.0 || Shift.Y != 0.0)) {
			MathLib.AngleMatrix(GetRenderAngles(), GetRenderOrigin(), out Matrix3x4 rootxform);

			MathLib.VectorIRotate(Shift, rootxform, out Vector3 localShift);
			MathLib.VectorIRotate(Lean, rootxform, out Vector3 localLean);

			Vector3 p0 = pos[0];
			float length = MathLib.VectorNormalize(ref p0);

			Vector3 shiftPos = pos[0] + localShift;
			MathLib.VectorNormalize(ref shiftPos);
			Vector3 leanPos = pos[0] + localLean;
			MathLib.VectorNormalize(ref leanPos);
			pos[0] = shiftPos * length;

			MathLib.CrossProduct(p0, leanPos, out Vector3 p1);
			float sinAngle = MathLib.VectorNormalize(ref p1);
			float cosAngle = MathLib.DotProduct(p0, leanPos);
			float angle = (float)(Math.Atan2(sinAngle, cosAngle) * 180 / Math.PI);
			angle = Math.Clamp(angle, -45.0f, 45.0f);
			MathLib.AxisAngleQuaternion(p1, angle, out Quaternion q1);
			MathLib.QuaternionMult(q1, q[0], out q[0]);
			MathLib.QuaternionNormalize2(ref q[0]);
		}
#endif
	}

	public override bool GetSoundSpatialization(ref SpatializationInfo info) {
		bool bret = base.GetSoundSpatialization(ref info);
		if (bret) {
			if ((info.Info.Channel == SoundEntityChannel.Voice || info.Info.Channel == SoundEntityChannel.Voice2) && MouthAttachment > 0) {
				using AutoAllowBoneAccess boneaccess = new(true, false);

				if (GetAttachment(MouthAttachment, out Vector3 origin, out QAngle angles)) {
					if (!Unsafe.IsNullRef(ref info.Origin))
						info.Origin = origin;

					if (!Unsafe.IsNullRef(ref info.Angles))
						info.Angles = angles;
				}
			}
		}

		return bret;
	}

	public static void RunFlexRules(StudioHdr? hdr, Span<float> dest) {
		if (g_CV_FlexRules.GetInt() == 0)
			return;

		if (hdr == null)
			return;

		hdr.RunFlexRules(g_flexweight, dest);
	}

	public FlexSettingHdr? FindSceneFile(ReadOnlySpan<char> filename) => FlexSceneFileManager.g_FlexSceneFileManager.FindSceneFile(this, filename, false);

	public virtual Vector3 SetViewTarget(StudioHdr? studioHdr) {
		if (studioHdr == null)
			return new(0, 0, 0);

		Vector3 tmp = ViewTarget;

		if (!OverrideViewTarget.IsZero())
			tmp = OverrideViewTarget;

		if (!SearchedForEyeFlexes) {
			SearchedForEyeFlexes = true;

			EyeUpdown = FindFlexController("eyes_updown");
			EyeRightleft = FindFlexController("eyes_rightleft");

			if (EyeUpdown != (LocalFlexController)(-1))
				studioHdr.FlexController(EyeUpdown).LocalToGlobal = AddGlobalFlexController("eyes_updown");
			if (EyeRightleft != (LocalFlexController)(-1))
				studioHdr.FlexController(EyeRightleft).LocalToGlobal = AddGlobalFlexController("eyes_rightleft");
		}

		if (EyeAttachment > 0) {
			if (!GetAttachment(EyeAttachment, out Matrix3x4 attToWorld))
				return new(0, 0, 0);

			MathLib.VectorITransform(tmp, attToWorld, out Vector3 local);

			if (local.X < 6)
				local.X = 6;
			float flDist = local.Length();
			MathLib.VectorNormalize(ref local);

			QAngle eyeAng = new(0, 0, 0);
			if (EyeUpdown != (LocalFlexController)(-1)) {
				MStudioFlexController flex = studioHdr.FlexController(EyeUpdown);
				eyeAng.X = g_flexweight[flex.LocalToGlobal];
			}

			if (EyeRightleft != (LocalFlexController)(-1)) {
				MStudioFlexController flex = studioHdr.FlexController(EyeRightleft);
				eyeAng.Y = g_flexweight[flex.LocalToGlobal];
			}

			MathLib.AngleVectors(eyeAng, out Vector3 eyeDeflect);
			eyeDeflect.X = 0;

			eyeDeflect = eyeDeflect * (local.X * local.X);
			local = local + eyeDeflect;
			MathLib.VectorNormalize(ref local);

			float flMaxEyeDeflection = studioHdr.MaxEyeDeflection();
			if (local.X < flMaxEyeDeflection) {
				local.X = 0;
				float d = local.LengthSquared();
				if (d > 0.0f) {
					d = MathF.Sqrt((1.0f - flMaxEyeDeflection * flMaxEyeDeflection) / (local.Y * local.Y + local.Z * local.Z));
					local.X = flMaxEyeDeflection;
					local.Y = local.Y * d;
					local.Z = local.Z * d;
				}
				else
					local.X = 1.0f;
			}
			local = local * flDist;
			MathLib.VectorTransform(local, attToWorld, out tmp);
		}

		modelrender.SetViewTarget(GetModelPtr()!, GetBody(), tmp);

		return tmp;
	}

	void ComputeBlendedSetting(EmphasizedPhoneme[] classes, float emphasis_intensity) {
		bool has_weak = classes[(int)PhonemeClass.Weak].Valid;
		bool has_strong = classes[(int)PhonemeClass.Strong].Valid;

		Assert(classes[(int)PhonemeClass.Normal].Valid);

		if (emphasis_intensity > STRONG_CROSSFADE_START) {
			if (has_strong) {
				float dist_remaining = 1.0f - emphasis_intensity;
				float frac = dist_remaining / (1.0f - STRONG_CROSSFADE_START);

				classes[(int)PhonemeClass.Normal].Amount = (frac) * 2.0f * STRONG_CROSSFADE_START;
				classes[(int)PhonemeClass.Strong].Amount = 1.0f - frac;
			}
			else {
				emphasis_intensity = Math.Min(emphasis_intensity, STRONG_CROSSFADE_START);
				classes[(int)PhonemeClass.Normal].Amount = 2.0f * emphasis_intensity;
			}
		}
		else if (emphasis_intensity < WEAK_CROSSFADE_START) {
			if (has_weak) {
				float dist_remaining = WEAK_CROSSFADE_START - emphasis_intensity;
				float frac = dist_remaining / (WEAK_CROSSFADE_START);

				classes[(int)PhonemeClass.Normal].Amount = (1.0f - frac) * 2.0f * WEAK_CROSSFADE_START;
				classes[(int)PhonemeClass.Weak].Amount = frac;
			}
			else {
				emphasis_intensity = Math.Max(emphasis_intensity, WEAK_CROSSFADE_START);
				classes[(int)PhonemeClass.Normal].Amount = 2.0f * emphasis_intensity;
			}
		}
		else
			classes[(int)PhonemeClass.Normal].Amount = 2.0f * emphasis_intensity;
	}

	void AddViseme(EmphasizedPhoneme[] classes, float emphasis_intensity, int phoneme, float scale, bool newexpression) {
		int type;

		bool skip = SetupEmphasisBlend(classes, phoneme);
		if (skip)
			return;

		ComputeBlendedSetting(classes, emphasis_intensity);

		for (type = 0; type < (int)PhonemeClass.NumPhonemeClasses; type++) {
			EmphasizedPhoneme info = classes[type];
			if (!info.Valid || info.Amount == 0.0f)
				continue;

			FlexSettingHdr actual_flexsetting_header = info.Base!;
			FlexSetting? setting = actual_flexsetting_header.IndexedSetting(phoneme);
			if (setting == null)
				continue;

			int truecount = setting.PSetting(0, out ReadOnlySpan<FlexSettingWeight> weights);
			for (int i = 0; i < truecount; i++) {
				int j = FlexControllerLocalToGlobal(actual_flexsetting_header, weights[i].Key);
				g_flexweight[j] += info.Amount * scale * weights[i].Weight;
			}
		}
	}

	bool SetupEmphasisBlend(EmphasizedPhoneme[] classes, int phoneme) {
		int i;

		bool skip = false;

		for (i = 0; i < (int)PhonemeClass.NumPhonemeClasses; i++) {
			EmphasizedPhoneme info = classes[i];

			info.Valid = false;
			info.Amount = 0.0f;

			if (!info.BaseChecked) {
				info.BaseChecked = true;
				info.Base = FindSceneFile(info.Classname);
			}
			info.Exp = null;
			if (info.Base != null) {
				Assert(info.Base.Id == ('V' << 16) + ('F' << 8) + ('E'));
				info.Exp = info.Base.IndexedSetting(phoneme);
			}

			if (info.Required && (info.Base == null || info.Exp == null)) {
				skip = true;
				break;
			}

			if (info.Exp != null)
				info.Valid = true;
		}

		return skip;
	}

	void AddVisemesForSentence(EmphasizedPhoneme[] classes, float emphasis_intensity, Sentence sentence, float t, float dt, bool juststarted) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return;

		int pcount = sentence.GetRuntimePhonemeCount();
		for (int k = 0; k < pcount; k++) {
			BasePhonemeTag phoneme = sentence.GetRuntimePhoneme(k);

			if (t > phoneme.GetStartTime() && t < phoneme.GetEndTime()) {
				bool bCrossfade = true;
				if ((hdr.Flags() & StudioHdrFlags.ForcePhonemeCrossfade) == 0) {
					if ((AccumulatedBoneMask & Studio.BONE_USED_BY_VERTEX_LOD0) != 0)
						bCrossfade = g_CV_PhonemeSnap.GetInt() > 0;
					else if ((AccumulatedBoneMask & Studio.BONE_USED_BY_VERTEX_LOD1) != 0)
						bCrossfade = g_CV_PhonemeSnap.GetInt() > 1;
					else if ((AccumulatedBoneMask & Studio.BONE_USED_BY_VERTEX_LOD2) != 0)
						bCrossfade = g_CV_PhonemeSnap.GetInt() > 2;
					else if ((AccumulatedBoneMask & Studio.BONE_USED_BY_VERTEX_LOD3) != 0)
						bCrossfade = g_CV_PhonemeSnap.GetInt() > 3;
					else
						bCrossfade = false;
				}

				if (bCrossfade) {
					if (k < pcount - 1) {
						BasePhonemeTag? next = sentence.GetRuntimePhoneme(k + 1);
						if (next != null) {
							if (next.GetStartTime() == phoneme.GetEndTime())
								dt = Math.Max(dt, Math.Min(next.GetEndTime() - t, phoneme.GetEndTime() - phoneme.GetStartTime()));
							else
								dt = Math.Max(dt, Math.Min(next.GetStartTime() - t, phoneme.GetEndTime() - phoneme.GetStartTime()));
						}
						else
							dt = Math.Max(dt, phoneme.GetEndTime() - phoneme.GetStartTime());
					}
				}
			}

			float t1 = (phoneme.GetStartTime() - t) / dt;
			float t2 = (phoneme.GetEndTime() - t) / dt;

			if (t1 < 1.0 && t2 > 0) {
				float scale;

				if (t2 > 1)
					t2 = 1;
				if (t1 < 0)
					t1 = 0;

				scale = (t2 - t1);

				AddViseme(classes, emphasis_intensity, phoneme.GetPhonemeCode(), scale, juststarted);
			}
		}
	}

	void ProcessVisemes(EmphasizedPhoneme[] classes) {
		if (!mouth.IsActive())
			return;

		for (int source = 0; source < mouth.GetNumVoiceSources(); source++) {
			VoiceData? vd = mouth.GetVoiceSource(source);
			if (vd == null || vd.ShouldIgnorePhonemes())
				continue;

			Sentence? sentence = engine.GetSentence(vd.GetSource());
			if (sentence == null)
				continue;

			float sentence_length = engine.GetSentenceLength(vd.GetSource());
			float timesincestart = vd.GetElapsedTime();

			if (timesincestart >= (sentence_length + 2.0f))
				continue;

			float t = timesincestart - g_CV_PhonemeDelay.GetFloat();

			float dt = g_CV_PhonemeFilter.GetFloat();

			bool juststarted = false;

			float emphasis_intensity = sentence.GetIntensity(t, sentence_length);

			AddVisemesForSentence(classes, emphasis_intensity, sentence, t, dt, juststarted);
		}
	}

	public override void OnThreadedDrawSetup() {
		if (EyeAttachment < 0)
			return;

		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return;

		CalcAttachments();
	}

	public override bool UsesFlexDelayedWeights() => FlexDelayedWeight != null && g_CV_FlexSmooth.GetBool();

	public static void LinkToGlobalFlexControllers(StudioHdr? hdr) {
		if (hdr != null && hdr.NumFlexControllers() > 0 && hdr.FlexController(0).LocalToGlobal == -1) {
			for (LocalFlexController i = 0; i < hdr.NumFlexControllers(); i++) {
				int j = AddGlobalFlexController(hdr.FlexController(i).Name());
				hdr.FlexController(i).LocalToGlobal = j;
			}
		}
	}

	public override void SetupWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) {
		LinkToGlobalFlexControllers(GetModelPtr());

		if (SetupGlobalWeights(boneToWorld, flexWeights, flexDelayedWeights))
			SetupLocalWeights(boneToWorld, flexWeights, flexDelayedWeights);

		{
			StudioHdr? hdr = GetModelPtr();
			if (hdr != null) {
				C_BaseEntity? ent = FlexManipulator.Get();
				if (ent != null) {
					if (ent is C_FlexManipulate) {
						base.SetupWeights(boneToWorld, flexWeights, flexDelayedWeights);
						SetViewTarget(hdr);
					}
				}
			}
		}
	}

	public virtual bool SetupGlobalWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return false;

		Array.Clear(g_flexweight);

		if (hdr.NumFlexControllers() == 0) {
			flexWeights.Clear();
			if (!flexDelayedWeights.IsEmpty)
				flexDelayedWeights.Clear();
			return false;
		}

		LocalFlexController i;

		ProcessSceneEvents(true);

		Assert(hdr.FlexController(0).LocalToGlobal != -1);

		for (i = 0; i < hdr.NumFlexControllers(); i++) {
			MStudioFlexController flex = hdr.FlexController(i);

			g_flexweight[flex.LocalToGlobal] = FlexWeight[(int)i];
			g_flexweight[flex.LocalToGlobal] = g_flexweight[flex.LocalToGlobal] * (flex.Max - flex.Min) + flex.Min;
		}

		ProcessSceneEvents(false);

		if (BlinkToggle != PrevBlinkToggle) {
			PrevBlinkToggle = BlinkToggle;
			BlinkTime = gpGlobals.CurTime + g_CV_BlinkDuration.GetFloat();
		}

		if (Blink == -1)
			Blink = AddGlobalFlexController("blink");

		float flBlinkDuration = g_CV_BlinkDuration.GetFloat();
		float flOOBlinkDuration = (flBlinkDuration > 0) ? 1.0f / flBlinkDuration : 0.0f;
		float t = (float)((BlinkTime - gpGlobals.CurTime) * Math.PI * 0.5 * flOOBlinkDuration);
		if (t > 0) {
			t = (float)Math.Cos(t);
			if (t > 0.0f && t < 1.0f) {
				t = MathF.Sqrt(t) * 2.0f;
				if (t > 1.0f)
					t = 2.0f - t;
				t = Math.Clamp(t, 0.0f, 1.0f);
				g_flexweight[Blink] = Math.Clamp(g_flexweight[Blink] + t, 0.0f, 1.0f);
			}
		}

		ProcessVisemes(PhonemeClasses);

		return true;
	}

	public static void RunFlexDelay(Span<float> flexWeights, Span<float> flexDelayedWeights, ref TimeUnit_t flexDelayTime) {
		if (flexDelayTime > 0.0 && flexDelayTime < gpGlobals.CurTime) {
			TimeUnit_t dt = Math.Clamp(gpGlobals.CurTime - flexDelayTime, 0.0, gpGlobals.FrameTime);
			float d = MathLib.ExponentialDecay(0.8f, 0.033f, (float)dt);

			for (int i = 0; i < flexWeights.Length; i++)
				flexDelayedWeights[i] = flexDelayedWeights[i] * d + flexWeights[i] * (1.0f - d);
		}
		flexDelayTime = gpGlobals.CurTime;
	}

	public virtual void SetupLocalWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return;

		AssertMsg(flexWeights.Length == FlexDelayedWeightCount, "Disagreement between the number of flex weights. Do the studio headers match?");
		if (flexWeights.Length != FlexDelayedWeightCount)
			return;

		RunFlexRules(hdr, flexWeights);

		SetViewTarget(hdr);

		Assert(hdr.FlexController(0).LocalToGlobal != -1);

		if (!flexDelayedWeights.IsEmpty) {
			RunFlexDelay(flexWeights, FlexDelayedWeight!, ref FlexDelayTime);
			FlexDelayedWeight!.AsSpan(0, flexWeights.Length).CopyTo(flexDelayedWeights);
		}
	}

	public static int AddGlobalFlexController(ReadOnlySpan<char> name) {
		int i;
		for (i = 0; i < g_numflexcontrollers; i++) {
			if (stricmp(g_flexcontroller[i], name) == 0)
				return i;
		}

		if (g_numflexcontrollers < Studio.MAXSTUDIOFLEXCTRL * 4)
			g_flexcontroller[g_numflexcontrollers++] = new(name);

		return i;
	}

	public LocalFlexController FindFlexController(ReadOnlySpan<char> name) {
		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
			if (stricmp(GetFlexControllerName(i), name) == 0)
				return i;
		}

		return (LocalFlexController)(-1);
	}

	public virtual void EnsureTranslations(FlexSettingHdr settinghdr) {
		Assert(settinghdr != null);

		if (LocalToGlobal.ContainsKey(settinghdr!))
			return;

		Assert(settinghdr!.NumKeys > 0);
		int[] mapping = new int[settinghdr.NumKeys];

		for (int i = 0; i < settinghdr.NumKeys; ++i)
			mapping[i] = AddGlobalFlexController(settinghdr.LocalName(i));

		LocalToGlobal.Add(settinghdr, mapping);
	}

	public int FlexControllerLocalToGlobal(FlexSettingHdr settinghdr, int key) {
		if (!LocalToGlobal.TryGetValue(settinghdr, out int[]? mapping)) {
			Assert(false);
			Warning($"Unable to find mapping for flexcontroller {key}, settings {settinghdr.GetHashCode():X8} on {EntIndex()}/{GetClassname()}\n");
			EnsureTranslations(settinghdr);
			if (!LocalToGlobal.TryGetValue(settinghdr, out mapping))
				Error("CBaseFlex::FlexControllerLocalToGlobal failed!\n");
		}

		Assert(mapping!.Length != 0 && key < mapping.Length);
		int index = mapping[key];
		return index;
	}

	public void StartChoreoScene(ChoreoScene scene) {
		if (ActiveChoreoScenes.Contains(scene))
			return;

		ActiveChoreoScenes.Add(scene);
	}

	public void RemoveChoreoScene(ChoreoScene scene) {
		ActiveChoreoScenes.Remove(scene);
	}

	public void ClearSceneEvents(ChoreoScene? scene, bool canceled) {
		if (scene == null) {
			SceneEvents.Clear();
			return;
		}

		for (int i = SceneEvents.Count - 1; i >= 0; i--) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Scene != null);
			Assert(info.Event != null);

			if (info.Scene != scene)
				continue;

			if (!ClearSceneEvent(info, false, canceled))
				Assert(false);

			info.Event = null;
			info.Scene = null;
			info.Started = false;

			SceneEvents.RemoveAt(i);
		}
	}

	public virtual bool ClearSceneEvent(SceneEventInfo info, bool fastKill, bool canceled) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);
		return true;
	}

	public void AddSceneEvent(ChoreoScene? scene, ChoreoEvent? ev, C_BaseEntity? target, bool clientSide) {
		if (scene == null || ev == null) {
			Msg("C_BaseFlex::AddSceneEvent:  scene or event was NULL!!!\n");
			return;
		}

		ChoreoActor? actor = ev.GetActor();
		if (actor == null) {
			Msg("C_BaseFlex::AddSceneEvent:  event->GetActor() was NULL!!!\n");
			return;
		}

		SceneEventInfo info = new() { Layer = 0 };

		info.Event = ev;
		info.Scene = scene;
		info.Target.Set(target);
		info.Started = false;
		info.ClientSide = clientSide;

		if (StartSceneEvent(info, scene, ev, actor, target))
			SceneEvents.Add(info);
		else
			scene.SceneMsg("C_BaseFlex::AddSceneEvent:  event failed\n");
	}

	public virtual bool StartSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, C_BaseEntity? target) {
		switch (ev.GetType()) {
			default:
				break;

			case EventType.FlexAnimation:
				info.InitWeight(this);
				return true;

			case EventType.Expression:
				return true;

			case EventType.Sequence:
				if (info.ClientSide)
					return RequestStartSequenceSceneEvent(info, scene, ev, actor, target);
				break;

			case EventType.Speak:
				if (info.ClientSide)
					return true;
				break;
		}

		return false;
	}

	bool RequestStartSequenceSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, C_BaseEntity? target) {
		info.Sequence = LookupSequence(ev.GetParameters());

		if (info.Sequence < 0)
			return false;

		info.Actor = actor;
		return true;
	}

	public void RemoveSceneEvent(ChoreoScene? scene, ChoreoEvent? ev, bool fastKill) {
		Assert(ev != null);

		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Event != null);

			if (info.Scene != scene)
				continue;

			if (info.Event != ev)
				continue;

			if (ClearSceneEvent(info, fastKill, false)) {
				info.Event = null;
				info.Scene = null;
				info.Started = false;

				SceneEvents.RemoveAt(i);
			}
		}
	}

	public bool CheckSceneEvent(TimeUnit_t currenttime, ChoreoScene? scene, ChoreoEvent? ev) {
		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Event != null);

			if (info.Scene != scene)
				continue;

			if (info.Event != ev)
				continue;

			return CheckSceneEventCompletion(info, currenttime, scene!, ev!);
		}
		return true;
	}

	public virtual bool CheckSceneEventCompletion(SceneEventInfo info, TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) => true;

#if GMOD_DLL
	public void SetFlexWeight(LocalFlexController index, float value, bool clamp) {
		if (index >= 0 && index < GetNumFlexControllers() && (int)index < 96) {
			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return;

			MStudioFlexController flexcontroller = studioHdr.FlexController(index);

			if (flexcontroller.Max != flexcontroller.Min) {
				value = (value - flexcontroller.Min) / (flexcontroller.Max - flexcontroller.Min);
				if (clamp)
					value = Math.Clamp(value, 0.0f, 1.0f);
			}

			FlexWeight[(int)index] = value;
		}
	}
#endif

	public void SetFlexWeight(LocalFlexController index, float value) {
		if (index >= 0 && index < GetNumFlexControllers()) {
			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return;

			MStudioFlexController flexcontroller = studioHdr.FlexController(index);

			if (flexcontroller.Max != flexcontroller.Min) {
				value = (value - flexcontroller.Min) / (flexcontroller.Max - flexcontroller.Min);
				value = Math.Clamp(value, 0.0f, 1.0f);
			}

			FlexWeight[(int)index] = value;
		}
	}

	public float GetFlexWeight(LocalFlexController index) {
		if (index >= 0 && index < GetNumFlexControllers()) {
			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return 0;

			MStudioFlexController flexcontroller = studioHdr.FlexController(index);

			if (flexcontroller.Max != flexcontroller.Min)
				return FlexWeight[(int)index] * (flexcontroller.Max - flexcontroller.Min) + flexcontroller.Min;

			return FlexWeight[(int)index];
		}
		return 0.0f;
	}

	public virtual void ProcessSceneEvents(bool flexEvents) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return;

		if (flexEvents) {
			for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++)
				SetFlexWeight(i, GetFlexWeight(i) * 0.95f);
		}

		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];
			Assert(info != null);

			ChoreoEvent? ev = info!.Event;
			Assert(ev != null);

			ChoreoScene? scene = info.Scene;
			Assert(scene != null);

			if (ProcessSceneEvent(flexEvents, info, scene!, ev!))
				info.Started = true;
		}
	}

	bool ProcessFlexAnimationSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		Assert(ev.HasEndTime());
		if (ev.HasEndTime())
			AddFlexAnimation(info);
		return true;
	}

	bool ProcessFlexSettingSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (!ev.HasEndTime())
			return true;

		string scenefile = ev.GetParameters();
		string name = ev.GetParameters2();

		if (scenefile != null && name != null) {
			FlexSettingHdr? expHdr = FlexSceneFileManager.g_FlexSceneFileManager.FindSceneFile(this, scenefile, true);
			if (expHdr != null) {
				TimeUnit_t scenetime = scene.GetTime();

				float scale = ev.GetIntensity(scenetime);

				AddFlexSetting(name, scale, expHdr, !info.Started);
			}
		}

		return true;
	}

	void AddFlexSetting(ReadOnlySpan<char> expr, float scale, FlexSettingHdr settinghdr, bool newexpression) {
		int i;
		FlexSetting? setting = null;

		for (i = 0; i < settinghdr.NumFlexSettings; i++) {
			setting = settinghdr.Setting(i);
			if (setting == null)
				continue;

			string name = setting.Name();

			if (stricmp(name, expr) == 0)
				break;
		}

		if (i >= settinghdr.NumFlexSettings)
			return;

		int truecount = setting!.PSetting(0, out ReadOnlySpan<FlexSettingWeight> weights);

		for (i = 0; i < truecount; i++) {
			ref readonly FlexSettingWeight w = ref weights[i];

			int flex = FlexControllerLocalToGlobal(settinghdr, w.Key);

			float s = Math.Clamp(scale * w.Influence, 0.0f, 1.0f);
			g_flexweight[flex] = g_flexweight[flex] * (1.0f - s) + w.Weight * s;
		}
	}

	public virtual bool ProcessSceneEvent(bool flexEvents, SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		switch (ev.GetType()) {
			default:
				break;

			case EventType.FlexAnimation:
				if (flexEvents)
					return ProcessFlexAnimationSceneEvent(info, scene, ev);
				return true;

			case EventType.Expression:
				if (!flexEvents)
					return ProcessFlexSettingSceneEvent(info, scene, ev);
				return true;

			case EventType.Sequence:
				if (info.ClientSide) {
					if (!flexEvents)
						return ProcessSequenceSceneEvent(info, scene, ev);
					return true;
				}
				break;

			case EventType.Speak:
				if (info.ClientSide)
					return true;
				break;
		}

		return false;
	}

	bool ProcessSequenceSceneEvent(SceneEventInfo? info, ChoreoScene? scene, ChoreoEvent? ev) {
		if (info == null || ev == null || scene == null)
			return false;

		SetSequence(info.Sequence);
		return true;
	}

	void AddFlexAnimation(SceneEventInfo? info) {
		if (info == null)
			return;

		ChoreoEvent? ev = info.Event;
		if (ev == null)
			return;

		ChoreoScene? scene = info.Scene;
		if (scene == null)
			return;

		if (!ev.GetTrackLookupSet()) {
			for (int i = 0; i < ev.GetNumFlexAnimationTracks(); i++) {
				FlexAnimationTrack? track = ev.GetFlexAnimationTrack(i);
				if (track == null)
					continue;

				if (track.IsComboType()) {
					string name = "right_" + track.GetFlexControllerName();

					track.SetFlexControllerIndex((LocalFlexController)Math.Max((int)FindFlexController(name), 0), 0, 0);

					name = "left_" + track.GetFlexControllerName();

					track.SetFlexControllerIndex((LocalFlexController)Math.Max((int)FindFlexController(name), 0), 0, 1);
				}
				else
					track.SetFlexControllerIndex((LocalFlexController)Math.Max((int)FindFlexController(track.GetFlexControllerName()), 0), 0);
			}

			ev.SetTrackLookupSet(true);
		}

		if (!scene_clientflex.GetBool())
			return;

		TimeUnit_t scenetime = scene.GetTime();

		float weight = ev.GetIntensity(scenetime);

		weight = weight * info.UpdateWeight(this);

		for (int i = 0; i < ev.GetNumFlexAnimationTracks(); i++) {
			FlexAnimationTrack? track = ev.GetFlexAnimationTrack(i);
			if (track == null)
				continue;

			if (!track.IsTrackActive())
				continue;

			if (track.IsComboType()) {
				for (int side = 0; side < 2; side++) {
					LocalFlexController controller = track.GetRawFlexControllerIndex(side);

					float intensity = track.GetIntensity((float)scenetime, side);
					if (controller >= 0) {
						float orig = GetFlexWeight(controller);
						float value = orig * (1 - weight) + intensity * weight;
						SetFlexWeight(controller, value);
					}
				}
			}
			else {
				LocalFlexController controller = track.GetRawFlexControllerIndex(0);

				float intensity = track.GetIntensity((float)scenetime, 0);
				if (controller >= 0) {
					float orig = GetFlexWeight(controller);
					float value = orig * (1 - weight) + intensity * weight;
					SetFlexWeight(controller, value);
				}
			}
		}

		info.Started = true;
	}

	public bool HasSceneEvents() => SceneEvents.Count != 0;
}