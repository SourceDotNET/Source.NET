using static Game.Server.NavMesh.Nav;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;

using System.Numerics;
using Source.Common.Mathematics;

namespace Game.Server.NavMesh;

public partial class NavMesh
{
	public enum EditModeType
	{
		Normal,
		PlacePainting,
		CreatingArea,
		CreatingLadder,
		DragSelecting,
		ShiftingXY,
		ShiftingZ
	}

	enum GenerationStateType
	{
		SampleWalkableSpace,
		CreateAreasFromSamples,
		FindHidingSpots,
		FindEncounterSpots,
		FindSniperSpots,
		FindEarliestOccupyTimes,
		FindLightIntensity,
		ComputeMeshVisibility,
		Custom,
		SaveNavMesh,
		NumGenerationStates
	}

	enum GenerationModeType
	{
		None,
		Full,
		Incremental,
		Simplify,
		AnalysisOnly
	}

	struct WalkableSeedSpot
	{
		public Vector3 Pos;
		public Vector3 Normal;
	}

	public List<List<NavArea>> Grid = [];
	float GridCellSize;
	public int GridSizeX;
	public int GridSizeY;
	float MinX;
	float MinY;
	uint AreaCount;
	bool bIsLoaded;
	bool IsOutOfDate;
	bool bIsAnalyzed;
	const int HASH_TABLE_SIZE = 256;
	readonly NavArea?[] HashTable = new NavArea[HASH_TABLE_SIZE];
	string[]? PlaceName;
	uint PlaceCount;
	EditModeType EditMode;
	bool IsEditing;
	uint NavPlace;
	Vector3 EditCursorPos;
	NavArea? MarkedArea;
	NavArea? SelectedArea;
	NavArea? LastSelectedArea;
	public NavCornerType MarkedCorner;
	Vector3 Anchor;
	bool IsPlacePainting;
	bool SplitAlongX;
	float SplitEdge;
	bool ClimbableSurface;
	Vector3 SurfaceNormal;
	Vector3 LadderAnchor;
	Vector3 LadderNormal;
	NavLadder? SelectedLadder;
	NavLadder? LastSelectedLadder;
	NavLadder? MarkedLadder;
	CountdownTimer ShowAreaInfoTimer = new();
	readonly List<NavArea> SelectedSet = [];
	readonly List<NavArea> DragSelectionSet = [];
	bool ContinuouslySelecting;
	bool ContinuouslyDeselecting;
	bool IsDragDeselecting;
	int DragSelectionVolumeZMax;
	int DragSelectionVolumeZMin;
	NavNode? CurrentNode;
	NavDirType GenerationDir;
	readonly List<NavLadder> Ladders = [];
	GenerationStateType GenerationState;
	GenerationModeType GenerationMode;
	int GenerationIndex;
	int SampleTick;
	bool QuitWhenFinished;
	TimeUnit_t GenerationStartTime;
	Extent SimplifyGenerationExtent;
	string? SpawnName;
	readonly List<WalkableSeedSpot> WalkableSeeds = [];
	int SeedIdx;
	int HostThreatModeRestoreValue;
	readonly List<NavArea> TransientAreas = [];
	readonly List<NavArea> AvoidanceObstacleAreas = [];
	readonly List<INavAvoidanceObstacle> AvoidanceObstacles = [];
	readonly List<NavArea> BlockedAreas = [];
	readonly List<int> StoredSelectedSet = [];
	CountdownTimer UpdateBlockedAreasTimer = new();

	public static NavMesh? Instance;

	public NavMesh() {
		SpawnName = null;
		GridCellSize = 300.0f;
		EditMode = EditModeType.Normal;
		QuitWhenFinished = false;
		HostThreatModeRestoreValue = 0;
		PlaceCount = 0;
		PlaceName = null;

		LoadPlaceDatabase();

		// gameevents

		Reset();
	}

	public void Reset() {
		GenerationMode = GenerationModeType.None;
		CurrentNode = null;
		ClearWalkableSeeds();

		bIsAnalyzed = false;
		IsOutOfDate = false;
		IsEditing = false;
		NavPlace = Nav.UndefinedPlace;
		MarkedArea = null;
		SelectedArea = null;
		QuitWhenFinished = false;

		EditMode = EditModeType.Normal;

		LastSelectedArea = null;
		IsPlacePainting = false;

		ClimbableSurface = false;
		MarkedLadder = null;
		SelectedLadder = null;

		UpdateBlockedAreasTimer.Invalidate();

		SpawnName = null;

		WalkableSeeds.Clear();
	}

	public NavArea? GetMarkedArea() {
		if (MarkedArea != null)
			return MarkedArea;

		if (SelectedSet.Count > 0)
			return SelectedSet[0];

		return null;
	}

	public NavLadder? GetMarkedLadder() => MarkedLadder;

	public NavArea? GetSelectedArea() => SelectedArea;

	public NavLadder? GetSelectedLadder() => SelectedLadder;

