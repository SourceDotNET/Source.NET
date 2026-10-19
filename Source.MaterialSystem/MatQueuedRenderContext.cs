using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;

using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

using Color = Source.Color;

namespace Source.MaterialSystem;

public sealed unsafe class MatQueuedRenderContext : MatRenderContextBase, IMatRenderContextInternal
{
	static readonly ConVar mat_report_queue_status = new("mat_report_queue_status", "0", FCvar.MaterialSystemThread);

	static readonly HashSet<string> ReportedUnsupported = [];
	public static void CannotSupport([CallerMemberName] string name = "") {
		lock (ReportedUnsupported) {
			if (ReportedUnsupported.Add(name))
				Msg($"Called function that cannot be supported ({name})\n");
		}
	}

	readonly MatCallQueue Queue = new();
	MatQueuedMesh? QueuedMesh;
	MatQueuedMesh? QueuedFlexMesh;

	MatRenderContext? HardwareContext;
	MatRenderContext H => HardwareContext!;

	int RenderDepth;
	int WidthBackBuffer, HeightBackBuffer;
	int BoneCount;
	MaterialFogMode FogModeValue;
	float FogStartValue;
	float FogEndValue;
	float FogZ;
	float FogMaxDensityValue = 1.0f;
	bool FogRadialValue;
	byte FogColorR, FogColorG, FogColorB;

	readonly MatMemoryStack Vertices = new(1024 * 1024, 16);
	readonly MatMemoryStack Indices = new(256 * 1024, 16);
	PrimList[] PrimLists = new PrimList[256];
	int PrimListCount;

	public MatQueuedRenderContext(MaterialSystem materials) : base(materials) { }

	public IShaderAPI ShaderAPI => materials.ShaderAPI;

	public void Init(MatRenderContext hardwareContext) {
		HardwareContext = hardwareContext;
		QueuedMesh = new MatQueuedMesh(this, hardwareContext, false);
		QueuedFlexMesh = new MatQueuedMesh(this, hardwareContext, true);
	}

	public void Shutdown() {
		if (HardwareContext == null)
			return;

		Assert(currentMaterial == null);

		QueuedMesh = null;
		QueuedFlexMesh = null;
		HardwareContext = null;

		Vertices.Term();
		Indices.Term();
		Assert(Queue.Count() == 0);
	}

	public void CompactMemory() {
		Vertices.Term();
		Indices.Term();
	}

	public bool IsInitialized() => HardwareContext != null;

	public void BeginQueue(MatRenderContextBase? initialState = null) {
		initialState ??= HardwareContext;

		InitializeFrom(initialState!);
		ShaderAPI.GetBackBufferDimensions(out WidthBackBuffer, out HeightBackBuffer);
		IMatRenderContext initial = (IMatRenderContext)initialState!;
		FogModeValue = initial.GetFogMode();
		BoneCount = initial.GetCurrentNumBones();
		initial.GetFogDistances(out FogStartValue, out FogEndValue, out FogZ);
	}

	public void EndQueue(bool callQueued = false) {
		if (callQueued)
			CallQueued();

		currentMaterial = null;
		LocalCubemapTexture = null;

		for (int i = 0; i < MAX_FB_TEXTURES; i++)
			CurrentFrameBufferCopyTexture[i] = null;

		for (int i = 0; i < RenderTargetStack.Count; i++) {
			ref RenderTargetStackElement element = ref RenderTargetStack[i];
			for (int j = 0; j < RenderTargetStackElement.NUM_RENDER_TARGET_BINDS; j++)
				element[j] = null;
			element.DepthTexture = null;
		}

		while (RenderTargetStack.Count > 0)
			RenderTargetStack.Pop();
	}

	public void CallQueued(bool termAfterCall = false) {
		if (mat_report_queue_status.GetBool())
			Msg($"{Queue.Count()} calls queued, {Vertices.GetUsed()} bytes verts, {Indices.GetUsed()} bytes indices\n");

		Queue.CallQueued();

		Vertices.FreeAll();
		Indices.FreeAll();
		Array.Clear(PrimLists, 0, PrimListCount);
		PrimListCount = 0;

		if (termAfterCall)
			Shutdown();
	}

	public void FlushQueued() => Queue.Flush();

	public ICallQueue? GetCallQueue() => Queue;
	public MatCallQueue GetCallQueueInternal() => Queue;
	MatCallQueue? IMatRenderContextInternal.GetCallQueueInternal() => Queue;

	public byte* AllocVertices(int verts, int vertexSize) {
		byte* memory = Vertices.Alloc(verts * vertexSize);
		if (memory == null)
			Warning("CMatQueuedRenderContext::AllocVertices failed. Something is trying to render far too many things!\n");
		return memory;
	}

	public ushort* AllocIndices(int indices) {
		ushort* memory = (ushort*)Indices.Alloc(indices * sizeof(ushort));
		if (memory == null)
			Warning("CMatQueuedRenderContext::AllocIndices failed. Something is trying to render far too many things!\n");
		return memory;
	}

	public byte* ReallocVertices(byte* verts, int vertsOld, int vertsNew, int vertexSize) {
		Assert(vertsNew <= vertsOld);
		if (vertsNew < vertsOld)
			Vertices.ShrinkLast(verts, vertsOld * vertexSize, vertsNew * vertexSize);
		return verts;
	}

	public ushort* ReallocIndices(ushort* indices, int indicesOld, int indicesNew) {
		Assert(indicesNew <= indicesOld);
		if (indicesNew < indicesOld)
			Indices.ShrinkLast((byte*)indices, indicesOld * sizeof(ushort), indicesNew * sizeof(ushort));
		return indices;
	}

