global using static Source.Engine.CollisionBSPDataStatic;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Source.Engine;

public static class CollisionBSPDataStatic
{
	static readonly CollisionBSPData g_BSPData = new();
	public static CollisionBSPData GetCollisionBSPData() => g_BSPData;
}

public class CollisionBSPData
{
	public string? MapName;
	public string? MapNullName;
	public readonly List<CollisionModel> MapCollisionModels = [];
	public readonly List<CollisionSurface> MapSurfaces = [];
	public readonly List<CollisionPlane> MapPlanes = [];
	public readonly List<CollisionBrush> MapBrushes = [];
	public readonly List<CollisionBrushSide> MapBrushSides = [];
	public readonly List<CollisionBoxBrush> MapBoxBrushes = [];
	public readonly List<CollisionNode> MapNodes = [];
	public readonly List<CollisionLeaf> MapLeafs = [];
	public readonly List<ushort> MapDispList = [];
	public readonly List<ushort> MapLeafBrushes = [];
	public readonly List<string?> TextureNames = [];
	public static readonly CollisionSurface NullSurface = new() { Name = "**empty**", Flags = 0, SurfaceProps = 0 };
	public string? MapEntityString;
	public byte[]? MapEntityData; // raw entity lump bytes (server parse pipeline; C++ char*)

	IMaterialSystem? materials;

	public byte[]? MapVis;
	public int NumVisibility;

	public CollisionArea[] MapAreas = [];
	public BSPDAreaPortal[] MapAreaPortals = [];
	public bool[] PortalOpen = [];
	public int NumAreaPortals;
	public int NumPortalOpen;
	public int FloodValid;

	public int MapRootNode;
	public int SolidLeaf;
	public int EmptyLeaf;

	// More of these should just be explicit Count's into their respective lists, honestly
	public int NumSurfaces;
	public int NumLeafs;
	public int NumBrushes => MapBrushes.Count;
	public int NumAreas;
	public int NumPlanes => MapPlanes.Count;
	public int NumClusters;
	public int NumNodes;
	public int NumTextures;
	public int NumBrushSides;
	public int NumBoxBrushes;

	public ref readonly CollisionSurface GetSurfaceAtIndex(ushort surfaceIndex) {
		if (surfaceIndex == SURFACE_INDEX_INVALID)
			return ref NullSurface;
		return ref MapSurfaces.AsSpan()[surfaceIndex];
	}