	void DestroyNavigationMesh(bool incremental = false) {
		BlockedAreas.Clear();
		AvoidanceObstacleAreas.Clear();
		TransientAreas.Clear();

		if (!incremental) {
			NavArea.IsReset = true;
			foreach (NavArea area in NavArea.TheNavAreas) {
				// EditDestroyNotification notification = new(area);
				// ForEachActor(notification.Invoke); // FunctorUtils todo
			}

			foreach (NavArea area in NavArea.TheNavAreas)
				DestroyArea(area);

			NavArea.TheNavAreas.Clear();

			NavArea.IsReset = false;

			DestroyLadders();
		}
		else {
			foreach (NavArea area in NavArea.TheNavAreas)
				area.ResetNodes();
		}

		DestroyHidingSpots();

		NavNode.CleanupGeneration();

		if (!incremental) {
			Grid.Clear();
			GridSizeX = 0;
			GridSizeY = 0;
		}

		for (int i = 0; i < HASH_TABLE_SIZE; i++)
			HashTable[i] = null;

		if (!incremental) {
			AreaCount = 0;

			NavArea.CompressIDs();
			NavLadder.CompressIDs();
		}

		SetEditMode(EditModeType.Normal);

		MarkedArea = null;
		SelectedArea = null;
		LastSelectedArea = null;
		ClimbableSurface = false;
		MarkedLadder = null;
		SelectedLadder = null;

		if (!incremental)
			bIsLoaded = false;
	}

	public void Update() {
		if (IsGenerating()) {
			UpdateGeneration(0.03f);
			return;
		}

		if (UpdateBlockedAreasTimer.HasStarted() && UpdateBlockedAreasTimer.IsElapsed()) {
			TestAllAreasForBlockedStatus();
			UpdateBlockedAreasTimer.Invalidate();
		}

		UpdateBlockedAreas();
		UpdateAvoidanceObstacleAreas();

		if (nav_edit.GetBool()) {
			if (IsEditing == false) {
				OnEditModeStart();
				IsEditing = true;
			}

			DrawEditMode();
		}
		else {
			if (IsEditing) {
				OnEditModeEnd();
				IsEditing = false;
			}
		}

		if (nav_show_danger.GetBool()) DrawDanger();
		if (nav_show_player_counts.GetBool()) DrawPlayerCounts();
		if (nav_show_func_nav_avoid.GetBool()) DrawFuncNavAvoid();
		if (nav_show_func_nav_prefer.GetBool()) DrawFuncNavPrefer();
		if (nav_show_func_nav_prerequisite.GetBool()) DrawFuncNavPrerequisite();

		if (nav_show_potentially_visible.GetBool()) {
			// todo
		}

		for (int i = 0; i < WalkableSeeds.Count; i++) {
			WalkableSeedSpot spot = WalkableSeeds[i];

			const float height = 50.0f;
			const float width = 25.0f;

			DrawLine(spot.Pos, spot.Pos + height * spot.Normal, 3, 255, 0, 255);
			DrawLine(spot.Pos + new Vector3(width, 0, 0), spot.Pos + height * spot.Normal, 3, 255, 0, 255);
			DrawLine(spot.Pos + new Vector3(-width, 0, 0), spot.Pos + height * spot.Normal, 3, 255, 0, 255);
			DrawLine(spot.Pos + new Vector3(0, width, 0), spot.Pos + height * spot.Normal, 3, 255, 0, 255);
			DrawLine(spot.Pos + new Vector3(0, -width, 0), spot.Pos + height * spot.Normal, 3, 255, 0, 255);
		}
	}

	static void DrawLine(in Vector3 from, in Vector3 to, float _, int r, int g, int b) => Shared.DebugOverlay.Line(from, to, r, g, b, true, Shared.DebugOverlay.Persist);

	void FireGameEvent(IGameEvent gameEvent) { }

	public bool IsLoaded() => bIsLoaded;

	public bool IsAnalyzed() => bIsAnalyzed;

	void AllocateGrid(float minX, float maxX, float minY, float maxY) {
		Grid.Clear();

		MinX = minX;
		MinY = minY;

		GridSizeX = (int)((maxX - minX) / GridCellSize) + 1;
		GridSizeY = (int)((maxY - minY) / GridCellSize) + 1;

		Grid = [];
		for (int i = 0; i < GridSizeX * GridSizeY; i++)
			Grid.Add([]);
	}

	public void AddNavArea(NavArea area) {
		if (Grid.Count == 0)
			AllocateGrid(0, 0, 0, 0);

		int loX = WorldToGridX(area.GetCorner(NavCornerType.NorthWest).X);
		int loY = WorldToGridY(area.GetCorner(NavCornerType.NorthWest).Y);
		int hiX = WorldToGridX(area.GetCorner(NavCornerType.SouthEast).X);
		int hiY = WorldToGridY(area.GetCorner(NavCornerType.SouthEast).Y);

		for (int y = loY; y <= hiY; ++y) {
			for (int x = loX; x <= hiX; ++x)
				Grid[x + y * GridSizeX].Add(area);
		}

		int key = ComputeHashKey(area.GetID());

		if (HashTable[key] != null) {
			area.PrevHash = null;
			area.NextHash = HashTable[key];
			HashTable[key]!.PrevHash = area;
			HashTable[key] = area;
		}
		else {
			HashTable[key] = area;
			area.NextHash = null;
			area.PrevHash = null;
		}

		if ((area.GetAttributes() & NavAttributeType.Transient) != 0)
			TransientAreas.Add(area);

		++AreaCount;
	}

