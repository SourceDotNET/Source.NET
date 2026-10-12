using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<PathTrack>;

public enum TrackOrientationType
{
	Fixed = 0,
	FacePath,
	FacePathAngles,
}

[LinkEntityToClass("path_track")]
public class PathTrack : PointEntity
{
	public const int SF_PATH_DISABLED = 0x00000001;
	public const int SF_PATH_ALTREVERSE = 0x00000004;
	public const int SF_PATH_DISABLE_TRAIN = 0x00000008;
	public const int SF_PATH_TELEPORT = 0x00000010;
	public const int SF_PATH_UPHILL = 0x00000020;
	public const int SF_PATH_DOWNHILL = 0x00000040;
	public const int SF_PATH_ALTERNATE = 0x00008000;

	public Handle<PathTrack> Next = new();
	public Handle<PathTrack> Previous = new();
	public Handle<PathTrack> AltPath = new();

	float Radius;
	float Length;
	string? AltName;
	int IterVal;
	int OrientationType;

	public OutputEvent OnPass = new();
	public OutputEvent OnTeleport = new();

	static int s_nCurrIterVal;
	static bool s_bIsIterating;

	public static readonly new DataMap DataDesc = new(typeof(PathTrack), BaseEntity.DataDesc, [
		DEFINE.FIELD(nameof(Next), FieldType.ClassPtr),
		DEFINE.FIELD(nameof(Previous), FieldType.ClassPtr),
		DEFINE.FIELD(nameof(AltPath), FieldType.ClassPtr),

		DEFINE.KEYFIELD(nameof(Radius), FieldType.Float, "radius"),
		DEFINE.FIELD(nameof(Length), FieldType.Float),
		DEFINE.KEYFIELD(nameof(AltName), FieldType.String, "altpath"),
		DEFINE.KEYFIELD(nameof(OrientationType), FieldType.Integer, "orientationtype"),

		DEFINE.INPUTFUNC(FieldType.Void, "InPass", nameof(InputPass), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputPass(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "InTeleport", nameof(InputTeleport), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputTeleport(data))),

		DEFINE.INPUTFUNC(FieldType.Void, "EnableAlternatePath", nameof(InputEnableAlternatePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputEnableAlternatePath(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "DisableAlternatePath", nameof(InputDisableAlternatePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputDisableAlternatePath(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "ToggleAlternatePath", nameof(InputToggleAlternatePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputToggleAlternatePath(data))),

		DEFINE.INPUTFUNC(FieldType.Void, "EnablePath", nameof(InputEnablePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputEnablePath(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "DisablePath", nameof(InputDisablePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputDisablePath(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "TogglePath", nameof(InputTogglePath), (INPUTFUNCPTR)((self, data) => ((PathTrack)self).InputTogglePath(data))),

		DEFINE.OUTPUT(nameof(OnPass), "OnPass", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTeleport), "OnTeleport", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public PathTrack() {
		IterVal = -1;
		OrientationType = (int)TrackOrientationType.FacePath;
	}

	public override void Spawn() {
		SetSolid(Source.SolidType.None);
		Util.SetSize(this, new Vector3(-8, -8, -8), new Vector3(8, 8, 8));
	}

	public override void Activate() {
		base.Activate();

		if (!string.IsNullOrEmpty(GetEntityName()))
			Link();
	}

	void Link() {
		BaseEntity? target;

		if (!string.IsNullOrEmpty(Target)) {
			target = gEntList.FindEntityByName(null, Target);

			if (target == this)
				Warning($"ERROR: path_track ({GetDebugName()}) refers to itself as a target!\n");
			else if (target != null) {
				Next.Set(target as PathTrack);

				Next.Get()?.SetPrevious(this);
			}
			else
				Warning($"Dead end link: {Target}\n");
		}

		if (!string.IsNullOrEmpty(AltName)) {
			target = gEntList.FindEntityByName(null, AltName);
			if (target != null) {
				AltPath.Set(target as PathTrack);
				AltPath.Get()!.SetPrevious(this);
			}
		}
	}

	public static void BeginIteration() {
		Assert(!s_bIsIterating);
		++s_nCurrIterVal;
		s_bIsIterating = true;
	}

	public static void EndIteration() {
		Assert(s_bIsIterating);
		s_bIsIterating = false;
	}

	public void Visit() => IterVal = s_nCurrIterVal;

	public bool HasBeenVisited() => IterVal == s_nCurrIterVal;

	public bool HasAlternathPath() => AltPath.Get() != null;

	public void ToggleAlternatePath() {
		if (AltPath.Get() != null) {
			if (!HasSpawnFlags(SF_PATH_ALTERNATE))
				EnableAlternatePath();
			else
				DisableAlternatePath();
		}
	}

	public void EnableAlternatePath() {
		if (AltPath.Get() != null)
			AddSpawnFlags(SF_PATH_ALTERNATE);
	}

	public void DisableAlternatePath() {
		if (AltPath.Get() != null)
			RemoveSpawnFlags(SF_PATH_ALTERNATE);
	}

	public void InputEnableAlternatePath(InputData inputdata) => EnableAlternatePath();

	public void InputDisableAlternatePath(InputData inputdata) => DisableAlternatePath();

	public void InputToggleAlternatePath(InputData inputdata) => ToggleAlternatePath();

	public void TogglePath() {
		if (HasSpawnFlags(SF_PATH_DISABLED))
			EnablePath();
		else
			DisablePath();
	}

	public void EnablePath() => RemoveSpawnFlags(SF_PATH_DISABLED);

	public void DisablePath() => AddSpawnFlags(SF_PATH_DISABLED);

	public void InputEnablePath(InputData inputdata) => EnablePath();

	public void InputDisablePath(InputData inputdata) => DisablePath();

	public void InputTogglePath(InputData inputdata) => TogglePath();

	public static PathTrack? ValidPath(PathTrack? path, int testFlag = 1) {
		if (path == null)
			return null;

		if (testFlag != 0 && path.HasSpawnFlags(SF_PATH_DISABLED))
			return null;

		return path;
	}

	public static PathTrack? Instance(Edict ent) {
		BaseEntity? entity = BaseEntity.Instance(ent);
		if (FClassnameIs(entity, "path_track"))
			return (PathTrack?)entity;
		return null;
	}

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.AbsBox)) != 0)
			if (Next.IsValid())
				DebugOverlay.Line(GetAbsOrigin(), Next.Get()!.GetAbsOrigin(), 255, 100, 100, true, 0.0f);

		base.DrawDebugGeometryOverlays();
	}

	void Project(PathTrack? start, PathTrack? end, ref Vector3 origin, float dist) {
		if (start != null && end != null) {
			Vector3 dir = end.GetLocalOrigin() - start.GetLocalOrigin();
			MathLib.VectorNormalize(ref dir);
			origin = end.GetLocalOrigin() + dir * dist;
		}
	}

	public PathTrack? GetNext() {
		if (AltPath.Get() != null && HasSpawnFlags(SF_PATH_ALTERNATE) && !HasSpawnFlags(SF_PATH_ALTREVERSE))
			return AltPath.Get();

		return Next.Get();
	}

	public PathTrack? GetPrevious() {
		if (AltPath.Get() != null && HasSpawnFlags(SF_PATH_ALTERNATE) && HasSpawnFlags(SF_PATH_ALTREVERSE))
			return AltPath.Get();

		return Previous.Get();
	}

	void SetPrevious(PathTrack? prev) {
		if (prev != null && !FStrEq(prev.GetEntityName(), AltName))
			Previous.Set(prev);
	}

	public PathTrack? GetNextInDir(bool forward) {
		if (forward)
			return GetNext();

		return GetPrevious();
	}

	public PathTrack? LookAhead(ref Vector3 origin, float dist, int move) => LookAhead(ref origin, dist, move, out _);

	public PathTrack? LookAhead(ref Vector3 origin, float dist, int move, out PathTrack? nextNext) {
		PathTrack current = this;
		float originalDist = dist;
		Vector3 currentPos = origin;
		nextNext = null;

		bool forward = true;
		if (dist < 0) {
			dist = -dist;
			forward = false;
		}

		while (dist > 0) {
			if (ValidPath(current.GetNextInDir(forward), move) == null) {
				if (move == 0)
					Project(current.GetNextInDir(!forward), current, ref origin, dist);

				return null;
			}

			Vector3 dir = current.GetNextInDir(forward)!.GetLocalOrigin() - currentPos;
			float length = dir.Length();

			if (length == 0 && ValidPath(current.GetNextInDir(forward)!.GetNextInDir(forward), move) == null) {
				nextNext = null;

				if (dist == originalDist)
					return null;

				return current.GetNextInDir(forward);
			}

			if (length > dist) {
				origin = currentPos + (dir * (dist / length));
				nextNext = current.GetNextInDir(forward);

				return current;
			}

			dist -= length;
			currentPos = current.GetNextInDir(forward)!.GetLocalOrigin();
			current = current.GetNextInDir(forward)!;
			origin = currentPos;
		}

		nextNext = current.GetNextInDir(forward);

		return current;
	}

	public PathTrack? Nearest(in Vector3 origin) {
		int deadCount;
		float minDist, dist;
		Vector3 delta;
		PathTrack? path, nearest;

		delta = origin - GetLocalOrigin();
		delta.Z = 0;
		minDist = delta.Length();
		nearest = this;
		path = GetNext();

		deadCount = 0;
		while (path != null && path != this) {
			deadCount++;
			if (deadCount > 9999) {
				Warning($"Bad sequence of path_tracks from {GetDebugName()}\n");
				Assert(false);
				return null;
			}
			delta = origin - path.GetLocalOrigin();
			delta.Z = 0;
			dist = delta.Length();
			if (dist < minDist) {
				minDist = dist;
				nearest = path;
			}
			path = path.GetNext();
		}
		return nearest;
	}

	public TrackOrientationType GetOrientationType() => (TrackOrientationType)OrientationType;

	public QAngle GetOrientation(bool forwardDir) {
		TrackOrientationType orient = GetOrientationType();
		if (orient == TrackOrientationType.FacePathAngles)
			return GetLocalAngles();

		PathTrack? prev = this;
		PathTrack? next = GetNextInDir(forwardDir);

		if (next == null) {
			prev = GetNextInDir(!forwardDir);
			next = this;
		}

		Vector3 vecDir = next.GetLocalOrigin() - prev!.GetLocalOrigin();

		MathLib.VectorAngles(vecDir, out QAngle angDir);
		return angDir;
	}

	public float GetRadius() => Radius;

	public bool IsUpHill() => HasSpawnFlags(SF_PATH_UPHILL);
	public bool IsDownHill() => HasSpawnFlags(SF_PATH_DOWNHILL);

	public HillType GetHillType() {
		HillType retVal = HillType.None;
		if (IsUpHill())
			retVal = HillType.Uphill;
		else if (IsDownHill())
			retVal = HillType.Downhill;

		return retVal;
	}

	public bool IsDisabled() => HasSpawnFlags(SF_PATH_DISABLED);

	public void InputPass(InputData inputdata) => OnPass.FireOutput(inputdata.Activator, this);

	public void InputTeleport(InputData inputdata) => OnTeleport.FireOutput(inputdata.Activator, this);
}
