using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

namespace Game.Client.HL2;

[NetworkName("CPropCombineBall")]
public partial class C_PropCombineBall : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Emit),
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Held),
		nameof(Launched),
	];

	public Vector3 LastOrigin;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_flRadius")]
	public float Radius;
	[NetworkVar("m_bHeld")]
	public bool Held;
	[NetworkVar("m_bLaunched")]
	public bool Launched;

	IMaterial? FlickerMaterial;
	IMaterial? BodyMaterial;
	IMaterial? BlurMaterial;

	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created) {
			LastOrigin = GetAbsOrigin();
			InitMaterials();
		}
	}

	public override RenderGroup GetRenderGroup() {
		return RenderGroup.TranslucentEntity;
	}

	bool InitMaterials() {
		if (BlurMaterial == null) {
			BlurMaterial = materials.FindMaterial("effects/ar2_altfire1b", null, false);

			if (BlurMaterial == null)
				return false;
		}

		// Main body of the ball
		if (BodyMaterial == null) {
			BodyMaterial = materials.FindMaterial("effects/ar2_altfire1", null, false);

			if (BodyMaterial == null)
				return false;
		}

		// Flicker material
		if (FlickerMaterial == null) {
			FlickerMaterial = materials.FindMaterial("effects/combinemuzzle1", null, false);

			if (FlickerMaterial == null)
				return false;
		}

		return true;
	}

	void DrawFlicker() {
		float rand1 = random.RandomFloat(0.2f, 0.3f);
		float rand2 = random.RandomFloat(1.5f, 2.5f);

		if (gpGlobals.FrameTime == 0.0f) {
			rand1 = 0.2f;
			rand2 = 1.5f;
		}

		Span<float> color = stackalloc float[3];
		color[0] = color[1] = color[2] = rand1;

		// Draw the flickering glow
		using MatRenderContextPtr renderContext = new(materials );
		renderContext.Bind(FlickerMaterial!);
		DrawHalo(FlickerMaterial, GetAbsOrigin(), Radius * rand2, color);
	}

	void DrawHaloOriented(in Vector3 source, float scale, ReadOnlySpan<float> color, float roll) {
		Vector3 point, screen;

		using MatRenderContextPtr renderContext = new(materials );
		IMesh mesh = renderContext.GetDynamicMesh();

		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, 1);

		// Transform source into screen space
		ScreenTransform(source, out screen);

		Vector3 right = default, up = default;
		float sr, cr;

		MathLib.SinCos(roll, out sr, out cr);

		for (int i = 0; i < 3; i++) {
			right[i] = CurrentViewRight()[i] * cr + CurrentViewUp()[i] * sr;
			up[i] = CurrentViewRight()[i] * -sr + CurrentViewUp()[i] * cr;
		}

		meshBuilder.Color3fv(color);
		meshBuilder.TexCoord2f(0, 0, 1);
		MathLib.VectorMA(source, -scale, up, out point);
		MathLib.VectorMA(point, -scale, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color3fv(color);
		meshBuilder.TexCoord2f(0, 0, 0);
		MathLib.VectorMA(source, scale, up, out point);
		MathLib.VectorMA(point, -scale, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color3fv(color);
		meshBuilder.TexCoord2f(0, 1, 0);
		MathLib.VectorMA(source, scale, up, out point);
		MathLib.VectorMA(point, scale, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.Color3fv(color);
		meshBuilder.TexCoord2f(0, 1, 1);
		MathLib.VectorMA(source, -scale, up, out point);
		MathLib.VectorMA(point, scale, right, out point);
		meshBuilder.Position3fv(point);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		mesh.Draw();
	}

	void DrawMotionBlur() {

	}

	public override int DrawModel(StudioFlags flags) {
		if (!Emit)
			return 0;

		// Make sure our materials are cached
		if (!InitMaterials()) {
			//NOTENOTE: This means that a material was not found for the combine ball, so it may not render!
			Assert(false);
			return 0;
		}

		// Draw the flickering overlay
		DrawFlicker();

		// Draw the motion blur from movement
		if (Held || Launched) 
			DrawMotionBlur();

		// Draw the model if we're being held
		if (Held) {
			QAngle angles;
			MathLib.VectorAngles(-CurrentViewForward(), out angles);

			// Always orient towards the camera!
			SetAbsAngles(angles);

			base.DrawModel(flags);
		}
		else {
			Span<float> color = stackalloc float[3];
			color[0] = color[1] = color[2] = 1.0f;

			TimeUnit_t sinOffs = 1.0 * Math.Sin(gpGlobals.CurTime * 25);

			TimeUnit_t roll = SpawnTime() % 360;

			// Draw the main ball body
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.Bind(BodyMaterial!, this);
			DrawHaloOriented(GetAbsOrigin(), (float)(Radius + sinOffs), color, (float)roll);
		}

		LastOrigin = GetAbsOrigin();

		return 1;
	}
}
