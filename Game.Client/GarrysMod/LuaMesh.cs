using CommunityToolkit.HighPerformance;

using Source;
using Source.Common;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client.GarrysMod;

public class LuaMeshObject
{
	public int Refs;
	public IMesh? Mesh;
}

public static partial class LuaMesh
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_mesh = new("mesh");

	[LuaClass(typeof(LuaMeshObject))]
	public static readonly LuaClass LC_IMesh = new("IMesh", LuaType.IMesh, null, null);

	static readonly List<LuaMeshObject> PendingDestroy = [];

	static MeshBuilder Builder = new();
	static IMesh? CurrentMesh;
	static bool IsStatic;
	static int MaxVertices;
	static int VerticesAdded;

	static IMesh? GetMesh(ILuaInterface lua, int stackPos) {
		LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(stackPos);
		if (obj == null || obj.Mesh == null) {
			lua.Error("Tried to use a NULL IMesh!");
			return null;
		}
		return obj.Mesh;
	}

	static bool CanWriteVertex() => VerticesAdded < MaxVertices;

	static void EndMesh() {
		if (CurrentMesh == null)
			return;
		CurrentMesh = null;
		Builder.End(false, !IsStatic);
	}

	public static void FlushPendingDestroy() {
		for (int i = 0; i < PendingDestroy.Count; i++) {
			LuaMeshObject obj = PendingDestroy[i];
			if (CurrentMesh == obj.Mesh)
				EndMesh();
			obj.Refs--;
			if (obj.Refs < 1 && obj.Mesh != null) {
				using MatRenderContextPtr renderContext = new(materials);
				renderContext.DestroyStaticMesh(obj.Mesh);
				obj.Mesh = null;
			}
		}
		PendingDestroy.Clear();
	}

	static void QueueDestroy(ILuaInterface lua) {
		LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(1);
		if (obj == null)
			return;
		if (!PendingDestroy.Contains(obj))
			PendingDestroy.Add(obj);
		lua.SetUserType(1, 0);
	}

	[LuaGlobal("Mesh")]
	static int CreateMesh(ILuaInterface lua) {
		IMaterial? material = null;
		if (lua.GetType(1) == LuaType.Material)
			material = (IMaterial?)LuaMaterial.LC_IMaterial.Get(1);
		int boneWeights = (int)lua.CheckNumberOpt(2, 0);

		VertexFormat format = MaterialVertexFormat.Model;
		if (material != null) {
			if (material.GetVertexFormat() != 0)
				format = material.GetVertexFormat();
			else {
				Warning($"Global.Mesh Warning! Attempted to use a material '{material.GetName()}' with vertex format 0!\n");
				material = null;
			}
		}

		if (boneWeights > 0) {
			if ((uint)(boneWeights - 2) > 1) {
				lua.Error("Invalid bone weight count! Allowed values are 0, 2, or 3.\n");
				return 0;
			}
			format |= VertexExts.GetBoneWeight(boneWeights) | VertexFormat.BoneIndex;
		}

		using MatRenderContextPtr renderContext = new(materials);
		IMesh? mesh = renderContext.CreateStaticMesh(format, "Model Verts", material!);
		if (mesh == null) {
			Warning("Mesh(): Error creating a new mesh!\n");
			return 0;
		}

		LC_IMesh.Push(new LuaMeshObject() { Refs = 1, Mesh = mesh });
		return 1;
	}

	[LuaMethod]
	static int IMesh__BuildFromTriangles(ILuaInterface lua) {
		IMesh? mesh = GetMesh(lua, 1);
		if (!lua.IsType(2, LuaType.Table)) {
			lua.TypeError("table", 2);
			return 0;
		}
		BuildFromTriangles(3, MaterialPrimitiveType.Triangles, mesh!, lua.GetObject(2));
		return 0;
	}

	static void BuildFromTriangles(int vertsPerPrimitive, MaterialPrimitiveType type, IMesh mesh, ILuaObject table) {
		int count = 1;
		LuaObject vert = new();
		table.GetMember(count, vert);
		while (!vert.isNil()) {
			vert.UnReference();
			count++;
			vert = new();
			table.GetMember(count, vert);
		}
		vert.UnReference();
		count--;
		if (count < 3)
			return;

		int primitiveVerts = count / vertsPerPrimitive * vertsPerPrimitive;
		VertexFormat format = mesh.GetVertexFormat();
		int texCoordSize = format.GetTexCoordDimensionSize(0);
		int numBoneWeights = format.GetBoneWeightsSize();
		int userDataSize = format.GetUserDataSize();
		bool modify = mesh.IndexCount() > 0;

		MeshBuilder builder = new();
		if (!modify)
			builder.Begin(mesh, type, count / vertsPerPrimitive);
		else {
			if (mesh.VertexCount() < count) {
				g_Lua!.ErrorNoHalt("When modifying a mesh, you must match vertex count!");
				return;
			}
			builder.BeginModify(mesh, 0, -1, 0, 0);
		}

		if (count < primitiveVerts)
			primitiveVerts = count;

		for (int i = 1; i <= primitiveVerts; i++) {
			vert = new();
			table.GetMember(i, vert);
			if (vert.isNil()) {
				vert.UnReference();
				break;
			}

			if ((format & VertexFormat.Position) != 0)
				builder.Position3fv(vert.GetMemberVector("pos", vec3_origin));
			if ((format & VertexFormat.Normal) != 0)
				builder.Normal3fv(vert.GetMemberVector("normal", vec3_origin));
			if (texCoordSize > 1) {
				builder.TexCoord2f(0, vert.GetMemberFloat("u", 0), vert.GetMemberFloat("v", 0));
				builder.TexCoord2f(1, vert.GetMemberFloat("u1", 0), vert.GetMemberFloat("v1", 0));
			}
			if ((format & VertexFormat.Color) != 0) {
				LuaObject color = new();
				vert.GetMember("color", color);
				Color col = LuaRender.GetColor(color);
				builder.Color4ub(col.R, col.G, col.B, col.A);
				color.UnReference();
			}
			if ((format & VertexFormat.TangentT) != 0)
				builder.TangentT3fv(vert.GetMemberVector("tangent", vec3_origin));
			if ((format & VertexFormat.TangentS) != 0)
				builder.TangentS3fv(vert.GetMemberVector("binormal", vec3_origin));
			if (numBoneWeights != 0) {
				LuaObject weights = new();
				vert.GetMember("weights", weights);
				int weightCount = Math.Min(numBoneWeights, 3);
				for (int w = 0; w < weightCount; w++) {
					LuaObject weight = new();
					weights.GetMember(w + 1, weight);
					if (weight.isTable()) {
						int bone = weight.GetMemberInt("bone", 0);
						if ((uint)(bone - 1) > 51) {
							g_Lua!.ErrorNoHalt($"BuildFromTriangles: Too many model transform matrices! ({53} max)\n");
							weight.UnReference();
							break;
						}
						builder.BoneMatrix(w, bone);
						if (w < 2)
							builder.BoneWeight(w, weight.GetMemberFloat("weight", 1.0f));
					}
					weight.UnReference();
				}
				weights.UnReference();
			}
			if (userDataSize > 3) {
				Vector4 userData = default;
				LuaObject data = new();
				vert.GetMember("userdata", data);
				if (data.isTable()) {
					userData.X = (float)data.GetMemberDouble(1, 0);
					userData.Y = (float)data.GetMemberDouble(2, 0);
					userData.Z = (float)data.GetMemberDouble(3, 0);
					userData.W = (float)data.GetMemberDouble(4, 0);
				}
				data.UnReference();
				builder.UserData(userData);
			}
			vert.UnReference();
			builder.AdvanceVertex();
		}

		if (!modify)
			builder.End();
		else
			builder.EndModify();
	}

	[LuaMethod]
	static int IMesh__Destroy(ILuaInterface lua) {
		if (LC_IMesh.Get(1) == null) {
			lua.Error("Tried to use a NULL LuaMesh!");
			return 0;
		}
		QueueDestroy(lua);
		return 0;
	}

	[LuaMethod]
	static int IMesh__Draw(ILuaInterface lua) {
		IMesh? mesh = GetMesh(lua, 1);
		if (mesh == null)
			return 0;
		if (mesh.GetVertexFormat().GetBoneWeightsSize() != 0) {
			lua.Error("Trying to render skinned mesh without skinning!\n");
			return 0;
		}
		mesh.Draw();
		return 0;
	}

	static bool IsMatrixMember(ILuaObject table, int key) {
		LuaObject member = new();
		table.GetMember(key, member);
		bool isMatrix = member.GetType() == LuaType.Matrix;
		member.UnReference();
		return isMatrix;
	}

	[LuaMethod]
	static int IMesh__DrawSkinned(ILuaInterface lua) {
		LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(1);
		if (obj == null || obj.Mesh == null) {
			lua.Error("Tried to use a NULL IMesh!");
			return 0;
		}
		IMesh mesh = obj.Mesh;

		int numBoneWeights = mesh.GetVertexFormat().GetBoneWeightsSize();
		if (numBoneWeights == 0) {
			mesh.Draw();
			return 0;
		}

		ILuaObject table = lua.GetObject(2);
		if (!table.isTable()) {
			lua.TypeError("table", 2);
			return 0;
		}

		bool transform = lua.GetType(3) != LuaType.Nil && lua.GetBool(3);

		using MatRenderContextPtr renderContext = new(materials);
		Matrix4x4 model = Matrix4x4.Identity;
		renderContext.GetMatrix(MaterialMatrixMode.Model, out model);

		int bone = 1;
		while (IsMatrixMember(table, bone)) {
			Matrix4x4 bind = table.GetMemberMatrix(bone, default);
			if (bone > 52) {
				lua.ErrorNoHalt($"Too many model transform matrices! ({53} max)\n");
				break;
			}
			if (transform)
				MathLib.MatrixMultiply(model, bind, out bind);
			renderContext.LoadBoneMatrix(bone, new ReadOnlySpan<Matrix4x4>(in bind).Cast<Matrix4x4, Matrix3x4>()[0]);
			bone++;
		}

		Matrix4x4 fill = Matrix4x4.Identity;
		if (transform)
			MathLib.MatrixMultiply(fill, model, out fill);
		for (; bone < 53; bone++)
			renderContext.LoadBoneMatrix(bone, new ReadOnlySpan<Matrix4x4>(in fill).Cast<Matrix4x4, Matrix3x4>()[0]);

		int oldBones = renderContext.GetCurrentNumBones();
		renderContext.SetNumBoneWeights(numBoneWeights);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		mesh.Draw();
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
		renderContext.SetNumBoneWeights(oldBones);
		return 0;
	}

	[LuaMethod]
	static int IMesh__IsValid(ILuaInterface lua) {
		LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(1);
		lua.PushBool(obj != null && obj.Mesh != null);
		return 1;
	}

	[LuaMethod]
	static int IMesh____gc(ILuaInterface lua) {
		QueueDestroy(lua);
		return 0;
	}

	[LuaMethod]
	static int IMesh____tostring(ILuaInterface lua) {
		LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(1);
		if (obj == null || obj.Mesh == null) {
			lua.PushString("IMesh [NULL]");
			return 1;
		}
		lua.PushString($"IMesh [{obj.Mesh.VertexCount()} verts, {obj.Mesh.IndexCount()} indices]");
		return 1;
	}

	[LuaFunction]
	static int Begin(ILuaInterface lua) {
		if (CurrentMesh != null) {
			EndMesh();
			lua.Error("mesh.End must be called before calling mesh.Begin again");
			return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);
		int maxVertices = 32768;
		IMesh? mesh;
		bool isStatic;
		int type;
		int count;
		if (lua.GetType(1) == LuaType.IMesh) {
			LuaMeshObject? obj = (LuaMeshObject?)LC_IMesh.Get(1);
			if (obj == null || obj.Mesh == null)
				lua.Error("Tried to use a NULL IMesh!");
			mesh = obj?.Mesh;
			type = (int)lua.CheckNumber(2);
			count = (int)lua.CheckNumber(3);
			isStatic = true;
			maxVertices = 65535;
		}
		else {
			mesh = renderContext.GetDynamicMesh(true);
			type = (int)lua.CheckNumber(1);
			count = (int)lua.CheckNumber(2);
			isStatic = false;
		}

		if (count < 1) {
			lua.Error($"called mesh.Begin with a bad primitive type ({type}) or count ({count})");
			return 0;
		}

		VerticesAdded = 0;
		IsStatic = isStatic;
		switch ((MaterialPrimitiveType)type) {
			case MaterialPrimitiveType.Lines:
				MaxVertices = count * 2;
				break;
			case MaterialPrimitiveType.Triangles:
				MaxVertices = count * 3;
				break;
			case MaterialPrimitiveType.TriangleStrip:
				MaxVertices = count + 2;
				break;
			case MaterialPrimitiveType.LineStrip:
				MaxVertices = count + 1;
				break;
			case MaterialPrimitiveType.Polygon:
				if (count < 3)
					goto default;
				MaxVertices = count;
				break;
			case MaterialPrimitiveType.Points:
			case MaterialPrimitiveType.LineLoop:
				MaxVertices = count;
				break;
			case MaterialPrimitiveType.Quads:
				MaxVertices = count * 4;
				break;
			default:
				CurrentMesh = null;
				lua.Error($"called mesh.Begin with a bad primitive type ({type}) or count ({count})");
				return 0;
		}

		if (maxVertices < MaxVertices) {
			CurrentMesh = null;
			lua.Error($"called mesh.Begin with a primitive count which results in too many vertices ({MaxVertices}, maximum={maxVertices})");
			return 0;
		}

		CurrentMesh = mesh;
		Builder.Begin(mesh!, (MaterialPrimitiveType)type, count);
		return 0;
	}

	[LuaFunction]
	static int End(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.End if no mesh was started using mesh.Begin");
			return 0;
		}
		EndMesh();
		return 0;
	}

	[LuaFunction]
	static int AdvanceVertex(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.AdvanceVertex if no mesh was started using mesh.Begin");
			return 0;
		}
		if (CanWriteVertex()) {
			Builder.AdvanceVertex();
			VerticesAdded++;
		}
		return 0;
	}

	[LuaFunction]
	static int BoneData(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.BoneData if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;

		uint index = (uint)lua.CheckNumber(1);
		uint bone = (uint)lua.CheckNumber(2);
		if (index >= 3) {
			lua.Error($"Invalid bone matrix index. Must be integer [0,{index - 1}]\n");
			return 0;
		}
		if (bone >= 53) {
			lua.Error($"Invalid bone matrix bone. Must be integer [1,{52}]\n");
			return 0;
		}

		int numBoneWeights = CurrentMesh.GetVertexFormat().GetBoneWeightsSize();
		if (numBoneWeights <= (int)index) {
			lua.Error($"Invalid bone matrix index {index}. Mesh only has {numBoneWeights} weights!\n");
			return 0;
		}

		if (index == 2) {
			Builder.BoneMatrix((int)index, (int)bone);
			return 0;
		}

		float weight = (float)lua.CheckNumber(3);
		if (weight < 0.0f || weight > 1.0f) {
			lua.Error("Invalid bone matrix weight. Must be float [0,1]\n");
			return 0;
		}
		Builder.BoneMatrix((int)index, (int)bone);
		Builder.BoneWeight((int)index, weight);
		return 0;
	}

	[LuaFunction("Color")]
	static int MeshColor(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.Color if no mesh was started using mesh.Begin");
			return 0;
		}
		if (CanWriteVertex())
			Builder.Color4ub((byte)(int)lua.CheckNumber(1), (byte)(int)lua.CheckNumber(2), (byte)(int)lua.CheckNumber(3), (byte)(int)lua.CheckNumber(4));
		return 0;
	}

	[LuaFunction]
	static int Normal(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.Normal if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;
		if (lua.GetType(1) == LuaType.Number)
			Builder.Normal3f((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), (float)lua.CheckNumber(3));
		else
			Builder.Normal3fv(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int Position(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.Position if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;
		if (lua.GetType(1) == LuaType.Number)
			Builder.Position3f((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), (float)lua.CheckNumber(3));
		else
			Builder.Position3fv(LuaVector.Get_Vector(1));
		return 0;
	}

	static void QuadVertex(in Vector3 position, in Vector3 normal, float s, float t, in Color color) {
		if (!CanWriteVertex())
			return;
		Builder.Position3fv(position);
		Builder.Normal3fv(normal);
		Builder.TexCoord2f(0, s, t);
		Builder.Color4ub(color.R, color.G, color.B, color.A);
		Builder.AdvanceVertex();
		VerticesAdded++;
	}

	[LuaFunction]
	static int Quad(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.Quad if no mesh was started using mesh.Begin");
			return 0;
		}
		Vector3 p1 = LuaVector.Get_Vector(1);
		Vector3 p2 = LuaVector.Get_Vector(2);
		Vector3 p3 = LuaVector.Get_Vector(3);
		Vector3 p4 = LuaVector.Get_Vector(4);
		Color color = LuaRender.GetColor(lua.GetObject(5));

		Vector3 normal = Vector3.Cross(p1 - p2, p1 - p3);
		MathLib.VectorNormalize(ref normal);

		QuadVertex(p1, normal, 1, 1, color);
		QuadVertex(p2, normal, 0, 1, color);
		QuadVertex(p3, normal, 0, 0, color);
		QuadVertex(p4, normal, 1, 0, color);
		return 0;
	}

	[LuaFunction]
	static int QuadEasy(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.QuadEasy if no mesh was started using mesh.Begin");
			return 0;
		}
		Vector3 position = LuaVector.Get_Vector(1);
		Vector3 normal = LuaVector.Get_Vector(2);
		float width = (float)lua.CheckNumber(3);
		float height = (float)lua.CheckNumber(4);
		Color color = LuaRender.GetColor(lua.GetObject(5));
		float rotation = 0;
		if (lua.GetType(6) == LuaType.Number)
			rotation = (float)lua.CheckNumber(6);

		MathLib.VectorVectors(normal, out Vector3 right, out Vector3 up);

		float widthAngle = MathLib.DEG2RAD(rotation);
		float heightAngle = MathLib.DEG2RAD(rotation + 90.0f);
		Vector3 halfWidth = (right * MathF.Cos(widthAngle) - up * MathF.Sin(widthAngle)) * width * 0.5f;
		Vector3 halfHeight = (right * MathF.Cos(heightAngle) - up * MathF.Sin(heightAngle)) * height * 0.5f;

		QuadVertex(position + halfWidth - halfHeight, normal, 1, 1, color);
		QuadVertex(position - halfWidth - halfHeight, normal, 0, 1, color);
		QuadVertex(position - halfWidth + halfHeight, normal, 0, 0, color);
		QuadVertex(position + halfWidth + halfHeight, normal, 1, 0, color);
		return 0;
	}

	[LuaFunction]
	static int Specular(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.Specular if no mesh was started using mesh.Begin");
			return 0;
		}
		if (CanWriteVertex())
			Builder.Specular4ub((byte)(int)lua.CheckNumber(1), (byte)(int)lua.CheckNumber(2), (byte)(int)lua.CheckNumber(3), (byte)(int)lua.CheckNumber(4));
		return 0;
	}

	[LuaFunction]
	static int TangentS(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.TangentS if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;
		if (lua.GetType(1) == LuaType.Number)
			Builder.TangentS3f((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), (float)lua.CheckNumber(3));
		else
			Builder.TangentS3fv(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int TangentT(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.TangentT if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;
		if (lua.GetType(1) == LuaType.Number)
			Builder.TangentT3f((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), (float)lua.CheckNumber(3));
		else
			Builder.TangentT3fv(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int TexCoord(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.TexCoord if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;

		uint stage = (uint)lua.CheckNumber(1);
		if (stage >= 8)
			return 0;

		int args = lua.Top();
		if (args == 4)
			Builder.TexCoord3f((int)stage, (float)lua.CheckNumber(2), (float)lua.CheckNumber(3), (float)lua.CheckNumber(4));
		else if (args == 3)
			Builder.TexCoord2f((int)stage, (float)lua.CheckNumber(2), (float)lua.CheckNumber(3));
		else if (args < 3)
			Builder.TexCoord1f((int)stage, (float)lua.CheckNumber(2));
		else
			Builder.TexCoord4f((int)stage, (float)lua.CheckNumber(2), (float)lua.CheckNumber(3), (float)lua.CheckNumber(4), (float)lua.CheckNumber(5));
		return 0;
	}

	[LuaFunction]
	static int UserData(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.UserData if no mesh was started using mesh.Begin");
			return 0;
		}
		if (!CanWriteVertex())
			return 0;
		Vector4 data = new((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), (float)lua.CheckNumber(3), (float)lua.CheckNumber(4));
		if (CurrentMesh.GetVertexFormat().GetUserDataSize() > 3)
			Builder.UserData(data);
		return 0;
	}

	[LuaFunction]
	static int VertexCount(ILuaInterface lua) {
		if (CurrentMesh == null) {
			lua.Error("cannot call mesh.VertexCount if no mesh was started using mesh.Begin");
			return 0;
		}
		lua.PushNumber(Builder.VertexCount());
		return 1;
	}
}