	public void RemoveNavArea(NavArea area) {
		int loX = WorldToGridX(area.GetCorner(NavCornerType.NorthWest).X);
		int loY = WorldToGridY(area.GetCorner(NavCornerType.NorthWest).Y);
		int hiX = WorldToGridX(area.GetCorner(NavCornerType.SouthEast).X);
		int hiY = WorldToGridY(area.GetCorner(NavCornerType.SouthEast).Y);

		for (int y = loY; y <= hiY; ++y) {
			for (int x = loX; x <= hiX; ++x)
				Grid[x + y * GridSizeX].Remove(area);
		}

		int key = ComputeHashKey(area.GetID());

		if (area.PrevHash != null)
			area.PrevHash.NextHash = area.NextHash;
		else
			HashTable[key] = area.NextHash;

		if (area.NextHash != null)
			area.NextHash.PrevHash = area.PrevHash;

		if ((area.GetAttributes() & NavAttributeType.Transient) != 0)
			BuildTransientAreaList();

		AvoidanceObstacleAreas.Remove(area);
		BlockedAreas.Remove(area);

		--AreaCount;
	}

	public void OnServerActivate() {
		foreach (NavArea area in NavArea.TheNavAreas)
			area.OnServerActivate();
	}

	void TestAllAreasForBlockedStatus() {
		foreach (NavArea area in NavArea.TheNavAreas)
			area.UpdateBlocked(true);
	}

	void OnRoundRestart() { }

	void OnRoundRestartPreEntity() { }

	void BuildTransientAreaList() {
		TransientAreas.Clear();

		foreach (NavArea area in NavArea.TheNavAreas) {
			if ((area.GetAttributes() & NavAttributeType.Transient) != 0)
				TransientAreas.Add(area);
		}
	}

	void GridToWorld(int gridX, int gridY, Vector3 pos) {
		gridX = Math.Clamp(gridX, 0, GridSizeX - 1);
		gridY = Math.Clamp(gridY, 0, GridSizeY - 1);

		pos.X = MinX + gridX * GridCellSize;
		pos.Y = MinY + gridY * GridCellSize;
	}

	public NavArea? GetNavArea(Vector3 pos, float beneathLimit = 120.0f) {
		if (Grid.Count == 0)
			return null;

		int x = WorldToGridX(pos.X);
		int y = WorldToGridY(pos.Y);
		List<NavArea> areaVector = Grid[x + y * GridSizeX];

		NavArea? use = null;
		float useZ = -99999999.9f;
		Vector3 testPos = pos + new Vector3(0, 0, 5);

		for (int it = 0; it < areaVector.Count; ++it) {
			NavArea area = areaVector[it];

			if (area.IsOverlapping(testPos)) {
				float z = area.GetZ(testPos);

				if (z > testPos.Z)
					continue;

				if (z < pos.Z - beneathLimit)
					continue;

				if (z > useZ) {
					use = area;
					useZ = z;
				}
			}
		}

		return use;
	}

	NavArea? GetNavArea(BaseEntity entity, int flags, float beneathLimit) {
		if (Grid.Count == 0)
			return null!;

		Vector3 testPos = entity.GetAbsOrigin();

		float stepHeight = 1e-3f;
		if (entity is BaseCombatCharacter combatCharacter) {
			NavArea? lastArea = null;// combatCharacter.LastKnownArea;
			if (lastArea != null && lastArea.IsOverlapping(testPos)) {
				float z = lastArea.GetZ(testPos);
				if (z <= testPos.Z + stepHeight && z >= testPos.Z - stepHeight)
					return lastArea;
			}

			stepHeight = StepHeight;
		}

		int x = WorldToGridX(testPos.X);
		int y = WorldToGridY(testPos.Y);
		List<NavArea> areaVector = Grid[x + y * GridSizeX];

		NavArea? use = null;
		float useZ = -99999999.9f;

		bool skipBlocked = (flags & (int)GetNavAreaFlags.AllowBlockedAreas) == 0;
		for (int it = 0; it < areaVector.Count; ++it) {
			NavArea area = areaVector[it];

			if (!area.IsOverlapping(testPos))
				continue;

			// if (skipBlocked && area.IsBlocked(entity.TeamNumber))
			// 	continue;

			float z = area.GetZ(testPos);

			if (z > testPos.Z + stepHeight)
				continue;

			if (z < testPos.Z - beneathLimit)
				continue;

			use = area;
			useZ = z;
		}

		if (use != null && (flags & (int)GetNavAreaFlags.CheckLOS) != 0 && useZ < testPos.Z - stepHeight) {
			Util.TraceLine(testPos, new Vector3(testPos.X, testPos.Y, useZ), Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out Trace result);
			if (result.Fraction != 1.0f && Math.Abs(result.EndPos.Z - useZ) > stepHeight)
				return null;
		}

		return use;
	}