	internal bool Init() {
		NumLeafs = 1;
		MapVis = null;
		NumAreas = 1;
		NumClusters = 1;
		MapNullName = "**empty**";
		NumTextures = 0;

		return true;
	}
	internal void PreLoad() {
		Init();
	}
	internal void Destroy() {
		for (int i = 0; i < MapCollisionModels.Count; i++)
			physcollision.VCollideUnload(MapCollisionModels[i].VCollisionData);

		// DispCollTrees_FreeLeafList(this);
		// CM.DestroyDispPhysCollide();
		// DispCollTrees_Free(CM.g_DispCollTrees);
		// CM.g_DispCollTrees = null;
		// CM.g_DispBounds = null;
		CM.g_DispCollTreeCount = 0;

		MapPlanes.Clear();
		TextureNames.Clear();
		MapSurfaces.Clear();
		MapAreaPortals = [];
		PortalOpen = [];
		MapAreas = [];
		MapEntityString = null;
		MapBrushes.Clear();
		MapDispList.Clear();
		MapCollisionModels.Clear();
		MapLeafBrushes.Clear();
		MapLeafs.Clear();
		MapNodes.Clear();
		MapBrushSides.Clear();
		MapVis = null;

		NumBrushSides = 0;
		EmptyLeaf = SolidLeaf = 0;
		NumNodes = 0;
		NumLeafs = 0;
		NumAreas = 0;
		NumTextures = 0;
		FloodValid = 0;
		NumAreaPortals = 0;
		NumClusters = 0;
		NumVisibility = 0;
		NumPortalOpen = 0;
		MapName = null;
		MapRootNode = 0;
	}
	internal void LoadTextures() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.TexData);
		MapLoadHelper lhStringData = new MapLoadHelper(LumpIndex.TexDataStringData);
		MapLoadHelper lhStringTable = new MapLoadHelper(LumpIndex.TexDataStringTable);
		Span<byte> stringData = lhStringData.LoadLumpData<byte>();
		Span<int> stringTable = lhStringTable.LoadLumpData<int>();

		BSPDTexData[] inData = lh.LoadLumpData<BSPDTexData>(throwIfNoElements: true, maxElements: BSPFileCommon.MAX_MAP_TEXDATA, sysErrorIfOOB: true);
		IMaterial? material;
		MapSurfaces.Clear(); MapSurfaces.EnsureCapacity(inData.Length);
		TextureNames.Clear(); TextureNames.EnsureCapacity(inData.Length);
		int lastNull = -1;
		for (int i = 0; i < stringData.Length; i++) {
			ref byte c = ref stringData[i];
			if (c == 0) {
				TextureNames.Add(Encoding.ASCII.GetString(stringData[(lastNull + 1)..i]));
				lastNull = i;
			}
		}
		NumTextures = inData.Length;

		for (int i = 0; i < inData.Length; i++) {
			ref BSPDTexData _in = ref inData[i];
			Assert(_in.NameStringTableID >= 0);
			Assert(stringTable[_in.NameStringTableID] > 0);

			int index = _in.NameStringTableID;

			MapSurfaces.Add(new CollisionSurface() {
				Name = TextureNames[index]!,
				SurfaceProps = 0,
				Flags = 0
			});

			material = materials!.FindMaterial(MapSurfaces[i].Name, MaterialDefines.TEXTURE_GROUP_WORLD, true);
			if (!material.IsErrorMaterial()) {
				IMaterialVar var;
				bool varFound;
				var = material.FindVar("$surfaceprop", out varFound, false);
				if (varFound) {
					ReadOnlySpan<char> props = var.GetStringValue();
					// TODO: set surface properties.
				}
			}
		}
	}
	internal void LoadTexinfo(List<ushort> map_texinfo) {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.TexInfo);
		BSPTexInfo[] inData = lh.LoadLumpData<BSPTexInfo>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_TEXINFO, sysErrorIfOOB: true);
		map_texinfo.Clear();
		map_texinfo.EnsureCount(inData.Length);
		ushort _out;
		Span<CollisionSurface> mapSurfaces = MapSurfaces.AsSpan();
		for (int i = 0; i < inData.Length; i++) {
			ref BSPTexInfo _in = ref inData[i];
			_out = (ushort)_in.TexData;
			if (_out >= NumTextures)
				_out = 0;
			mapSurfaces[_out].Flags |= (ushort)_in.Flags;
			map_texinfo[i] = _out;
		}
	}
	internal void LoadLeafs() {
		MapLoadHelper lh = new(LumpIndex.Leafs);
		switch (lh.LumpVersion) {
			case 0:
				CollisionBSPData_LoadLeafs_Version_0(lh);
				break;
			case 1:
				CollisionBSPData_LoadLeafs_Version_1(lh);
				break;
			default:
				Assert(false);
				Error("Unknown LUMP_LEAFS version\n");
				break;
		}
	}

	private void CollisionBSPData_LoadLeafs_Version_1(MapLoadHelper lh) {
		BSPDLeaf[] inData = lh.LoadLumpData<BSPDLeaf>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_LEAFS, sysErrorIfOOB: true);
		int count = inData.Length;
		MapLeafs.Clear(); MapLeafs.EnsureCount(count + 1);

		NumLeafs = count;
		NumClusters = 0;

		Span<CollisionLeaf> mapLeafs = MapLeafs.AsSpan();
		for (int i = 0; i < count; i++) {
			ref BSPDLeaf _in = ref inData[i];
			ref CollisionLeaf _out = ref mapLeafs[i];
			_out.Contents = (Contents)_in.Contents;
			_out.Cluster = _in.Cluster;
			_out.Area = _in.Area;
			_out.Flags = _in.Flags;
			_out.FirstLeafBrush = _in.FirstLeafBrush;
			_out.NumLeafBrushes = _in.NumLeafBrushes;

			_out.DispCount = 0;

			if (_out.Cluster >= NumClusters)
				NumClusters = _out.Cluster + 1;

		}

		if (mapLeafs[0].Contents != Contents.Solid)
			Sys.Error("Map leaf 0 is not Contents.Solid");


		SolidLeaf = 0;
		EmptyLeaf = NumLeafs;
		memreset(ref MapLeafs.AsSpan()[EmptyLeaf]);
		NumLeafs++;
	}

	private void CollisionBSPData_LoadLeafs_Version_0(MapLoadHelper lh) {
		BSPDLeafVersion0[] inData = lh.LoadLumpData<BSPDLeafVersion0>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_PLANES, sysErrorIfOOB: true);
		int count = inData.Length;
		MapLeafs.Clear(); MapLeafs.EnsureCount(count + 1);

		NumLeafs = count;
		NumClusters = 0;

		Span<CollisionLeaf> mapLeafs = MapLeafs.AsSpan();
		for (int i = 0; i < count; i++) {
			ref BSPDLeafVersion0 _in = ref inData[i];
			ref CollisionLeaf _out = ref mapLeafs[i];
			_out.Contents = (Contents)_in.Contents;
			_out.Cluster = _in.Cluster;
			_out.Area = _in.Area;
			_out.Flags = _in.Flags;
			_out.FirstLeafBrush = _in.FirstLeafBrush;
			_out.NumLeafBrushes = _in.NumLeafBrushes;

			_out.DispCount = 0;

			if (_out.Cluster >= NumClusters)
				NumClusters = _out.Cluster + 1;

		}

		if (mapLeafs[0].Contents != Contents.Solid)
			Sys.Error("Map leaf 0 is not Contents.Solid");


		SolidLeaf = 0;
		EmptyLeaf = NumLeafs;
		memreset(ref MapLeafs.AsSpan()[EmptyLeaf]);
		NumLeafs++;
	}

	internal void LoadLeafBrushes() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.LeafBrushes);
		ushort[] inData = lh.LoadLumpData<ushort>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_LEAFBRUSHES, sysErrorIfOOB: true);

		MapLeafBrushes.Clear(); MapLeafBrushes.AddRange(inData);
	}

	internal void LoadPlanes() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Planes);
		BSPDPlane[] inData = lh.LoadLumpData<BSPDPlane>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_PLANES, sysErrorIfOOB: true);
		MapPlanes.Clear(); MapPlanes.EnsureCount(inData.Length + 1);

		Span<CollisionPlane> planes = MapPlanes.AsSpan();
		int count = inData.Length;
		for (int i = 0; i < count; i++) {
			ref readonly BSPDPlane _in = ref inData[i];
			ref CollisionPlane _out = ref planes[i];
			int bits = 0;
			for (int j = 0; j < 3; j++) {
				_out.Normal[j] = _in.Normal[j];
				if (_out.Normal[j] < 0)
					bits |= 1 << j;
			}

			_out.Dist = _in.Dist;
			_out.Type = (PlaneType)_in.Type;
			_out.SignBits = (byte)bits;
		}
	}
	internal void LoadBrushes() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Brushes);
		BSPDBrush[] inData = lh.LoadLumpData<BSPDBrush>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_BRUSHES, sysErrorIfOOB: true);

		MapBrushes.EnsureCountNew(inData.Length);
		Span<CollisionBrush> outData = MapBrushes.AsSpan();
		for (int i = 0; i < inData.Length; i++) {
			ref CollisionBrush outBrush = ref outData[i];
			ref BSPDBrush inBrush = ref inData[i];

			outBrush.FirstBrushSide = inBrush.FirstSide;
			outBrush.NumSides = inBrush.NumSides;
			outBrush.Contents = (Contents)inBrush.Contents;
		}
	}
	const int SURFACE_INDEX_INVALID = 0xFFFF;

	internal void LoadBrushSides(List<ushort> map_texinfo) {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.BrushSides);
		BSPDBrushSide[] inData = lh.LoadLumpData<BSPDBrushSide>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_BRUSHSIDES, sysErrorIfOOB: true);

		// Brushes are compressed on load to remove any AABB brushes.  The brushsides for those are removed
		// and those brushes are stored as cboxbrush_t.  But the texinfo/surface data needs to be copied
		// So the algorithm is:
		//
		// count box brushes
		// count total brush sides
		// allocate
		// iterate brushes and copy sides or fill out box brushes
		// done
		//

		int boxBrushCount = 0;
		int brushSideCount = 0;
		for (int i = 0; i < NumBrushes; i++) {
			if (IsBoxBrush(in MapBrushes.AsSpan()[i], inData, MapPlanes.AsSpan())) {
				// mark as axial
				MapBrushes.AsSpan()[i].NumSides = CollisionBrush.NUMSIDES_BOXBRUSH;
				boxBrushCount++;
			}
			else {
				brushSideCount += MapBrushes.AsSpan()[i].NumSides;
			}
		}

		MapBrushSides.EnsureCountNew(brushSideCount);
		MapBoxBrushes.EnsureCountNew(boxBrushCount);
		Span<CollisionBrushSide> brushsides = MapBrushSides.AsSpan();
		Span<CollisionBoxBrush> boxbrushes = MapBoxBrushes.AsSpan();

		int outBoxBrush = 0;
		int outBrushSide = 0;

		Span<CollisionBrush> brushes = MapBrushes.AsSpan();
		for (int i = 0; i < NumBrushes; i++) {
			ref CollisionBrush brush = ref brushes[i];
			if (brush.IsBox()) { // fill out the box brush - extract from the input sides
				ref CollisionBoxBrush box = ref boxbrushes[outBoxBrush];
				ExtractBoxBrush(ref box, brush, inData, MapPlanes.AsSpan(), map_texinfo);
				brush.SetBox(outBoxBrush);
				outBoxBrush++;
			}
			else { // copy each side into the output array
				int firstInputSide = brush.FirstBrushSide;
				brush.FirstBrushSide = outBrushSide;
				for (int j = 0; j < brush.NumSides; j++) {
					ref CollisionBrushSide side = ref brushsides[outBrushSide];
					ref BSPDBrushSide inputSide = ref inData[firstInputSide + j];
					side.SetPlanePointer(MapPlanes.Base(), inputSide.PlaneNum);
					int t = inputSide.TexInfo;
					if (t >= map_texinfo.Count)
						Sys.Error("Bad brushside texinfo");


					// BUGBUG: Why is vbsp writing out -1 as the texinfo id?  (TEXINFO_NODE ?)
					side.SurfaceIndex = (ushort)((t < 0) ? SURFACE_INDEX_INVALID : map_texinfo[t]);
					side.Bevel = inputSide.Bevel != 0 ? true : false;
					outBrushSide++;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ExtractBoxBrush(ref CollisionBoxBrush box, in CollisionBrush brush, Span<BSPDBrushSide> sides, Span<CollisionPlane> planes, List<ushort> map_texinfo) {
		// brush.numsides is no longer valid.  Assume numsides == 6
		for (int i = 0; i < 6; i++) {
			ref BSPDBrushSide side = ref sides[i + brush.FirstBrushSide];
			ref CollisionPlane plane = ref planes[side.PlaneNum];
			int t = side.TexInfo;
			Assert(t < map_texinfo.Count);
			int surfaceIndex = (t < 0) ? SURFACE_INDEX_INVALID : map_texinfo[t];
			int axis = (int)plane.Type;
			Assert(MathF.Abs(plane.Normal[axis]) == 1.0f);
			if (plane.Normal[axis] == 1.0f) {
				box.Maxs[axis] = plane.Dist;
				box.SurfaceIndex[axis + 3] = (ushort)surfaceIndex;
			}
			else if (plane.Normal[axis] == -1.0f) {
				box.Mins[axis] = -plane.Dist;
				box.SurfaceIndex[axis] = (ushort)surfaceIndex;
			}
			else
				Assert(false);
		}
		box.Pad2[0] = 0;
		box.Pad2[1] = 0;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsBoxBrush(in CollisionBrush brush, ReadOnlySpan<BSPDBrushSide> sides, ReadOnlySpan<CollisionPlane> planes) {
		int countAxial = 0;
		if (brush.NumSides == 6) {
			for (int i = 0; i < brush.NumSides; i++) {
				ref readonly CollisionPlane plane = ref planes[sides[brush.FirstBrushSide + i].PlaneNum];
				if (plane.Type > PlaneType.NormalZ)
					break;
				countAxial++;
			}
		}
		return (countAxial == brush.NumSides) ? true : false;
	}

	internal void LoadSubmodels() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Models);
		BSPDModel[] inData = lh.LoadLumpData<BSPDModel>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_MODELS, sysErrorIfOOB: true);

		MapCollisionModels.EnsureCountNew(inData.Length);

		for (int i = 0; i < inData.Length; i++) {
			CollisionModel outModel = MapCollisionModels[i];
			ref BSPDModel inModel = ref inData[i];

			for (int j = 0; j < 3; j++) {   // spread the mins / maxs by a pixel
				outModel.Mins[j] = inModel.Mins[j] - 1;
				outModel.Maxs[j] = inModel.Maxs[j] + 1;
				outModel.Origin[j] = inModel.Origin[j];
			}
			outModel.HeadNode = inModel.HeadNode;
		}
	}
	internal void LoadNodes() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Nodes);
		BSPDNode[] inData = lh.LoadLumpData<BSPDNode>(throwIfNoElements: true, BSPFileCommon.MAX_MAP_NODES, sysErrorIfOOB: true);
		int count = inData.Length;
		MapNodes.Clear(); MapNodes.EnsureCount(count + 6);

		NumNodes = count;
		MapRootNode = 0;

		Span<CollisionNode> outNodes = MapNodes.AsSpan();
		Span<CollisionPlane> planes = MapPlanes.AsSpan();
		for (int i = 0; i < count; i++) {
			ref BSPDNode _in = ref inData[i];
			ref CollisionNode _out = ref outNodes[i];
			_out.CollisionPlaneIdx = _in.PlaneNum;
			for (int j = 0; j < 2; j++)
				_out.Children[j] = _in.Children[j];
		}
	}

	internal unsafe void LoadPhysics() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.PhysCollide);
		if (lh.LumpSize == 0)
			return;

		Span<byte> ptr = lh.LoadLumpBaseRaw();
		Span<byte> basePtr = ptr;

		BSPDPhysModel physModel;
		do {
			physModel = ptr.Cast<byte, BSPDPhysModel>()[0];
			ptr = ptr[sizeof(BSPDPhysModel)..];

			if (physModel.DataSize > 0) {
				CollisionModel model = MapCollisionModels[physModel.ModelIndex];
				physcollision.VCollideLoad(model.VCollisionData, physModel.SolidCount, ptr[..(physModel.DataSize + physModel.KeyDataSize)]);
				ptr = ptr[physModel.DataSize..];
				ptr = ptr[physModel.KeyDataSize..];
			}

			// avoid infinite loop on badly formed file
			if (ptr.Length <= 0)
				break;

		} while (physModel.DataSize > 0);
	}

	internal void LoadAreas() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Areas);
		BSPDArea[] inData = lh.LoadLumpData<BSPDArea>(maxElements: BSPFileCommon.MAX_MAP_AREAS, sysErrorIfOOB: true);

		int count = inData.Length;
		MapAreas = new CollisionArea[count];
		NumAreas = count;

		for (int i = 0; i < count; i++) {
			ref CollisionArea output = ref MapAreas[i];
			output.NumAreaPortals = inData[i].NumAreaPortals;
			output.FirstAreaPortal = inData[i].FirstAreaPortal;
			output.FloodValid = 0;
			output.FloodNum = 0;
		}
	}
	internal void LoadAreaPortals() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.AreaPortals);
		BSPDAreaPortal[] inData = lh.LoadLumpData<BSPDAreaPortal>(maxElements: BSPFileCommon.MAX_MAP_AREAPORTALS, sysErrorIfOOB: true);

		int count = inData.Length + 1;

		NumPortalOpen = count;
		PortalOpen = new bool[count];

		NumAreaPortals = count;
		MapAreaPortals = new BSPDAreaPortal[count];
		inData.CopyTo(MapAreaPortals, 0);
	}
	internal void LoadVisibility() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Visibility);

		int visDataSize = lh.LumpSize;

		NumVisibility = visDataSize;
		if (visDataSize > BSPFileCommon.MAX_MAP_VISIBILITY)
			Sys.Error($"Map '{lh.GetMapName().SliceNullTerminatedString()}' has too many visibilities ({visDataSize} > max {BSPFileCommon.MAX_MAP_VISIBILITY})");

		if (visDataSize == 0)
			MapVis = null;
		else
			MapVis = lh.LoadLumpData<byte>();
	}
	internal void LoadEntityString() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Entities);
		byte[] inData = lh.LoadLumpData<byte>(throwIfNoElements: true, sysErrorIfOOB: true);
		MapEntityData = inData;
		MapEntityString = Encoding.ASCII.GetString(inData);
	}
	internal void LoadDispInfo() {
		// How many displacements in the map?
		CM.g_DispCollTreeCount = 0;
		nint dispInfoLumpSize = MapLoadHelper.GetLumpSize(LumpIndex.DispInfo);
		int coreDispCount = (int)(dispInfoLumpSize / Unsafe.SizeOf<BSPDDispInfo>());
		if (coreDispCount == 0)
			return;

		// Geometry needed to reconstruct each displacement's base surface.
		BSPDertex[] verts = new MapLoadHelper(LumpIndex.Vertexes).LoadLumpData<BSPDertex>();
		BSPDEdge[] edges = new MapLoadHelper(LumpIndex.Edges).LoadLumpData<BSPDEdge>();
		int[] surfEdges = new MapLoadHelper(LumpIndex.SurfEdges).LoadLumpData<int>();
		LumpIndex faceLumpToLoad = LumpIndex.Faces;
		if (HardwareConfig.GetHDRType() != HDRType.None && MapLoadHelper.GetLumpSize(LumpIndex.FacesHDR) > 0)
			faceLumpToLoad = LumpIndex.FacesHDR;
		BSPDFace[] faces = new MapLoadHelper(faceLumpToLoad).LoadLumpData<BSPDFace>();
		BSPDDispInfo[] dispInfos = new MapLoadHelper(LumpIndex.DispInfo).LoadLumpData<BSPDDispInfo>();
		DispVert[] dispVerts = new MapLoadHelper(LumpIndex.DispVerts).LoadLumpData<DispVert>();
		DispTri[] dispTris = new MapLoadHelper(LumpIndex.DispTris).LoadLumpData<DispTri>();

		if (faces.Length == 0)
			return;

		// Allocate displacement collision trees + culling bounds.
		CM.g_DispCollTreeCount = coreDispCount;
		g_DispCollTrees = new DispCollTree[coreDispCount];
		g_DispBounds = new AlignedBBox[coreDispCount];
		for (int i = 0; i < coreDispCount; i++) {
			g_DispCollTrees[i] = new DispCollTree { Counter = i };
			g_DispCollTrees[i].SetPower(0);
		}

		// Build the inverse mapping from disp index to face.
		ushort[] dispIndexToFaceIndex = new ushort[coreDispCount];
		Array.Fill(dispIndexToFaceIndex, (ushort)0xFFFF);
		for (int i = 0; i < faces.Length; ++i) {
			short dispInfoIndex = faces[i].DispInfo;
			if (dispInfoIndex == -1 || dispInfoIndex >= coreDispCount)
				continue;
			dispIndexToFaceIndex[dispInfoIndex] = (ushort)i;
		}

		Span<Vector3> surfPoints = stackalloc Vector3[4];
		for (int i = 0; i < coreDispCount; ++i) {
			// Find the face associated with this dispinfo.
			ushort faceIndex = dispIndexToFaceIndex[i];
			if (faceIndex == 0xFFFF)
				continue;

			ref BSPDDispInfo dispInfo = ref dispInfos[i];

			// Set up the CCoreDispInfo from the lump data.
			Span<DispVert> vSlice = dispVerts.AsSpan(dispInfo.DispVertStart, dispInfo.NumVerts());
			Span<DispTri> tSlice = dispTris.AsSpan(dispInfo.DispTriStart, dispInfo.NumTris());

			CoreDispInfo coreDisp = new();
			CoreDispSurface dispSurf = coreDisp.GetSurface();
			dispSurf.SetPointStart(dispInfo.StartPosition);
			dispSurf.SetContents(dispInfo.Contents);
			coreDisp.InitDispInfo(dispInfo.Power, dispInfo.MinTess, dispInfo.SmoothingAngle, vSlice, tSlice);

			// Hook the disp surface to the face.
			ref BSPDFace face = ref faces[faceIndex];
			dispSurf.SetHandle(faceIndex);

			// Get the base surface points from the face's edges.
			if (face.NumEdges > 4)
				continue;

			for (int j = 0; j < face.NumEdges; j++) {
				int eIndex = surfEdges[face.FirstEdge + j];
				if (eIndex < 0)
					surfPoints[j] = verts[edges[-eIndex].V[1]].Position;
				else
					surfPoints[j] = verts[edges[eIndex].V[0]].Position;
			}

			dispSurf.SetPointCount(face.NumEdges);
			for (int j = 0; j < 4; j++)
				dispSurf.SetPoint(j, surfPoints[j]);

			dispSurf.FindSurfPointStartIndex();
			dispSurf.AdjustSurfPointData();

			// Generate the collision displacement surface.
			DispCollTree tree = g_DispCollTrees[i];
			tree.SetPower(0);

			// Check for null faces (should have been taken care of in vbsp).
			if (dispSurf.GetPointCount() != 4)
				continue;

			coreDisp.Create();

			tree.Create(coreDisp);
			tree.GetBounds(out Vector3 treeMins, out Vector3 treeMaxs);
			g_DispBounds[i].Init(treeMins, treeMaxs, tree.Counter, tree.GetContents());

			// NOTE: surface props are left at their defaults; physprops material lookup
			// is not yet wired in the collision subsystem (see LoadTextures TODO).
		}

		// Link each displacement into the BSP leaves it touches.
		CM_DispTreeLeafnum();
	}

	// Port of CM_DispTreeLeafnum + CDispLeafBuilder: build the per-leaf displacement lists.
	private void CM_DispTreeLeafnum() {
		if (g_DispCollTrees == null || g_DispCollTrees.Length == 0)
			return;

		int treeCount = g_DispCollTrees.Length;
		Span<CollisionLeaf> leafs = MapLeafs.AsSpan();
		for (int i = 0; i < NumLeafs; i++)
			leafs[i].DispCount = 0;

		int headnode = MapCollisionModels[0].HeadNode;

		// For each displacement, gather the list of leaves it overlaps.
		List<int>[] perDispLeaves = new List<int>[treeCount];
		int totalRefs = 0;
		for (int i = 0; i < treeCount; i++) {
			perDispLeaves[i] = BuildLeafListForDisplacement(i, headnode);
			totalRefs += perDispLeaves[i].Count;
		}

		// Compute per-leaf displacement counts.
		for (int i = 0; i < treeCount; i++)
			foreach (int leafIndex in perDispLeaves[i])
				leafs[leafIndex].DispCount++;

		// Point each leaf at the start of its output range, then reset the running count.
		ushort firstDispIndex = 0;
		for (int i = 0; i < NumLeafs; i++) {
			leafs[i].DispListStart = firstDispIndex;
			firstDispIndex += leafs[i].DispCount;
			leafs[i].DispCount = 0;
		}

		// Write the compact per-leaf reference lists.
		MapDispList.SetCount(totalRefs);
		Span<ushort> outList = MapDispList.AsSpan();
		for (int i = 0; i < treeCount; i++) {
			foreach (int leafIndex in perDispLeaves[i]) {
				int outListIndex = leafs[leafIndex].DispListStart + leafs[leafIndex].DispCount;
				outList[outListIndex] = (ushort)i;
				leafs[leafIndex].DispCount++;
			}
		}
	}

	private List<int> BuildLeafListForDisplacement(int index, int headnode) {
		List<int> result = [];

		DispCollTree tree = g_DispCollTrees![index];
		if (tree == null || tree.GetPower() == 0)
			return result;

		tree.GetBounds(out Vector3 mins, out Vector3 maxs);

		const int MAX_NODES = 1024;
		Span<int> nodeList = stackalloc int[MAX_NODES];
		int listRead = 0;
		int listWrite = 1;
		nodeList[0] = headnode;

		while (listRead != listWrite) {
			int nodeIndex = nodeList[listRead];
			listRead = (listRead + 1) % MAX_NODES;

			if (nodeIndex < 0) {
				// leaf
				int leafIndex = -1 - nodeIndex;
				result.Add(leafIndex);
			}
			else {
				ref CollisionNode node = ref MapNodes.AsSpan()[nodeIndex];
				ref CollisionPlane plane = ref MapPlanes.AsSpan()[node.CollisionPlaneIdx];

				int sideResult = MathLib.BoxOnPlaneSide(mins, maxs, plane);

				if ((sideResult & 1) != 0) {
					nodeList[listWrite] = node.Children[0];
					listWrite = (listWrite + 1) % MAX_NODES;
				}
				if ((sideResult & 2) != 0) {
					nodeList[listWrite] = node.Children[1];
					listWrite = (listWrite + 1) % MAX_NODES;
				}
			}
		}

		return result;
	}
	internal bool Load(ReadOnlySpan<char> name) {
		List<ushort> map_texinfo = [];

		MapName = new(name);

		materials = Singleton<IMaterialSystem>();

		LoadTextures();
		LoadTexinfo(map_texinfo);
		LoadLeafs();
		LoadLeafBrushes();
		LoadPlanes();
		LoadBrushes();
		LoadBrushSides(map_texinfo);
		LoadSubmodels();
		LoadNodes();
		LoadAreas();
		LoadAreaPortals();
		LoadVisibility();
		LoadEntityString();
		LoadPhysics();
		LoadDispInfo();

		return true;
	}
}

