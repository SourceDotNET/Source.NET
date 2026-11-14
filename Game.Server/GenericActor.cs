using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("generic_actor")]
public class GenericActor : AI_BaseActor
{
	public static readonly ConVar flex_looktime = new("flex_looktime", "5");

	public static readonly new DataMap DataDesc = new(typeof(GenericActor), BaseEntity.DataDesc, [
		Source.DEFINE<GenericActor>.KEYFIELD(nameof(HullName), FieldType.String, "hull_name"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public string? HullName;

	public override Class_T Classify() => Class_T.None;

	public override float MaxYawSpeed() => 90;

	public override int GetSoundInterests() => 0;

	public override void HandleAnimEvent(ref AnimEvent animEvent) => base.HandleAnimEvent(ref animEvent);

	public override void Spawn() {
		Precache();

		SetModel(GetModelName());

		if (FStrEq(GetModelName(), "models/player.mdl") ||
			 FStrEq(GetModelName(), "models/holo.mdl") ||
			 FStrEq(GetModelName(), "models/blackout.mdl")) {
			Util.SetSize(this, VEC_HULL_MIN, VEC_HULL_MAX);
		}
		else
			Util.SetSize(this, NAI_Hull.Mins(AI_HullType.Human), NAI_Hull.Maxs(AI_HullType.Human));

		if (!FStrEq(GetModelName(), "models/blackout.mdl")) {
			SetSolid(SolidType.BBox);
			AddSolidFlags(SolidFlags.NotStandable);
		}
		else
			SetSolid(SolidType.None);

		SetMoveType(Source.MoveType.Step);
		Health = 8;
		FieldOfView = 0.5f;
		NPCState = NPCState.None;

		CapabilitiesAdd(Server.Capability.MoveGround | Server.Capability.OpenDoors);

		if (LookupAttachment("eyes") > 0 && LookupAttachment("forward") > 0)
			CapabilitiesAdd(Server.Capability.TurnHead | Server.Capability.AnimatedFace);

		if (HullName != null)
			SetHullType(NAI_Hull.LookupId(HullName));
		else
			SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		NPCInit();
	}

	public override void Precache() {
		PrecacheModel(GetModelName());
	}
}

[LinkEntityToClass("cycler_actor")]
public class FlextalkActor : GenericActor
{
	public static readonly new DataMap DataDesc = new(typeof(FlextalkActor), GenericActor.DataDesc, [
		Source.DEFINE<FlextalkActor>.FIELD(nameof(FlexTime), FieldType.Time),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(FlexNum), FieldType.Integer),
		Source.DEFINE<FlextalkActor>.ARRAY(nameof(FlexTarget), FieldType.Float, 64),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(BlinkTime), FieldType.Time),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(LookTime), FieldType.Time),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(LookTarget), FieldType.PositionVector),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(SpeakTime), FieldType.Time),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(IsTalking), FieldType.Integer),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(Phoneme), FieldType.Integer),
		Source.DEFINE<FlextalkActor>.KEYFIELD(nameof(SentenceName), FieldType.String, "Sentence"),
		Source.DEFINE<FlextalkActor>.FIELD(nameof(Sentence), FieldType.Integer),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public FlextalkActor() {
		SentenceName = null;
		Sentence = 0;
	}

	public TimeUnit_t FlexTime;
	public LocalFlexController FlexNum;
	public readonly float[] FlexTarget = new float[64];
	public TimeUnit_t BlinkTime;
	public TimeUnit_t LookTime;
	public Vector3 LookTarget;
	public TimeUnit_t SpeakTime;
	public int IsTalking;
	public int Phoneme;

	public string? SentenceName;
	public int Sentence;

	public void SetFlexTarget(LocalFlexController flexnum, float value) {
		FlexTarget[(int)flexnum] = value;

		string? type = GetFlexControllerType(flexnum);

		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
			if (i != flexnum) {
				string? otherType = GetFlexControllerType(i);
				if (stricmp(type, otherType) == 0)
					FlexTarget[(int)i] = 0;
			}
		}

		float value2 = RandomFloat(value - 0.2f, value + 0.2f);
		value2 = Math.Clamp(value2, 0.0f, 1.0f);

		if (strncmp("right_", GetFlexControllerName(flexnum), 6) == 0)
			FlexTarget[(int)flexnum + 1] = value2;
		else if (strncmp("left_", GetFlexControllerName(flexnum), 5) == 0)
			FlexTarget[(int)flexnum - 1] = value2;
	}

	public LocalFlexController LookupFlex(ReadOnlySpan<char> target) {
		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
			string? flex = GetFlexControllerName(i);
			if (stricmp(target, flex) == 0)
				return i;
		}
		return (LocalFlexController)(-1);
	}

	public override void ProcessSceneEvents() {
		if (HasSceneEvents()) {
			base.ProcessSceneEvents();
			return;
		}

		if (GetNumFlexControllers() > (LocalFlexController)2) {
			string expression = flex_expression.GetString();

			if (expression.Length > 1 && expression[0] == '+') {
				int i;
				int j = atoi(expression.AsSpan(1));
				for (i = 0; i < (int)GetNumFlexControllers(); i++)
					FlexTarget[(int)FlexNum] = 0;

				for (i = 0; i < 35 && predef_flexcontroller_names[i] != null; i++) {
					FlexNum = LookupFlex(predef_flexcontroller_names[i]);
					FlexTarget[(int)FlexNum] = predef_flexcontroller_values[j, i];
				}
			}
			else if (expression.Length > 0 && expression != "+") {
				char[] szExpression = expression.Replace('+', ' ').ToCharArray();

				int pos = 0;
				while (pos < szExpression.Length) {
					if (szExpression[pos] != ' ') {
						if (szExpression[pos] == '-') {
							for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++)
								FlexTarget[(int)i] = 0;
						}
						else if (szExpression[pos] == '?') {
							for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++)
								Msg($"\"{GetFlexControllerName(i)}\" ");
							Msg("\n");
							flex_expression.SetValue("");
						}
						else {
							int end = pos;
							while (end < szExpression.Length && end - pos < 31 && !char.IsWhiteSpace(szExpression[end]))
								end++;
							ReadOnlySpan<char> temp = szExpression.AsSpan(pos, end - pos);

							FlexNum = LookupFlex(temp);

							if (FlexNum != (LocalFlexController)(-1) && FlexTarget[(int)FlexNum] != 1)
								FlexTarget[(int)FlexNum] = 1.0f;
							pos += temp.Length - 1;
						}
					}
					pos++;
				}
			}
			else if (FlexTime < gpGlobals.CurTime) {
				FlexTime = gpGlobals.CurTime + RandomFloat(0.3f, 0.5f) * (30.0f / (int)GetNumFlexControllers());
				FlexNum = (LocalFlexController)RandomInt(0, (int)GetNumFlexControllers() - 1);

				if (FlexTarget[(int)FlexNum] == 1)
					FlexTarget[(int)FlexNum] = 0;
				else if (stricmp(GetFlexControllerType(FlexNum), "phoneme") != 0) {
					if (!(GetFlexControllerName(FlexNum) ?? "").Contains("upper_raiser")) {
						Msg($"{GetFlexControllerType(FlexNum)}:{GetFlexControllerName(FlexNum)}\n");
						SetFlexTarget(FlexNum, RandomFloat(0.5f, 1.0f));
					}
				}
			}

			for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
				float weight = GetFlexWeight(i);

				if (weight != FlexTarget[(int)i])
					weight = weight + (FlexTarget[(int)i] - weight) / RandomFloat(2.0f, 4.0f);
				weight = Math.Clamp(weight, 0.0f, 1.0f);
				SetFlexWeight(i, weight);
			}

			if (flex_talk.GetInt() == -1) {
				IsTalking = 1;

				string sentence = $"{SentenceName}{Sentence++}";
				int sentenceIndex = engine.SentenceIndexFromName(sentence);
				if (sentenceIndex >= 0) {
					Msg($"{sentenceIndex} : {sentence}\n");
					PASAttenuationFilter filter = new(this);
					EmitSentenceByIndex(filter, EntIndex(), (int)SoundEntityChannel.Voice, sentenceIndex, 1, SoundLevel.LvlTalking, 0, PITCH_NORM);
				}
				else
					Sentence = 0;

				flex_talk.SetValue("0");
			}
			else if (flex_talk.GetInt() == -2)
				NextEyeLookTime = gpGlobals.CurTime + 1000.0;
			else if (flex_talk.GetInt() == -3) {
				NextEyeLookTime = gpGlobals.CurTime;
				flex_talk.SetValue("0");
			}
			else if (flex_talk.GetInt() == -4) {
				AddLookTarget(Util.PlayerByIndex(1), 0.5f, flex_looktime.GetFloat());
				flex_talk.SetValue("0");
			}
			else if (flex_talk.GetInt() == -5) {
				PickLookTarget(true);
				flex_talk.SetValue("0");
			}
		}
	}
}