	NavArea? GetNearestNavArea(Vector3 pos, bool anyZ = false, float maxDist = 10000.0f, bool checkLOS = false, bool checkGround = true, int team = Constants.TEAM_ANY) {
		if (Grid.Count == 0)
			return null;

		NavArea? close = null;
		float closeDistSq = maxDist * maxDist;

		if (!checkLOS && !checkGround) {
			close = GetNavArea(pos);
			if (close != null)
				return close;
		}

		Vector3 source = new(pos.X, pos.Y, pos.Z);
		if (!GetGroundHeight(pos, out source.Z, out _)) {
			if (!checkGround)
				source.Z = pos.Z;
			else
				return null;
		}

		source.Z += HalfHumanHeight;

		uint searchMarker = (uint)random.RandomInt(0, 1024 * 1024);

		++searchMarker;

		if (searchMarker == 0)
			++searchMarker;

		int originX = WorldToGridX(pos.X);
		int originY = WorldToGridY(pos.Y);
		int shiftLimit = (int)Math.Ceiling(maxDist / GridCellSize);

		for (int shift = 0; shift <= shiftLimit; ++shift) {
			for (int x = originX - shift; x <= originX + shift; ++x) {
				if (x < 0 || x >= GridSizeX)
					continue;

				for (int y = originY - shift; y <= originY + shift; ++y) {
					if (y < 0 || y >= GridSizeY)
						continue;

					if (x > originX - shift && x < originX + shift && y > originY - shift && y < originY + shift)
						continue;

					List<NavArea> areaVector = Grid[x + y * GridSizeX];

					for (int it = 0; it < areaVector.Count; ++it) {
						NavArea area = areaVector[it];

						if (area.NearNavSearchMarker == searchMarker)
							continue;

						if (area.IsBlocked(team))
							continue;

						area.NearNavSearchMarker = searchMarker;

						area.GetClosestPointOnArea(ref source, out Vector3 areaPos);

						float distSq = Vector3.DistanceSquared(areaPos, pos);

						if (distSq >= closeDistSq)
							continue;

						if (checkLOS) {
							Vector3 safePos;

							Util.TraceLine(pos, pos + new Vector3(0, 0, StepHeight), Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out Trace result);
							if (result.StartSolid)
								safePos = result.EndPos + new Vector3(0, 0, 1.0f);
							else
								safePos = pos;

							float heightDelta = Math.Abs(areaPos.Z - safePos.Z);
							if (heightDelta > StepHeight) {
								Util.TraceLine(areaPos + new Vector3(0, 0, StepHeight), new Vector3(areaPos.X, areaPos.Y, safePos.Z), Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out result);
								if (result.Fraction != 1.0f)
									continue;
							}

							Util.TraceLine(safePos, new Vector3(areaPos.X, areaPos.Y, safePos.Z + StepHeight), Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out result);
							if (result.Fraction != 1.0f)
								continue;
						}

						closeDistSq = distSq;
						close = area;
						shiftLimit = shift + 1;
					}
				}
			}
		}

		return close;
	}

	NavArea GetNearestNavArea(BaseEntity entity, int flags, float maxDist) {
		throw new NotImplementedException();
	}

	public NavArea? GetNavAreaByID(uint id) {
		if (id == 0)
			return null;

		int key = ComputeHashKey(id);

		for (NavArea? area = HashTable[key]; area != null; area = area.NextHash) {
			if (area.GetID() == id)
				return area;
		}

		return null;
	}

	public List<NavLadder> GetLadders() => Ladders;

	public NavLadder? GetLadderByID(uint id) {
		if (id == 0)
			return null;

		foreach (NavLadder ladder in Ladders) {
			if (ladder.GetID() == id)
				return ladder;
		}

		return null;
	}

	uint GetPlace(Vector3 pos) {
		throw new NotImplementedException();
	}

	void LoadPlaceDatabase() {
		PlaceCount = 0;
		// todo? is this even used?
	}

	public string? PlaceToName(NavPlace place) {
		if (place >= 1 && place <= PlaceCount)
			return PlaceName![place];

		return "";
	}

	public NavPlace NameToPlace(ReadOnlySpan<char> name) {
		for (uint i = 0; i < PlaceCount; i++) {
			if (FStrEq(PlaceName![i], name))
				return i;
		}

		return UndefinedPlace;
	}

	public NavPlace PartialNameToPlace(ReadOnlySpan<char> name) {
		throw new NotImplementedException();
	}

	public void PrintAllPlaces() {
		throw new NotImplementedException();
	}

	public bool GetGroundHeight(Vector3 pos, out float height, out Vector3? normal) {
		const float maxOffset = 100.0f;

		TraceFilterGroundEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);

		Vector3 to = new(pos.X, pos.Y, pos.Z - 10000.0f);
		Vector3 from = new(pos.X, pos.Y, (int)(pos.Z + HalfHumanHeight + 1e-3));