public static partial class CM
{
	static uint last_checksum = uint.MaxValue;
	public static void LoadMap(ReadOnlySpan<char> name, bool allowReusePrevious, out uint checksum) {
		CollisionBSPData bspData = GetCollisionBSPData();
		if (name.Equals(bspData.MapName, StringComparison.OrdinalIgnoreCase) && allowReusePrevious) {
			checksum = last_checksum;
			return;
		}

		bspData.PreLoad();
		if (name.IsEmpty) {
			checksum = 0;
			return;
		}

		if (!MapLoadHelper.Init(null, name)) {
			checksum = 0;
			return;
		}

		bspData.Load(name);
		MapLoadHelper.Shutdown();

		DispTreeLeafnum(bspData);
		InitPortalOpenState(bspData);
		FloodAreaConnections(bspData);

		checksum = 0; // << Wtf, this never gets set in the engine? What's the point then???
		return;
	}

	public static void WorldSpaceCenter(ICollideable collideable, out Vector3 center) {
		MathLib.VectorAdd(collideable.OBBMins(), collideable.OBBMaxs(), out Vector3 vecLocalCenter);
		vecLocalCenter *= 0.5f;

		if ((collideable.GetCollisionAngles() == vec3_angle) || (vecLocalCenter == vec3_origin))
			MathLib.VectorAdd(vecLocalCenter, collideable.GetCollisionOrigin(), out center);
		else
			MathLib.VectorTransform(vecLocalCenter, collideable.CollisionToWorldTransform(), out center);
	}