	void QueueMatrixSync() {
		Queue.QueueCall(static s => s.H.LoadMatrix(in s.Matrix), (H, CurMatrixItem.Matrix));
	}

	public void BeginRender() {
		if (++RenderDepth == 1)
			Queue.QueueCall(static h => h.BeginRender(), H);
	}

	public void EndRender() {
		Assert(HardwareContext != null);
		if (--RenderDepth == 0)
			Queue.QueueCall(static h => h.EndRender(), H);
	}

	public override void Flush(bool flushHardware = false) => Queue.QueueCall(static s => s.H.Flush(s.FlushHardware), (H, FlushHardware: flushHardware));
	public void SwapBuffers() => Queue.QueueCall(static h => h.SwapBuffers(), H);

	public override void SetRenderTargetEx(int renderTargetID, ITexture? newTarget) {
		base.SetRenderTargetEx(renderTargetID, newTarget);
		Queue.QueueCall(static s => s.H.SetRenderTargetEx(s.ID, s.Target), (H, ID: renderTargetID, Target: newTarget));
	}

	public void GetRenderTargetDimensions(out int width, out int height) {
		ITexture? tos = RenderTargetStack.Top().RenderTarget0;
		if (tos != null) {
			width = tos.GetActualWidth();
			height = tos.GetActualHeight();
		}
		else {
			width = WidthBackBuffer;
			height = HeightBackBuffer;
		}
	}

	public override void GetViewport(out int x, out int y, out int width, out int height) {
		Assert(RenderTargetStack.Count > 0);
		ref RenderTargetStackElement element = ref RenderTargetStack.Top();

		if (element.ViewW < 0 || element.ViewH < 0) {
			x = y = 0;
			ITexture? renderTarget = element.RenderTarget0;
			if (renderTarget == null) {
				width = WidthBackBuffer;
				height = HeightBackBuffer;
			}
			else {
				width = renderTarget.GetActualWidth();
				height = renderTarget.GetActualHeight();
			}
		}
		else {
			x = element.ViewX;
			y = element.ViewY;
			width = element.ViewW;
			height = element.ViewH;
		}
	}

	public override void Bind(IMaterial iMaterial, object? proxyData) {
		IMaterialInternal? material = (IMaterialInternal?)iMaterial;
		if (material == null) {
			if (materials.errorMaterial == null)
				return;
			material = materials.errorMaterial;
		}
		else
			material = material.GetRealTimeVersion();

		currentMaterial = material;
		CurrentProxyData = proxyData;

		material.CallBindProxy(proxyData);

		Queue.QueueCall(static s => s.H.Bind(s.Material, s.Proxy), (H, Material: material, Proxy: proxyData));
	}

	public override void BindLocalCubemap(ITexture? texture) {
		base.BindLocalCubemap(texture);
		Queue.QueueCall(static s => s.H.BindLocalCubemap(s.Texture), (H, Texture: texture));
	}

	public override void BindLightmapPage(int lightmapPageID) {
		base.BindLightmapPage(lightmapPageID);
		Queue.QueueCall(static s => s.H.BindLightmapPage(s.Page), (H, Page: lightmapPageID));
	}