		while (to.Z - pos.Z < maxOffset) {
			Util.TraceLine(from, to, Mask.NPCSolidBrushOnly, null, ref filter, out Trace result);
			if (!result.StartSolid && ((result.Fraction == 1.0f) || ((from.Z - result.EndPos.Z) >= HalfHumanHeight))) {
				height = result.EndPos.Z;
				normal = !result.Plane.Normal.IsZero() ? result.Plane.Normal : new Vector3(0, 0, 1);
				return true;
			}

			to.Z = result.StartSolid ? from.Z : result.EndPos.Z;
			from.Z = (float)(to.Z + HalfHumanHeight + 1e-3);
		}

		height = 0;
		normal = new Vector3(0, 0, 1);

		return false;
	}

	public static bool GetSimpleGroundHeight(Vector3 pos, out float height, out Vector3 normal) {
		height = 0;
		normal = default;

		Vector3 to;
		to.X = pos.X;
		to.Y = pos.Y;
		to.Z = pos.Z - 9999.9f;

		Util.TraceLine(pos, to, Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out Trace result);

		if (result.StartSolid)
			return false;

		height = result.EndPos.Z;
		normal = result.Plane.Normal;

		return true;
	}

	void DrawDanger() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			Vector3 center = area.GetCenter();
			center.Z = area.GetZ(center);

			float danger = area.GetDanger(0);
			if (danger > 0.1f) {
				Vector3 top = new(center.X, center.Y, center.Z + 10.0f * danger);
				DrawLine(center, top, 3, 255, 0, 0);
			}

			danger = area.GetDanger(1);
			if (danger > 0.1f) {
				Vector3 top = new(center.X, center.Y, center.Z + 10.0f * danger);
				DrawLine(center, top, 3, 0, 0, 255);
			}
		}
	}

	void DrawPlayerCounts() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (area.GetPlayerCount() > 0)
				Shared.DebugOverlay.Text(area.GetCenter(), $"{area.GetPlayerCount()} ({area.GetPlayerCount(1)}/{area.GetPlayerCount(2)})", false, Shared.DebugOverlay.Persist);
		}
	}

	void DrawFuncNavAvoid() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (area.HasFuncNavAvoid())
				area.DrawFilled(255, 0, 0, 255);
		}
	}

	void DrawFuncNavPrefer() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (area.HasFuncNavPrefer())
				area.DrawFilled(0, 255, 0, 255);
		}
	}

	void DrawFuncNavPrerequisite() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (area.HasPrerequisite())
				area.DrawFilled(0, 0, 255, 255);
		}
	}

	public bool IsGenerating() => GenerationMode != GenerationModeType.None;

	void IncreaseDangerNearby(int teamID, float amount, NavArea startArea, Vector3 pos, float maxRadius, float dangerLimit) { }

	public void CommandNavMarkWalkable() {
		Vector3 pos;

		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		if (nav_edit.GetBool())
			pos = EditCursorPos;
		else {
			BasePlayer? player = Util.GetListenServerHost();

			if (player == null) {
				Msg("ERROR: No local player!\n");
				return;
			}

			pos = player.GetAbsOrigin();
		}

		pos.X = SnapToGrid(pos.X, true);
		pos.Y = SnapToGrid(pos.Y, true);

		if (!FindGroundForNode(ref pos, out Vector3 normal)) {
			Msg("ERROR: Invalid ground position.\n");
			return;
		}

		AddWalkableSeed(pos, normal);

		Msg("Walkable position marked.\n");
	}

	void DestroyLadders() {
		for (int i = 0; i < Ladders.Count; i++) {
			OnEditDestroyNotify(Ladders[i]);
			Ladders[i] = null!;
		}

		Ladders.Clear();

		MarkedLadder = null;
		SelectedLadder = null;
	}

	public void StripNavigationAreas() {
		foreach (NavArea area in NavArea.TheNavAreas)
			area.Strip();

		bIsAnalyzed = false;
	}

	public static HidingSpot CreateHidingSpot() => new();

	void DestroyHidingSpots() {
		foreach (NavArea area in NavArea.TheNavAreas)
			area.HidingSpots.Clear();

		HidingSpot.NextID = 0;

		HidingSpot.TheHidingSpots.Clear();
	}

	public void OnAreaBlocked(NavArea area) {
		if (!BlockedAreas.Contains(area))
			BlockedAreas.Add(area);
	}

	public void OnAreaUnblocked(NavArea area) => BlockedAreas.Remove(area);

	void UpdateBlockedAreas() {
		foreach (NavArea area in BlockedAreas)
			area.UpdateBlocked();
	}

	public void RegisterAvoidanceObstacle(INavAvoidanceObstacle obstruction) {
		if (!AvoidanceObstacles.Contains(obstruction))
			AvoidanceObstacles.Add(obstruction);
	}

	public void UnregisterAvoidanceObstacle(INavAvoidanceObstacle obstruction) => AvoidanceObstacles.Remove(obstruction);

	public List<INavAvoidanceObstacle> GetObstructions() => AvoidanceObstacles;

	public void OnAvoidanceObstacleEnteredArea(NavArea area) {
		if (!AvoidanceObstacleAreas.Contains(area))
			AvoidanceObstacleAreas.Add(area);
	}

	public void OnAvoidanceObstacleLeftArea(NavArea area) => AvoidanceObstacleAreas.Remove(area);

	void UpdateAvoidanceObstacleAreas() {
		foreach (NavArea area in AvoidanceObstacleAreas)
			area.UpdateAvoidanceObstacles();
	}

	void BeginVisibilityComputations() {
		g_NavVisPairHash.Clear();

		foreach (NavArea area in NavArea.TheNavAreas)
			area.ResetPotentiallyVisibleAreas();
	}

	void EndVisibilityComputations() {
		g_NavVisPairHash.Clear();

		int avgVisLength = 0;
		int maxVisLength = 0;
		int minVisLength = 999999999;

		for (int it = 0; it < NavArea.TheNavAreas.Count; ++it) {
			NavArea area = NavArea.TheNavAreas[it];

			int visLength = area.PotentiallyVisibleAreas.Count;
			avgVisLength += visLength;
			if (visLength < minVisLength)
				minVisLength = visLength;

			if (visLength > maxVisLength)
				maxVisLength = visLength;

			if (area.IsInheritedFrom)
				continue;

			List<NavArea.AreaBindInfo> bestDelta = [];
			NavArea? anchor = null;

			for (int dir = (int)NavDirType.North; dir < (int)NavDirType.NumDirections; ++dir) {
				int count = area.GetAdjacentCount((NavDirType)dir);
				for (int i = 0; i < count; ++i) {
					NavArea adjArea = area.GetAdjacentArea((NavDirType)dir, i)!;

					if (adjArea.InheritVisibilityFrom.Area != null) {
						adjArea = adjArea.InheritVisibilityFrom.Area;
						if (adjArea == area)
							continue;
					}

					List<NavArea.AreaBindInfo> delta = area.ComputeVisibilityDelta(adjArea);

					// keep the smallest delta
					if (anchor == null || (anchor != null && delta.Count < bestDelta.Count)) {
						bestDelta = delta;
						anchor = adjArea;
						Assert(anchor != area);
					}
				}
			}

			if (anchor != null && bestDelta.Count <= nav_max_vis_delta_list_length.GetInt() && anchor != area) {
				area.InheritVisibilityFrom.Area = anchor;
				area.PotentiallyVisibleAreas = bestDelta;

				anchor.IsInheritedFrom = true;
			}
			else
				area.InheritVisibilityFrom.Area = null;
		}

		if (NavArea.TheNavAreas.Count > 0)
			avgVisLength /= NavArea.TheNavAreas.Count;

		Msg($"NavMesh Visibility List Lengths:  min = {minVisLength}, avg = {avgVisLength}, max = {maxVisLength}\n");
	}

	public bool IsEditMode(EditModeType mode) => EditMode == mode;

	EditModeType GetEditMode() => EditMode;

	uint GetSubVersionNumber() => 0;

	public virtual void SaveCustomData(BinaryWriter buffer) { }
	public virtual void LoadCustomData(BinaryReader buffer, uint version) { }

	public virtual void SaveCustomDataPreArea(BinaryWriter buffer) { }
	public virtual void LoadCustomDataPreArea(BinaryReader buffer, uint version) { }

	public static NavArea CreateArea() => new();

	public void DestroyArea(NavArea area) { }

	int ComputeHashKey(uint id) => (int)(id & 0xFF);

	public int WorldToGridX(float wx) {
		int x = (int)((wx - MinX) / GridCellSize);

		if (x < 0)
			x = 0;
		else if (x >= GridSizeX)
			x = GridSizeX - 1;

		return x;
	}

	public int WorldToGridY(float wy) {
		int y = (int)((wy - MinY) / GridCellSize);

		if (y < 0)
			y = 0;
		else if (y >= GridSizeY)
			y = GridSizeY - 1;

		return y;
	}

	public static Mask GetGenerationTraceMask() => Mask.NPCSolidBrushOnly;

	public static HidingSpot? GetHidingSpotByID(uint id) {
		foreach (HidingSpot spot in HidingSpot.TheHidingSpots) {
			if (spot.GetID() == id)
				return spot;
		}

		return null;
	}

	public bool ForAllSelectedAreas(Func<NavArea, bool> func) {
		if (IsSelectedSetEmpty()) {
			NavArea? area = GetSelectedArea();
			if (area != null && !func(area))
				return false;
		}
		else {
			foreach (NavArea area in SelectedSet) {
				if (!func(area))
					return false;
			}
		}

		return true;
	}

	static int SearchMarker = RandomInt(0, 1024 * 1024);

	public bool ForAllAreas(Func<NavArea, bool> func) {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (!func(area))
				return false;
		}
		return true;
	}

	public bool ForAllAreasOverlappingExtent(Func<NavArea, bool> func, Extent extent) {
		if (Grid.Count == 0)
			return true;

		SearchMarker++;
		if (SearchMarker == 0)
			SearchMarker++;

		Extent areaExtent = default;

		int startX = WorldToGridX(extent.Lo.X);
		int endX = WorldToGridX(extent.Hi.X);
		int startY = WorldToGridY(extent.Lo.Y);
		int endY = WorldToGridY(extent.Hi.Y);

		for (int x = startX; x <= endX; ++x) {
			for (int y = startY; y <= endY; ++y) {
				int grid = x + y * GridSizeX;
				if (grid >= Grid.Count)
					return true;

				List<NavArea> areaVector = Grid[grid];

				foreach (NavArea area in areaVector) {
					if (area.NearNavSearchMarker == SearchMarker)
						continue;

					area.NearNavSearchMarker = (uint)SearchMarker;
					area.GetExtent(ref areaExtent);

					if (extent.IsOverlapping(areaExtent)) {
						if (!func(area))
							return false;
					}
				}
			}
		}

		return true;
	}

	public void CollectAreasOverlappingExtent(Extent extent, List<NavArea> outList) {
		if (Grid.Count == 0)
			return;

		SearchMarker++;
		if (SearchMarker == 0)
			SearchMarker++;

		Extent areaExtent = default;

		int startX = WorldToGridX(extent.Lo.X);
		int endX = WorldToGridX(extent.Hi.X);
		int startY = WorldToGridY(extent.Lo.Y);
		int endY = WorldToGridY(extent.Hi.Y);

		for (int x = startX; x <= endX; ++x) {
			for (int y = startY; y <= endY; ++y) {
				int grid = x + y * GridSizeX;
				if (grid >= Grid.Count)
					return;

				List<NavArea> areaVector = Grid[grid];

				foreach (NavArea area in areaVector) {
					if (area.NearNavSearchMarker == SearchMarker)
						continue;

					area.NearNavSearchMarker = (uint)SearchMarker;
					area.GetExtent(ref areaExtent);

					if (extent.IsOverlapping(areaExtent))
						outList.Add(area);
				}
			}
		}
	}

	public bool ForAllAreasInRadius(Func<NavArea, bool> func, Vector3 pos, float radius) {
		SearchMarker++;
		if (SearchMarker == 0)
			SearchMarker++;

		int originX = WorldToGridX(pos.X);
		int originY = WorldToGridY(pos.Y);

		int shiftLimit = (int)MathF.Ceiling(radius / GridCellSize);
		float radiusSq = radius * radius;

		if (radius == 0.0f)
			shiftLimit = Math.Max(GridSizeX, GridSizeY);

		for (int x = originX - shiftLimit; x <= originX + shiftLimit; ++x) {
			if (x < 0 || x >= GridSizeX)
				continue;

			for (int y = originY - shiftLimit; y <= originY + shiftLimit; ++y) {
				if (y < 0 || y >= GridSizeY)
					continue;

				List<NavArea> areaVector = Grid[x + y * GridSizeX];

				foreach (NavArea area in areaVector) {
					if (area.NearNavSearchMarker == SearchMarker)
						continue;

					area.NearNavSearchMarker = (uint)SearchMarker;

					float distSq = Vector3.DistanceSquared(area.GetCenter(), pos);

					if (distSq <= radiusSq || radiusSq == 0) {
						if (!func(area))
							return false;
					}
				}
			}
		}

		return true;
	}

	public bool ForAllAreasAlongLine(Func<NavArea, bool> func, NavArea startArea, NavArea endArea) {
		if (startArea == null || endArea == null)
			return false;

		if (startArea == endArea) {
			func(startArea);
			return true;
		}

		Vector3 start = startArea.GetCenter();
		Vector3 end = endArea.GetCenter();

		Vector3 to = end - start;
		float range = to.Length();
		to /= range;

		const float epsilon = 0.00001f;

		if (range < epsilon) {
			func(startArea);
			return true;
		}

		NavArea? area = startArea;

		while (area != null) {
			func(area);

			if (area == endArea)
				return true;

			Vector3 origin = area.GetCorner(NavCornerType.NorthWest);
			float xMin = origin.X;
			float xMax = xMin + area.GetSizeX();
			float yMin = origin.Y;
			float yMax = yMin + area.GetSizeY();

			Vector3 exit = default;
			NavDirType edge = NavDirType.NumDirections;

			if (to.X < 0.0f) {
				float t = (xMin - start.X) / (end.X - start.X);
				if (t > 0.0f && t < 1.0f) {
					float y = start.Y + t * (end.Y - start.Y);
					if (y >= yMin && y <= yMax) {
						exit.X = xMin;
						exit.Y = y;
						edge = NavDirType.West;
					}
				}
			}
			else {
				float t = (xMax - start.X) / (end.X - start.X);
				if (t > 0.0f && t < 1.0f) {
					float y = start.Y + t * (end.Y - start.Y);
					if (y >= yMin && y <= yMax) {
						exit.X = xMax;
						exit.Y = y;
						edge = NavDirType.East;
					}
				}
			}

			if (edge == NavDirType.NumDirections) {
				if (to.Y < 0.0f) {
					float t = (yMin - start.Y) / (end.Y - start.Y);
					if (t > 0.0f && t < 1.0f) {
						float x = start.X + t * (end.X - start.X);
						if (x >= xMin && x <= xMax) {
							exit.X = x;
							exit.Y = yMin;
							edge = NavDirType.North;
						}
					}
				}
				else {
					float t = (yMax - start.Y) / (end.Y - start.Y);
					if (t > 0.0f && t < 1.0f) {
						float x = start.X + t * (end.X - start.X);
						if (x >= xMin && x <= xMax) {
							exit.X = x;
							exit.Y = yMax;
							edge = NavDirType.South;
						}
					}
				}
			}

			if (edge == NavDirType.NumDirections)
				break;

			List<NavConnect> adjVector = area.GetAdjacentAreas(edge);

			area = null;

			foreach (var conn in adjVector) {
				NavArea adjArea = conn.Area!;
				Vector3 adjOrigin = adjArea.GetCorner(NavCornerType.NorthWest);

				if (edge == NavDirType.North || edge == NavDirType.South) {
					if (adjOrigin.X <= exit.X && adjOrigin.X + adjArea.GetSizeX() >= exit.X) {
						area = adjArea;
						break;
					}
				}
				else {
					if (adjOrigin.Y <= exit.Y && adjOrigin.Y + adjArea.GetSizeY() >= exit.Y) {
						area = adjArea;
						break;
					}
				}
			}
		}

		return false;
	}

	public bool ForAllLadders(Func<NavLadder, bool> func) {
		foreach (NavLadder ladder in Ladders) {
			if (!func(ladder))
				return false;
		}
		return true;
	}

	public bool StitchMesh(Func<NavArea, bool> func) {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (func(area)) {
				StitchAreaIntoMesh(area, NavDirType.North, func);
				StitchAreaIntoMesh(area, NavDirType.South, func);
				StitchAreaIntoMesh(area, NavDirType.East, func);
				StitchAreaIntoMesh(area, NavDirType.West, func);
			}
		}
		return true;
	}
}