	public static void FreeMap() {
		CollisionBSPData bspData = GetCollisionBSPData();
		bspData.Destroy();
	}

	static void FloodArea_r(CollisionBSPData bspData, ref CollisionArea area, int floodnum) {
		if (area.FloodValid == bspData.FloodValid) {
			if (area.FloodNum == floodnum)
				return;
			Sys.Error("FloodArea_r: reflooded");
		}

		area.FloodNum = floodnum;
		area.FloodValid = bspData.FloodValid;
		for (int i = 0; i < area.NumAreaPortals; i++) {
			ref BSPDAreaPortal p = ref bspData.MapAreaPortals[area.FirstAreaPortal + i];
			if (bspData.PortalOpen[p.PortalKey])
				FloodArea_r(bspData, ref bspData.MapAreas[p.OtherArea], floodnum);
		}
	}

	internal static void FloodAreaConnections(CollisionBSPData bspData) {
		bspData.FloodValid++;
		int floodnum = 0;

		for (int i = 1; i < bspData.NumAreas; i++) {
			ref CollisionArea area = ref bspData.MapAreas[i];
			if (area.FloodValid == bspData.FloodValid)
				continue;
			floodnum++;
			FloodArea_r(bspData, ref area, floodnum);
		}
	}

	private static void InitPortalOpenState(CollisionBSPData bspData) {
		for (int i = 0; i < bspData.NumPortalOpen; i++)
			bspData.PortalOpen[i] = false;
	}

	private static void DispTreeLeafnum(CollisionBSPData bspData) {

	}

	public static VCollide? VCollideForModel(int modelindex, Model? model) {
		if (model != null) {
			switch (model.Type) {
				case ModelType.Brush:
					return GetVCollide(modelindex - 1);
				case ModelType.Studio:
					return mdlcache.GetVCollide(model.Studio);
			}
		}

		return null;
	}

	public static VCollide? GetVCollide(int modelIndex) {
		CollisionModel? model = InlineModelNumber(modelIndex);
		if (model == null)
			return null;

		// return the model's collision data
		return model.VCollisionData;
	}

	internal static CollisionModel? InlineModelNumber(int index) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if ((index < 0) || (index >= bspData.MapCollisionModels.Count))
			return null;

		return (bspData.MapCollisionModels[index]);
	}

	internal static void ClearTrace(ref Trace trace) {
		trace = default;
		trace.Fraction = 1;
		trace.FractionLeftSolid = 0;
		trace.Surface = CollisionBSPData.NullSurface;
	}
}