	public void DepthRange(float near, float far) => Queue.QueueCall(static s => s.H.DepthRange(s.Near, s.Far), (H, Near: near, Far: far));
	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil = false) => Queue.QueueCall(static s => s.H.ClearBuffers(s.Color, s.Depth, s.Stencil), (H, Color: clearColor, Depth: clearDepth, Stencil: clearStencil));

	public void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat) {
		MaterialLock hLock = materials.Lock();
		CallQueued();
		ShaderAPI.ReadPixels(x, y, width, height, data, dstFormat);
		materials.Unlock(hLock);
	}

	public void ReadPixelsAndStretch(ref Rectangle srcRect, ref Rectangle dstRect, Span<byte> buffer, ImageFormat dstFormat, int dstStride) {
		MaterialLock hLock = materials.Lock();
		CallQueued();
		H.ReadPixelsAndStretch(ref srcRect, ref dstRect, buffer, dstFormat, dstStride);
		materials.Unlock(hLock);
	}

	public void SetAmbientLight(float r, float g, float b) => Queue.QueueCall(static s => s.H.SetAmbientLight(s.R, s.G, s.B), (H, R: r, G: g, B: b));
	public void SetLight(int lightNum, in LightDesc desc) => Queue.QueueCall(static s => s.H.SetLight(s.Num, in s.Desc), (H, Num: lightNum, Desc: desc));
	public void SetLightingOrigin(Vector3 lightingOrigin) => Queue.QueueCall(static s => s.H.SetLightingOrigin(s.Origin), (H, Origin: lightingOrigin));

	public void SetAmbientLightCube(Span<Vector4> cube) {
		InlineArray6<Vector4> copy = default;
		cube[..6].CopyTo(copy);
		Queue.QueueCall(static s => {
			InlineArray6<Vector4> c = s.Cube;
			s.H.SetAmbientLightCube(c);
		}, (H, Cube: copy));
	}

	public void CopyRenderTargetToTexture(ITexture? texture) => Queue.QueueCall(static s => s.H.CopyRenderTargetToTexture(s.Texture!), (H, Texture: texture));

	public override void SetFrameBufferCopyTexture(ITexture? texture, int textureIndex = 0) {
		base.SetFrameBufferCopyTexture(texture, textureIndex);
		Queue.QueueCall(static s => s.H.SetFrameBufferCopyTexture(s.Texture, s.Index), (H, Texture: texture, Index: textureIndex));
	}

	public override void MatrixMode(MaterialMatrixMode mode) {
		base.MatrixMode(mode);
		Queue.QueueCall(static s => s.H.MatrixMode(s.Mode), (H, Mode: mode));
	}

	public override void PushMatrix() {
		base.PushMatrix();
		Queue.QueueCall(static h => h.PushMatrix(), H);
	}

	public override void PopMatrix() {
		base.PopMatrix();
		Queue.QueueCall(static h => h.PopMatrix(), H);
	}

	public override void LoadMatrix(in Matrix4x4 matrix) {
		base.LoadMatrix(in matrix);
		QueueMatrixSync();
	}

	public override void LoadMatrix(Matrix3x4 matrix) {
		base.LoadMatrix(matrix);
		QueueMatrixSync();
	}

	public override void MultMatrix(in Matrix4x4 matrix) {
		base.MultMatrix(in matrix);
		QueueMatrixSync();
	}

	public override void MultMatrix(in Matrix3x4 matrix) {
		base.MultMatrix(in matrix);
		QueueMatrixSync();
	}

	public override void MultMatrixLocal(in Matrix4x4 matrix) {
		base.MultMatrixLocal(in matrix);
		QueueMatrixSync();
	}

	public override void MultMatrixLocal(in Matrix3x4 matrix) {
		base.MultMatrixLocal(in matrix);
		QueueMatrixSync();
	}

	public override void LoadIdentity() {
		base.LoadIdentity();
		Queue.QueueCall(static h => h.LoadIdentity(), H);
	}

	public override void Ortho(double left, double top, double right, double bottom, double near, double far) {
		base.Ortho(left, top, right, bottom, near, far);
		QueueMatrixSync();
	}

	public override void PerspectiveX(double fovx, double aspect, double zNear, double zFar) {
		base.PerspectiveX(fovx, aspect, zNear, zFar);
		QueueMatrixSync();
	}

	public override void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right) {
		base.PerspectiveOffCenterX(fovx, aspect, zNear, zFar, bottom, top, left, right);
		QueueMatrixSync();
	}

	public override void PickMatrix(int x, int y, int width, int height) {
		base.PickMatrix(x, y, width, height);
		QueueMatrixSync();
	}

	public override void Rotate(float angle, float x, float y, float z) {
		base.Rotate(angle, x, y, z);
		QueueMatrixSync();
	}

	public override void Viewport(int x, int y, int width, int height) {
		base.Viewport(x, y, width, height);
		Queue.QueueCall(static s => s.H.Viewport(s.X, s.Y, s.W, s.Height), (H, X: x, Y: y, W: width, Height: height));
	}

	public void CullMode(MaterialCullMode cullMode) => Queue.QueueCall(static s => s.H.CullMode(s.Mode), (H, Mode: cullMode));

	public void FogMode(MaterialFogMode fogMode) {
		FogModeValue = fogMode;
		Queue.QueueCall(static s => s.H.FogMode(s.Mode), (H, Mode: fogMode));
	}

	public void FogStart(float start) {
		FogStartValue = start;
		Queue.QueueCall(static s => s.H.FogStart(s.Start), (H, Start: start));
	}

	public void FogEnd(float end) {
		FogEndValue = end;
		Queue.QueueCall(static s => s.H.FogEnd(s.End), (H, End: end));
	}

	public void FogMaxDensity(float maxDensity) {
		FogMaxDensityValue = maxDensity;
		Queue.QueueCall(static s => s.H.FogMaxDensity(s.Density), (H, Density: maxDensity));
	}

	public void SetFogZ(float fogZ) {
		FogZ = fogZ;
		Queue.QueueCall(static s => s.H.SetFogZ(s.FogZ), (H, FogZ: fogZ));
	}

	public void FogRadial(bool radial) {
		FogRadialValue = radial;
		Queue.QueueCall(static s => s.H.FogRadial(s.Radial), (H, Radial: radial));
	}

	public bool GetFogRadial() => FogRadialValue;

	public MaterialFogMode GetFogMode() => FogModeValue;

	public void FogColor3f(float r, float g, float b) => FogColor3ub((byte)Math.Clamp((int)(r * 255.0f), 0, 255), (byte)Math.Clamp((int)(g * 255.0f), 0, 255), (byte)Math.Clamp((int)(b * 255.0f), 0, 255));
	public void FogColor3fv(ReadOnlySpan<float> rgb) => FogColor3ub((byte)Math.Clamp((int)(rgb[0] * 255.0f), 0, 255), (byte)Math.Clamp((int)(rgb[1] * 255.0f), 0, 255), (byte)Math.Clamp((int)(rgb[2] * 255.0f), 0, 255));

	public void FogColor3ub(byte r, byte g, byte b) {
		FogColorR = r;
		FogColorG = g;
		FogColorB = b;
		Queue.QueueCall(static s => s.H.FogColor3ub(s.R, s.G, s.B), (H, R: r, G: g, B: b));
	}

	public void FogColor3ubv(ReadOnlySpan<byte> rgb) => FogColor3ub(rgb[0], rgb[1], rgb[2]);

	public void GetFogColor(out Color rgb) => rgb = new Color(FogColorR, FogColorG, FogColorB);

	public void GetFogDistances(out float start, out float end, out float fogZ) {
		start = FogStartValue;
		end = FogEndValue;
		fogZ = FogZ;
	}

	public int GetCurrentNumBones() => BoneCount;

	public void SetNumBoneWeights(int numBones) {
		BoneCount = numBones;
		Queue.QueueCall(static s => s.H.SetNumBoneWeights(s.Bones), (H, Bones: numBones));
	}

	public IMesh? CreateStaticMesh(VertexFormat fmt, ReadOnlySpan<char> textureBudgetGroup, IMaterial? material = null) => H.CreateStaticMesh(fmt, textureBudgetGroup, material);
	public void DestroyStaticMesh(IMesh? mesh) => Queue.QueueCall(static s => s.H.DestroyStaticMesh(s.Mesh!), (H, Mesh: mesh));

	public IMesh? GetDynamicMesh(bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) => GetDynamicMeshEx(0, buffered, vertexOverride, indexOverride, autoBind);

	public IMesh? GetDynamicMeshEx(VertexFormat vertexFormat, bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) {
		if (autoBind != null)
			Bind(autoBind, null);

		int currentBoneCount = GetCurrentNumBones();
		Assert(currentBoneCount <= 4);
		if (currentBoneCount > 1)
			--currentBoneCount;

		QueuedMesh!.OnGetDynamicMesh(vertexFormat, buffered ? MatQueuedMesh.MQM_BUFFERED : 0, vertexOverride, indexOverride, GetCurrentMaterialInternal(), currentBoneCount);
		return QueuedMesh;
	}

	public IMesh? GetFlexMesh() {
		QueuedFlexMesh!.OnGetDynamicMesh(0, 0, null, null, null, 0);
		return QueuedFlexMesh;
	}

	public int GetMaxVerticesToRender(IMaterial? material) {
		material = ((IMaterialInternal)material!).GetRealTimeVersion();

		MeshDesc temp = default;
		ShaderAPI.ComputeVertexDescription(default, material.GetVertexFormat(), ref temp);
		int vertexSize = temp.Vertex.ActualVertexSize;
		if (vertexSize == 0) {
			Warning($"bad vertex size for material {material.GetName()}\n");
			return 0;
		}

		int maxVerts = ShaderAPI.GetCurrentDynamicVBSize() / vertexSize;
		return Math.Min(maxVerts, 32767);
	}

	public int GetMaxIndicesToRender() => IMesh.INDEX_BUFFER_SIZE;

	public void GetMaxToRender(IMesh? mesh, bool maxUntilFlush, Span<int> maxVerts, Span<int> maxIndices) {
		Assert(!maxUntilFlush);
		int vertexSize = QueuedMesh!.GetVertexSize();
		maxVerts[0] = vertexSize != 0 ? Math.Min(ShaderAPI.GetCurrentDynamicVBSize() / vertexSize, 32767) : 0;
		maxIndices[0] = IMesh.INDEX_BUFFER_SIZE;
	}

	public int SelectionMode(bool selectionMode) {
		CannotSupport();
		return 0;
	}

	public void SelectionBuffer(Span<uint> buffer) => CannotSupport();
	public void ClearSelectionNames() => Queue.QueueCall(static h => h.ClearSelectionNames(), H);
	public void LoadSelectionName(int name) => Queue.QueueCall(static s => s.H.LoadSelectionName(s.Name), (H, Name: name));
	public void PushSelectionName(int name) => Queue.QueueCall(static s => s.H.PushSelectionName(s.Name), (H, Name: name));
	public void PopSelectionName() => Queue.QueueCall(static h => h.PopSelectionName(), H);

	public void ClearColor3ub(byte r, byte g, byte b) => Queue.QueueCall(static s => s.H.ClearColor3ub(s.R, s.G, s.B), (H, R: r, G: g, B: b));
	public void ClearColor4ub(byte r, byte g, byte b, byte a) => Queue.QueueCall(static s => s.H.ClearColor4ub(s.R, s.G, s.B, s.A), (H, R: r, G: g, B: b, A: a));

	public void OverrideDepthEnable(bool enable, bool depthEnable) => Queue.QueueCall(static s => s.H.OverrideDepthEnable(s.Enable, s.Depth), (H, Enable: enable, Depth: depthEnable));
	public void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable) => Queue.QueueCall(static s => s.H.OverrideAlphaWriteEnable(s.Enable, s.Alpha), (H, Enable: enable, Alpha: alphaWriteEnable));
	public void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable) => Queue.QueueCall(static s => s.H.OverrideColorWriteEnable(s.Enable, s.Color), (H, Enable: overrideEnable, Color: colorWriteEnable));

	public void DrawScreenSpaceQuad(IMaterial? material) => Queue.QueueCall(static s => s.H.DrawScreenSpaceQuad(s.Material), (H, Material: material));

	public void SyncToken(ReadOnlySpan<char> token) => Queue.QueueCall(static s => s.H.SyncToken(s.Token), (H, Token: new string(token)));

	public OcclusionQueryObjectHandle_t CreateOcclusionQueryObject() {
		OcclusionQueryObjectHandle_t h = materials.OcclusionQueryMgr.CreateOcclusionQueryObject();
		Queue.QueueCall(static s => s.Mgr.OnCreateOcclusionQueryObject(s.Handle), (Mgr: materials.OcclusionQueryMgr, Handle: h));
		return h;
	}

	public void DestroyOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) => Queue.QueueCall(static s => s.H.DestroyOcclusionQueryObject(s.Handle), (H, Handle: handle));
	public void BeginOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) => Queue.QueueCall(static s => s.H.BeginOcclusionQueryDrawing(s.Handle), (H, Handle: handle));
	public void EndOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) => Queue.QueueCall(static s => s.H.EndOcclusionQueryDrawing(s.Handle), (H, Handle: handle));
	public void ResetOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) => Queue.QueueCall(static s => s.H.ResetOcclusionQueryObject(s.Handle), (H, Handle: handle));

	public int OcclusionQuery_GetNumPixelsRendered(OcclusionQueryObjectHandle_t handle) {
		Queue.QueueCall(static s => s.Mgr.OcclusionQuery_IssueNumPixelsRenderedQuery(s.Handle), (Mgr: materials.OcclusionQueryMgr, Handle: handle));
		return materials.OcclusionQueryMgr.OcclusionQuery_GetNumPixelsRendered(handle, false);
	}

	public void SetFlashlightMode(bool enable) {
		FlashlightEnable = enable;
		Queue.QueueCall(static s => s.H.SetFlashlightMode(s.Enable), (H, Enable: enable));
	}

	public bool GetFlashlightMode() => FlashlightEnable;

	public void SetFlashlightState(in FlashlightState state, in Matrix4x4 worldToTexture) => Queue.QueueCall(static s => s.H.SetFlashlightState(in s.State, in s.WorldToTexture), (H, State: state, WorldToTexture: worldToTexture));
	public void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? flashlightDepthTexture) => Queue.QueueCall(static s => s.H.SetFlashlightStateEx(in s.State, in s.WorldToTexture, s.Depth), (H, State: state, WorldToTexture: worldToTexture, Depth: flashlightDepthTexture));

	public override void SetHeightClipMode(MaterialHeightClipMode heightClipMode) {
		base.SetHeightClipMode(heightClipMode);
		Queue.QueueCall(static s => s.H.SetHeightClipMode(s.Mode), (H, Mode: heightClipMode));
	}

	public override void SetHeightClipZ(float z) {
		base.SetHeightClipZ(z);
		Queue.QueueCall(static s => s.H.SetHeightClipZ(s.Z), (H, Z: z));
	}

	public override bool EnableClipping(bool enable) {
		Queue.QueueCall(static s => s.H.EnableClipping(s.Enable), (H, Enable: enable));
		return base.EnableClipping(enable);
	}

	public void EnableUserClipTransformOverride(bool enable) => Queue.QueueCall(static s => s.H.EnableUserClipTransformOverride(s.Enable), (H, Enable: enable));
	public void UserClipTransform(in Matrix4x4 worldToView) => Queue.QueueCall(static s => s.H.UserClipTransform(in s.Matrix), (H, Matrix: worldToView));

	public IMorph CreateMorph(MorphFormatFlags format, ReadOnlySpan<char> debugName) {
		CannotSupport();
		return null!;
	}

	public void DestroyMorph(IMorph morph) => Queue.QueueCall(static s => s.H.DestroyMorph(s.Morph), (H, Morph: morph));
	public void BindMorph(IMorph morph) => Queue.QueueCall(static s => s.H.BindMorph(s.Morph), (H, Morph: morph));
	public void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights) => Assert(false);

	public void GetWindowSize(out int width, out int height) {
		width = WidthBackBuffer;
		height = HeightBackBuffer;
	}

	public void DrawScreenSpaceRectangle(IMaterial? material, int destX, int destY, int width, int height, float srcTextureX0, float srcTextureY0, float srcTextureX1, float srcTextureY1, int srcTextureWidth, int srcTextureHeight, IClientRenderable? clientRenderable = null, int xDice = 1, int yDice = 1) {
		IMaterialInternal realTimeVersion = ((IMaterialInternal)material!).GetRealTimeVersion();
		realTimeVersion.CallBindProxy(clientRenderable);
		Queue.QueueCall(static s => s.H.DrawScreenSpaceRectangle(s.Material, s.DestX, s.DestY, s.Width, s.Height, s.Src.X, s.Src.Y, s.Src.Z, s.Src.W, s.SrcWidth, s.SrcHeight, s.Renderable, s.XDice, s.YDice),
			(H, Material: material, DestX: destX, DestY: destY, Width: width, Height: height, Src: new Vector4(srcTextureX0, srcTextureY0, srcTextureX1, srcTextureY1), SrcWidth: srcTextureWidth, SrcHeight: srcTextureHeight, Renderable: clientRenderable, XDice: xDice, YDice: yDice));
	}

	public void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) => Queue.QueueCall(static s => s.H.LoadBoneMatrix(s.Index, in s.Matrix), (H, Index: boneIndex, Matrix: matrix));

	public override void PushRenderTargetAndViewport() {
		base.PushRenderTargetAndViewport();
		Queue.QueueCall(static h => h.PushRenderTargetAndViewport(), H);
	}

	public override void PushRenderTargetAndViewport(ITexture? texture) {
		base.PushRenderTargetAndViewport(texture);
		Queue.QueueCall(static s => s.H.PushRenderTargetAndViewport(s.Texture), (H, Texture: texture));
	}

	public override void PushRenderTargetAndViewport(ITexture? texture, int viewX, int viewY, int viewW, int viewH) {
		base.PushRenderTargetAndViewport(texture, viewX, viewY, viewW, viewH);
		Queue.QueueCall(static s => s.H.PushRenderTargetAndViewport(s.Texture, s.X, s.Y, s.W, s.Height), (H, Texture: texture, X: viewX, Y: viewY, W: viewW, Height: viewH));
	}

	public override void PushRenderTargetAndViewport(ITexture? texture, ITexture? depthTexture, int viewX, int viewY, int viewW, int viewH) {
		base.PushRenderTargetAndViewport(texture, depthTexture, viewX, viewY, viewW, viewH);
		Queue.QueueCall(static s => s.H.PushRenderTargetAndViewport(s.Texture, s.Depth, s.X, s.Y, s.W, s.Height), (H, Texture: texture, Depth: depthTexture, X: viewX, Y: viewY, W: viewW, Height: viewH));
	}

	public override void PopRenderTargetAndViewport() {
		base.PopRenderTargetAndViewport();
		Queue.QueueCall(static h => h.PopRenderTargetAndViewport(), H);
	}

	public void BindLightmatexture(ITexture? lightmapTexture) => Queue.QueueCall(static s => s.H.BindLightmatexture(s.Texture), (H, Texture: lightmapTexture));

	public void CopyRenderTargetToTextureEx(ITexture? texture, int renderTargetID, ref Rectangle srcRect, ref Rectangle dstRect) {
		Rectangle? src = Unsafe.IsNullRef(ref srcRect) ? null : srcRect;
		Rectangle? dst = Unsafe.IsNullRef(ref dstRect) ? null : dstRect;
		Queue.QueueCall(static s => {
			Rectangle src = s.Src.GetValueOrDefault(), dst = s.Dst.GetValueOrDefault();
			s.H.CopyRenderTargetToTextureEx(s.Texture, s.ID, ref s.Src.HasValue ? ref src : ref Unsafe.NullRef<Rectangle>(), ref s.Dst.HasValue ? ref dst : ref Unsafe.NullRef<Rectangle>());
		}, (H, Texture: texture, ID: renderTargetID, Src: src, Dst: dst));
	}

	public void CopyTextureToRenderTargetEx(int renderTargetID, ITexture? texture, ref Rectangle srcRect, ref Rectangle dstRect) {
		Rectangle? src = Unsafe.IsNullRef(ref srcRect) ? null : srcRect;
		Rectangle? dst = Unsafe.IsNullRef(ref dstRect) ? null : dstRect;
		Queue.QueueCall(static s => {
			Rectangle src = s.Src.GetValueOrDefault(), dst = s.Dst.GetValueOrDefault();
			s.H.CopyTextureToRenderTargetEx(s.ID, s.Texture, ref s.Src.HasValue ? ref src : ref Unsafe.NullRef<Rectangle>(), ref s.Dst.HasValue ? ref dst : ref Unsafe.NullRef<Rectangle>());
		}, (H, Texture: texture, ID: renderTargetID, Src: src, Dst: dst));
	}

	public void SetFloatRenderingParameter(int parm_number, float value) => Queue.QueueCall(static s => s.H.SetFloatRenderingParameter(s.Parm, s.Value), (H, Parm: parm_number, Value: value));
	public void SetIntRenderingParameter(int parm_number, int value) => Queue.QueueCall(static s => s.H.SetIntRenderingParameter(s.Parm, s.Value), (H, Parm: parm_number, Value: value));
	public void SetVectorRenderingParameter(int parm_number, in Vector3 value) => Queue.QueueCall(static s => s.H.SetVectorRenderingParameter(s.Parm, in s.Value), (H, Parm: parm_number, Value: value));

	public float GetFloatRenderingParameter(int parmNumber) {
		CannotSupport();
		return 0;
	}

	public int GetIntRenderingParameter(int parmNumber) {
		CannotSupport();
		return 0;
	}

	public Vector3 GetVectorRenderingParameter(int parmNumber) {
		CannotSupport();
		return default;
	}

	public void SetStencilEnable(bool onoff) => Queue.QueueCall(static s => s.H.SetStencilEnable(s.On), (H, On: onoff));
	public void SetStencilFailOperation(StencilOperation op) => Queue.QueueCall(static s => s.H.SetStencilFailOperation(s.Op), (H, Op: op));
	public void SetStencilZFailOperation(StencilOperation op) => Queue.QueueCall(static s => s.H.SetStencilZFailOperation(s.Op), (H, Op: op));
	public void SetStencilPassOperation(StencilOperation op) => Queue.QueueCall(static s => s.H.SetStencilPassOperation(s.Op), (H, Op: op));
	public void SetStencilCompareFunction(StencilComparisonFunction cmpfn) => Queue.QueueCall(static s => s.H.SetStencilCompareFunction(s.Fn), (H, Fn: cmpfn));
	public void SetStencilReferenceValue(int reference) => Queue.QueueCall(static s => s.H.SetStencilReferenceValue(s.Ref), (H, Ref: reference));
	public void SetStencilTestMask(uint msk) => Queue.QueueCall(static s => s.H.SetStencilTestMask(s.Mask), (H, Mask: msk));
	public void SetStencilWriteMask(uint msk) => Queue.QueueCall(static s => s.H.SetStencilWriteMask(s.Mask), (H, Mask: msk));
	public void ClearStencilBufferRectangle(int xmin, int ymin, int xmax, int ymax, int value) => Queue.QueueCall(static s => s.H.ClearStencilBufferRectangle(s.XMin, s.YMin, s.XMax, s.YMax, s.Value), (H, XMin: xmin, YMin: ymin, XMax: xmax, YMax: ymax, Value: value));

	public void PushCustomClipPlane(ReadOnlySpan<float> plane) {
		InlineArray4<float> copy = default;
		plane[..4].CopyTo(copy);
		Queue.QueueCall(static s => {
			InlineArray4<float> p = s.Plane;
			s.H.PushCustomClipPlane(p);
		}, (H, Plane: copy));
	}

	public void PopCustomClipPlane() => Queue.QueueCall(static h => h.PopCustomClipPlane(), H);

	public void DisableAllLocalLights() => Queue.QueueCall(static h => h.DisableAllLocalLights(), H);

	public int CompareMaterialCombos(IMaterial? material1, IMaterial? material2, int lightMapID1, int lightMapID2) {
		CannotSupport();
		return 0;
	}

	public ITexture? GetLocalCubemap() => LocalCubemapTexture;

	public void ClearBuffersObeyStencil(bool clearColor, bool clearDepth) => Queue.QueueCall(static s => s.H.ClearBuffersObeyStencil(s.Color, s.Depth), (H, Color: clearColor, Depth: clearDepth));
	public void ClearBuffersObeyStencilEx(bool clearColor, bool clearAlpha, bool clearDepth) => Queue.QueueCall(static s => s.H.ClearBuffersObeyStencilEx(s.Color, s.Alpha, s.Depth), (H, Color: clearColor, Alpha: clearAlpha, Depth: clearDepth));
	public void PerformFullScreenStencilOperation() => Queue.QueueCall(static h => h.PerformFullScreenStencilOperation(), H);

	public void BeginPIXEvent(Color color, ReadOnlySpan<char> name) => Queue.QueueCall(static s => s.H.BeginPIXEvent(s.Color, s.Name), (H, Color: color, Name: new string(name)));
	public void EndPIXEvent() => Queue.QueueCall(static h => h.EndPIXEvent(), H);
	public void SetPIXMarker(Color color, ReadOnlySpan<char> name) => Queue.QueueCall(static s => s.H.SetPIXMarker(s.Color, s.Name), (H, Color: color, Name: new string(name)));

	public void BeginBatch(IMesh? indices) {
		Assert(indices == QueuedMesh);
		QueuedMesh!.InvalidateAuxMeshSet();
		Queue.QueueCall(static ctx => ctx.H.BeginBatch(ctx.QueuedMesh!.MST_DetachActualMesh()!), this);
	}

	public void BindBatch(IMesh? vertices, IMaterial? autoBind = null) {
		Assert(vertices != QueuedMesh);
		Queue.QueueCall(static s => s.H.BindBatch(s.Vertices!, s.AutoBind), (H, Vertices: vertices, AutoBind: autoBind));
	}

	public void DrawBatch(int firstIndex, int numIndices) => Queue.QueueCall(static s => s.H.DrawBatch(s.First, s.Num), (H, First: firstIndex, Num: numIndices));
	public void EndBatch() => Queue.QueueCall(static h => h.EndBatch(), H);

	public override void SetToneMappingScaleLinear(in Vector3 scale) {
		LastSetToneMapScale = scale;
		Queue.QueueCall(static s => s.H.SetToneMappingScaleLinear(in s.Scale), (H, Scale: scale));
	}

	public override Vector3 GetToneMappingScaleLinear() => LastSetToneMapScale;

	public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias) => Queue.QueueCall(static s => s.H.SetShadowDepthBiasFactors(s.Slope, s.Bias), (H, Slope: slopeScaleDepthBias, Bias: depthBias));
	public void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor) => Queue.QueueCall(static s => s.H.SetScissorRect(s.Left, s.Top, s.Right, s.Bottom, s.Enable), (H, Left: left, Top: top, Right: right, Bottom: bottom, Enable: enableScissor));

	public void BeginMorphAccumulation() => Queue.QueueCall(static h => h.BeginMorphAccumulation(), H);
	public void EndMorphAccumulation() => Queue.QueueCall(static h => h.EndMorphAccumulation(), H);
	public void AccumulateMorph(IMorph morph, ReadOnlySpan<MorphWeight> weights) => Assert(false);

	public void PushDeformation(ref readonly DeformationBase deformation) => CannotSupport();
	public void PopDeformation() => CannotSupport();
	public int GetNumActiveDeformations() => 0;

	public bool GetMorphAccumulatorTexCoord(out Vector2 texCoord, IMorph morph, int vertex) {
		Assert(false);
		texCoord = default;
		return false;
	}

	public void EnableColorCorrection(bool enable) => Queue.QueueCall(static s => s.H.EnableColorCorrection(s.Enable), (H, Enable: enable));

	public ColorCorrectionHandle_t AddLookup(ReadOnlySpan<char> name) {
		MaterialLock hLock = materials.Lock();
		ColorCorrectionHandle_t handle = H.AddLookup(name);
		materials.Unlock(hLock);
		return handle;
	}

	public bool RemoveLookup(ColorCorrectionHandle_t handle) {
		MaterialLock hLock = materials.Lock();
		bool removed = H.RemoveLookup(handle);
		materials.Unlock(hLock);
		return removed;
	}

	public void LockLookup(ColorCorrectionHandle_t handle) {
		MaterialLock hLock = materials.Lock();
		H.LockLookup(handle);
		materials.Unlock(hLock);
	}

	public void LoadLookup(ColorCorrectionHandle_t handle, ReadOnlySpan<char> lookuname) {
		MaterialLock hLock = materials.Lock();
		H.LoadLookup(handle, lookuname);
		materials.Unlock(hLock);
	}

	public void UnlockLookup(ColorCorrectionHandle_t handle) {
		MaterialLock hLock = materials.Lock();
		H.UnlockLookup(handle);
		materials.Unlock(hLock);
	}

	public void SetLookupWeight(ColorCorrectionHandle_t handle, float weight) => Queue.QueueCall(static s => s.H.SetLookupWeight(s.Handle, s.Weight), (H, Handle: handle, Weight: weight));
	public void ResetLookupWeights() => Queue.QueueCall(static h => h.ResetLookupWeights(), H);
	public void SetResetable(ColorCorrectionHandle_t handle, bool resetable) => Queue.QueueCall(static s => s.H.SetResetable(s.Handle, s.Resetable), (H, Handle: handle, Resetable: resetable));

	public void SetFullScreenDepthTextureValidityFlag(bool isValid) => Queue.QueueCall(static s => s.H.SetFullScreenDepthTextureValidityFlag(s.Valid), (H, Valid: isValid));

	public void SetNonInteractivePacifierTexture(ITexture? texture, float normalizedX, float normalizedY, float normalizedSize) => Queue.QueueCall(static s => s.H.SetNonInteractivePacifierTexture(s.Texture, s.X, s.Y, s.Size), (H, Texture: texture, X: normalizedX, Y: normalizedY, Size: normalizedSize));
	public void SetNonInteractiveTempFullscreenBuffer(ITexture? texture, MaterialNonInteractiveMode mode) => Queue.QueueCall(static s => s.H.SetNonInteractiveTempFullscreenBuffer(s.Texture, s.Mode), (H, Texture: texture, Mode: mode));
	public void EnableNonInteractiveMode(MaterialNonInteractiveMode mode) => Queue.QueueCall(static s => s.H.EnableNonInteractiveMode(s.Mode), (H, Mode: mode));
	public void RefreshFrontBufferNonInteractive() => Queue.QueueCall(static h => h.RefreshFrontBufferNonInteractive(), H);

	public E? LockRenderDataTyped<E>(int count, E? srcData = null) where E : class {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public override float Knob(Span<char> knobname, Span<float> setvalue = default) => 0.0f;

	public void GMOD_ForceFilterMode(bool min, int mode) => Queue.QueueCall(static s => s.H.GMOD_ForceFilterMode(s.Min, s.Mode), (H, Min: min, Mode: mode));
	public void GMOD_FlushQueue() { }
	public void OverrideBlend(bool unk1, bool unk2, int unk3, int unk4, int unk5) => Queue.QueueCall(static s => s.H.OverrideBlend(s.A, s.B, s.C, s.D, s.E), (H, A: unk1, B: unk2, C: unk3, D: unk4, E: unk5));
	public void OverrideBlendSeparateAlpha(bool unk1, bool unk2, int unk3, int unk4, int unk5) => Queue.QueueCall(static s => s.H.OverrideBlendSeparateAlpha(s.A, s.B, s.C, s.D, s.E), (H, A: unk1, B: unk2, C: unk3, D: unk4, E: unk5));

	public override void SetCurrentMaterialInternal(IMaterialInternal? mat) {
		base.SetCurrentMaterialInternal(mat);
		Queue.QueueCall(static s => s.H.SetCurrentMaterialInternal(s.Material), (H, Material: mat));
	}

	public override void SetCurrentProxy(object? proxyData) {
		base.SetCurrentProxy(proxyData);
		Queue.QueueCall(static s => s.H.SetCurrentProxy(s.Proxy), (H, Proxy: proxyData));
	}

	public override int GetLightmapPage() {
		CannotSupport();
		return 0;
	}

	public void ForceDepthFuncEquals(bool force) => CannotSupport();

	public bool InFlashlightMode() {
		CannotSupport();
		return false;
	}

	public void BindStandardTexture(Sampler sampler, StandardTextureId id) => CannotSupport();

	public void GetLightmapDimensions(out int w, out int h) {
		CannotSupport();
		w = h = 0;
	}

	public MorphFormatFlags GetBoundMorphFormat() {
		CannotSupport();
		return 0;
	}

	public void DrawClearBufferQuad(byte r, byte g, byte b, byte a, bool clearColor, bool clearAlpha, bool clearDepth) => CannotSupport();

	public bool OnDrawMesh(IMesh mesh, int firstIndex, int numIndices) {
		Assert(mesh != QueuedMesh);
		Queue.QueueCall(static s => s.Mesh.Draw(s.First, s.Num), (Mesh: mesh, First: firstIndex, Num: numIndices));
		return false;
	}

	public bool OnDrawMesh(IMesh mesh, PrimList* lists, int numLists) {
		if (PrimListCount + numLists > PrimLists.Length)
			Array.Resize(ref PrimLists, Math.Max(PrimLists.Length * 2, PrimListCount + numLists));

		int offset = PrimListCount;
		new ReadOnlySpan<PrimList>(lists, numLists).CopyTo(PrimLists.AsSpan(offset));
		PrimListCount += numLists;

		Queue.QueueCall(static s => s.Ctx.DeferredDrawPrimList(s.Mesh, s.Offset, s.Count), (Ctx: this, Mesh: mesh, Offset: offset, Count: numLists));
		return false;
	}

	void DeferredDrawPrimList(IMesh mesh, int offset, int count) {
		Assert(mesh != QueuedMesh);
		mesh.Draw(new ReadOnlySpan<PrimList>(PrimLists, offset, count), count);
	}

	void DeferredSetFlexMesh(IMesh staticMesh) => staticMesh.SetFlexMesh(QueuedFlexMesh!.MST_GetActualMesh(), QueuedFlexMesh.MST_GetActualVertexOffsetInBytes());

	public bool OnSetFlexMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes) {
		Assert(mesh == QueuedFlexMesh || mesh == null);
		if (mesh != null)
			Queue.QueueCall(static s => s.Ctx.DeferredSetFlexMesh(s.Mesh), (Ctx: this, Mesh: staticMesh));
		else
			Queue.QueueCall(static mesh => mesh.SetFlexMesh(null, 0), staticMesh);
		return false;
	}

	public bool OnSetColorMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes) {
		Assert(staticMesh != QueuedMesh && staticMesh != QueuedFlexMesh);
		Queue.QueueCall(static s => s.Static.SetColorMesh(MatQueuedMesh.ResolveMesh(s.Mesh)!, s.Offset), (Static: staticMesh, Mesh: mesh, Offset: nVertexOffsetInBytes));
		return false;
	}

	public bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type) {
		Assert(mesh != QueuedMesh && mesh != QueuedFlexMesh);
		Queue.QueueCall(static s => s.Mesh.SetPrimitiveType(s.Type), (Mesh: mesh, Type: type));
		return false;
	}

	public bool OnFlushBufferedPrimitives() {
		Queue.QueueCall(static shaderAPI => shaderAPI.FlushBufferedPrimitives(), ShaderAPI);
		return false;
	}

	public void ForceHardwareSync() => Queue.QueueCall(static h => h.ForceHardwareSync(), H);
	public void BeginFrame() => Queue.QueueCall(static h => h.BeginFrame(), H);
	public void EndFrame() => Queue.QueueCall(static h => h.EndFrame(), H);

	public override void SetFrameTime(double frameTime) {
		Queue.QueueCall(static s => s.H.SetFrameTime(s.Time), (H, Time: frameTime));
		base.SetFrameTime(frameTime);
	}

	public int AddRef() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}

	public int Release() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}
}
