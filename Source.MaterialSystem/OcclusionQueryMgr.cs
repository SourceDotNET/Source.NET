using Source.Common.ShaderAPI;

namespace Source.MaterialSystem;

public class OcclusionQueryMgr(MaterialSystem materials)
{
	public const int COUNT_OCCLUSION_QUERY_STACK = 4;

	class OcclusionQueryObject
	{
		public readonly ShaderAPIOcclusionQuery_t[] QueryHandle = new ShaderAPIOcclusionQuery_t[COUNT_OCCLUSION_QUERY_STACK];
		public int LastResult = -1;
		public int FrameIssued = -1;
		public int CurrentIssue = 0;
		public readonly bool[] HasBeenIssued = new bool[COUNT_OCCLUSION_QUERY_STACK];
	}

	readonly Dictionary<OcclusionQueryObjectHandle_t, OcclusionQueryObject> OcclusionQueryObjects = [];
	OcclusionQueryObjectHandle_t NextHandle = 1;
	int FrameCount = 0;

	IShaderAPI ShaderAPI => materials.ShaderAPI;

	public OcclusionQueryObjectHandle_t CreateOcclusionQueryObject() {
		OcclusionQueryObjectHandle_t h = NextHandle++;
		OcclusionQueryObjects.Add(h, new());
		return h;
	}

	public void OnCreateOcclusionQueryObject(OcclusionQueryObjectHandle_t h) {
		OcclusionQueryObject obj = OcclusionQueryObjects[h];
		for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++)
			obj.QueryHandle[i] = ShaderAPI.CreateOcclusionQueryObject();
	}

	public void DestroyOcclusionQueryObject(OcclusionQueryObjectHandle_t occlusionQuery) {
		Assert(OcclusionQueryObjects.ContainsKey(occlusionQuery));
		if (OcclusionQueryObjects.TryGetValue(occlusionQuery, out OcclusionQueryObject? obj)) {
			for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++) {
				if (obj.QueryHandle[i] != IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE)
					ShaderAPI.DestroyOcclusionQueryObject(obj.QueryHandle[i]);
			}
			OcclusionQueryObjects.Remove(occlusionQuery);
		}
	}

	public void AdvanceFrame() {
		++FrameCount;
	}

	public void AllocOcclusionQueryObjects() {
		foreach (OcclusionQueryObject obj in OcclusionQueryObjects.Values) {
			for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++) {
				obj.QueryHandle[i] = ShaderAPI.CreateOcclusionQueryObject();
				obj.HasBeenIssued[i] = false;
			}
		}
	}

	public void FreeOcclusionQueryObjects() {
		foreach (OcclusionQueryObject obj in OcclusionQueryObjects.Values) {
			for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++) {
				if (obj.QueryHandle[i] != IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE) {
					ShaderAPI.DestroyOcclusionQueryObject(obj.QueryHandle[i]);
					obj.QueryHandle[i] = IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE;
					obj.HasBeenIssued[i] = false;
				}
			}
		}
	}

	public void ResetOcclusionQueryObject(OcclusionQueryObjectHandle_t occlusionQuery) {
		Assert(OcclusionQueryObjects.ContainsKey(occlusionQuery));
		if (OcclusionQueryObjects.TryGetValue(occlusionQuery, out OcclusionQueryObject? obj)) {
			for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++)
				obj.HasBeenIssued[i] = false;

			obj.LastResult = -1;
			obj.FrameIssued = -1;
		}
	}

	public void BeginOcclusionQueryDrawing(OcclusionQueryObjectHandle_t occlusionQuery) {
		Assert(OcclusionQueryObjects.ContainsKey(occlusionQuery));
		if (OcclusionQueryObjects.TryGetValue(occlusionQuery, out OcclusionQueryObject? obj)) {
			int current = obj.CurrentIssue;
			ShaderAPIOcclusionQuery_t query = obj.QueryHandle[current];
			if (query != IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE) {
				if (obj.HasBeenIssued[current]) {
					int pixels = ShaderAPI.OcclusionQuery_GetNumPixelsRendered(query, false);
					if ((pixels == IShaderAPI.OCCLUSION_QUERY_RESULT_PENDING) && (obj.FrameIssued == FrameCount)) {
						if (s_WarnCount++ < 5)
							DevWarning("blocking issue in occlusion queries! Grab brian!\n");
					}
					while (!IShaderAPI.OCCLUSION_QUERY_FINISHED(pixels))
						pixels = ShaderAPI.OcclusionQuery_GetNumPixelsRendered(query, true);
					if (pixels >= 0)
						obj.LastResult = pixels;
					obj.HasBeenIssued[current] = false;
				}
				ShaderAPI.BeginOcclusionQueryDrawing(query);
			}
		}
	}
	static int s_WarnCount = 0;

	public void EndOcclusionQueryDrawing(OcclusionQueryObjectHandle_t occlusionQuery) {
		Assert(OcclusionQueryObjects.ContainsKey(occlusionQuery));
		if (OcclusionQueryObjects.TryGetValue(occlusionQuery, out OcclusionQueryObject? obj)) {
			int current = obj.CurrentIssue;
			ShaderAPIOcclusionQuery_t query = obj.QueryHandle[current];
			if (query != IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE) {
				ShaderAPI.EndOcclusionQueryDrawing(query);

				obj.HasBeenIssued[current] = true;
				obj.FrameIssued = FrameCount;

				current = (current + 1) % COUNT_OCCLUSION_QUERY_STACK;
				obj.CurrentIssue = current;
			}
		}
	}

	public void OcclusionQuery_IssueNumPixelsRenderedQuery(OcclusionQueryObjectHandle_t occlusionQuery) {
		Assert(OcclusionQueryObjects.ContainsKey(occlusionQuery));
		if (OcclusionQueryObjects.TryGetValue(occlusionQuery, out OcclusionQueryObject? obj)) {
			for (int i = 0; i < COUNT_OCCLUSION_QUERY_STACK; i++) {
				int index = (obj.CurrentIssue + i) % COUNT_OCCLUSION_QUERY_STACK;
				ShaderAPIOcclusionQuery_t query = obj.QueryHandle[index];
				if (query != IShaderAPI.INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE && obj.HasBeenIssued[index]) {
					int pixels = ShaderAPI.OcclusionQuery_GetNumPixelsRendered(query);
					if (pixels == IShaderAPI.OCCLUSION_QUERY_RESULT_ERROR)
						obj.HasBeenIssued[index] = false;
					else if (pixels >= 0) {
						obj.LastResult = pixels;
						obj.HasBeenIssued[index] = false;
					}
				}
			}
		}
	}

	public int OcclusionQuery_GetNumPixelsRendered(OcclusionQueryObjectHandle_t h, bool doQuery) {
		if (doQuery)
			OcclusionQuery_IssueNumPixelsRenderedQuery(h);

		return OcclusionQueryObjects[h].LastResult;
	}
}