[Flags]
public enum HidingSpotFlags : byte
{
	/// <summary>in a corner with good hard cover nearby </summary>
	InCover = 0x01,
	/// <summary>had at least one decent sniping corridor </summary>
	GoodSniperSpot = 0x02,
	/// <summary>can see either very far, or a large area, or both </summary>
	IdealSniperSpot = 0x04,
	/// <summary>spot in the open, usually on a ledge or cliff </summary>
	Exposed = 0x08
};

public class HidingSpot
{
	public readonly static List<HidingSpot> TheHidingSpots = [];

	Vector3 Pos;
	public uint ID;
	uint Marker;
	NavArea? Area;
	public byte Flags;
	public static uint NextID = 1;
	static uint MasterMarker = 0;

	public HidingSpot() {
		Pos = Vector3.Zero;
		ID = NextID++;
		Flags = 0;
		Area = null;
		TheHidingSpots.Add(this);
	}

	public void Save(BinaryWriter fileBuffer, uint version) {
		fileBuffer.Write(ID);
		fileBuffer.Write(Pos.X);
		fileBuffer.Write(Pos.Y);
		fileBuffer.Write(Pos.Z);
		fileBuffer.Write(Flags);
	}

	public void Load(BinaryReader fileBuffer, uint version) {
		ID = fileBuffer.ReadUInt32();
		Pos.X = fileBuffer.ReadSingle();
		Pos.Y = fileBuffer.ReadSingle();
		Pos.Z = fileBuffer.ReadSingle();
		Flags = fileBuffer.ReadByte();

		if (ID >= NextID)
			NextID = ID + 1;
	}

	public NavErrorType PostLoad() {
		Area = NavMesh.Instance!.GetNavArea(Pos with { Z = Pos.Z + Nav.HalfHumanHeight });

		if (Area == null)
			DevWarning($"A Hiding Spot is off of the Nav Mesh at setpos {Pos.X} {Pos.Y} {Pos.Z}\n");

		return NavErrorType.Ok;
	}

	public bool HasGoodCover() => (Flags & (byte)HidingSpotFlags.InCover) != 0;
	public bool IsGoodSniperSpot() => (Flags & (byte)HidingSpotFlags.GoodSniperSpot) != 0;
	public bool IsIdealSniperSpot() => (Flags & (byte)HidingSpotFlags.IdealSniperSpot) != 0;
	bool IsExposed() => (Flags & (byte)HidingSpotFlags.Exposed) != 0;
	int GetFlags() => Flags;
	public Vector3 GetPosition() => Pos;
	public uint GetID() => ID;
	NavArea? GetArea() => Area;
	public void Mark() => Marker = MasterMarker;
	public bool IsMarked() => Marker == MasterMarker;
	public static void ChangeMasterMarker() => ++MasterMarker;
	public void SetFlags(HidingSpotFlags flags) => Flags = (byte)flags;
	public void SetPosition(Vector3 pos) => Pos = pos;
}

public class NavAreaCollector(bool checkForDuplicates = false)
{
	readonly bool CheckForDuplicates = checkForDuplicates;
	public readonly List<NavArea> Areas = [];
	public bool Invoke(NavArea area) {
		if (CheckForDuplicates && Areas.Contains(area))
			return true;

		Areas.Add(area);
		return true;
	}
}


class EditDestroyNotification(NavArea area)
{
	NavArea DeadArea = area;
	public bool Invoke(BaseCombatCharacter actor) {
		// actor.OnNavAreaRemoved(DeadArea);
		return true;
	}
}