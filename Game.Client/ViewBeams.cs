global using static Game.Client.ViewRenderBeams_Exposed;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System;
using System.Numerics;
namespace Game.Client;

public class ViewRenderBeams : IViewRenderBeams, IDisposable
{
	static readonly UniformRandomStream beamRandom = new();

	const int BEAM_FREELIST_MAX = 32;
	const int DEFAULT_PARTICLES = 2048;
	const int MIN_PARTICLES = 512;

	public ViewRenderBeams() {
		FreeBeams = null;
		ActiveBeams = null;
		BeamFreeListLength = 0;
	}

	public void Dispose() {
		ClearBeams();
	}

	Beam? ActiveBeams;
	Beam? FreeBeams;
	int BeamFreeListLength;

	BeamTrail?[]? BeamTrails;
	BeamTrail? ActiveTrails;
	BeamTrail? FreeTrails;
	int NumBeamTrails;

	public void ClearBeams() {
		Beam? next = null;
		for (; ActiveBeams != null; ActiveBeams = next) {
			next = ActiveBeams.Next;
			ActiveBeams.Dispose();
		}

		for (; FreeBeams != null; FreeBeams = next) {
			next = FreeBeams.Next;
			FreeBeams.Dispose();
		}

		BeamFreeListLength = 0;

		if (NumBeamTrails != 0) {
			// Also clear any particles used by beams
			FreeTrails = BeamTrails![0];
			ActiveTrails = null;

			for (int i = 0; i < NumBeamTrails; i++)
				BeamTrails![i].Next = BeamTrails[i + 1];

			BeamTrails![NumBeamTrails - 1].Next = null;
		}
	}

	public Beam? CreateBeamCirclePoints(ref BeamInfo beamInfo) {
		throw new NotImplementedException();
	}

	public void CreateBeamCirclePoints(int type, ref Vector3 start, ref Vector3 end, int modelIndex, int haloIndex, float haloScale, float life, float width, float endWidth, float fadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b) {
		throw new NotImplementedException();
	}

	public Beam? CreateBeamEntPoint(ref BeamInfo beamInfo) {
		if (beamInfo.Life != 0) {
			if (beamInfo.StartEnt != null && beamInfo.StartEnt.GetModel() == null)
				return null;

			if (beamInfo.EndEnt != null && beamInfo.EndEnt.GetModel() == null)
				return null;
		}

		// Model index.
		if (!beamInfo.ModelName.IsEmpty && beamInfo.ModelIndex == -1) {
			beamInfo.ModelIndex = modelinfo.GetModelIndex(beamInfo.ModelName);
		}

		if (!beamInfo.HaloName.IsEmpty && beamInfo.HaloIndex == -1) {
			beamInfo.HaloIndex = modelinfo.GetModelIndex(beamInfo.HaloName);
		}

		Beam? pBeam = CreateGenericBeam(ref beamInfo);
		if (pBeam == null)
			return null;

		pBeam.Type = TempEntType.BeamPoints;
		pBeam.Flags = 0;

		if (beamInfo.StartEnt != null) {
			pBeam.Flags |= BeamFlags.StartEntity;
			pBeam.Entity[0].Set(beamInfo.StartEnt);
			pBeam.AttachmentIndex[0] = beamInfo.StartAttachment;
			beamInfo.Start = vec3_origin;
		}
		if (beamInfo.EndEnt != null) {
			pBeam.Flags |= BeamFlags.EndEntity;
			pBeam.Entity[1].Set(beamInfo.EndEnt);
			pBeam.AttachmentIndex[1] = beamInfo.EndAttachment;
			beamInfo.End = vec3_origin;
		}

		SetBeamAttributes(pBeam, ref beamInfo);
		if (beamInfo.Life == 0) {
			pBeam.Flags |= BeamFlags.Forever;
		}

		UpdateBeam(pBeam, 0);
		return pBeam;
	}

	static void Noise(Span<float> noise, int divs, float scale) {
		int div2;

		div2 = divs >> 1;

		if (divs < 2)
			return;

		// Noise is normalized to +/- scale
		noise[div2] = (noise[0] + noise[divs]) * 0.5f + scale * beamRandom.RandomFloat(-1, 1);
		if (div2 > 1) {
			Noise(noise[div2..], div2, scale * 0.5f);
			Noise(noise, div2, scale * 0.5f);
		}
	}

	static void SineNoise(Span<float> noise, int divs) {
		int i;
		float freq;
		float step = MathF.PI / (float)divs;

		freq = 0;
		for (i = 0; i < divs; i++) {
			noise[i] = MathF.Sin(freq);
			freq += step;
		}
	}

	private void UpdateBeam(Beam pbeam, TimeUnit_t frametime) {
		if (pbeam.ModelIndex < 0) {
			pbeam.Die = gpGlobals.CurTime;
			return;
		}

		// if we are paused, force random numbers used by noise to generate the same value every frame
		if (frametime == 0.0f) {
			beamRandom.SetSeed((int)gpGlobals.CurTime);
		}

		// If FBEAM_ONLYNOISEONCE is set, we don't want to move once we've first calculated noise
		if ((pbeam.Flags & BeamFlags.OnlyNoiseOnce) == 0)
			pbeam.Freq += frametime;
		else
			pbeam.Freq += frametime * beamRandom.RandomFloat(1, 2);

		// OPTIMIZE: Do this every frame?
		// UNDONE: Do this differentially somehow?
		// Generate fractal noise
		pbeam.Noise[0] = 0;
		pbeam.Noise[Beam.NOISE_DIVISIONS] = 0;
		if (pbeam.Amplitude != 0) {
			if ((pbeam.Flags & BeamFlags.OnlyNoiseOnce) == 0 || pbeam.CalculatedNoise) {
				if ((pbeam.Flags & BeamFlags.SineNoise) != 0)
					SineNoise(pbeam.Noise, Beam.NOISE_DIVISIONS);
				else
					Noise(pbeam.Noise, Beam.NOISE_DIVISIONS, 1.0f);

				pbeam.CalculatedNoise = true;
			}
		}

		// update end points
		if ((pbeam.Flags & (BeamFlags.StartEntity | BeamFlags.EndEntity)) != 0) {
			// Makes sure attachment[0] + attachment[1] are valid
			if (!RecomputeBeamEndpoints(pbeam))
				return;

			// Compute segments from the new endpoints
			MathLib.VectorSubtract(pbeam.Attachment[1], pbeam.Attachment[0], out pbeam.Delta);
			if (pbeam.Amplitude >= 0.50f)
				pbeam.Segments = (int)(MathLib.VectorLength(pbeam.Delta) * 0.25f + 3); // one per 4 pixels
			else
				pbeam.Segments = (int)(MathLib.VectorLength(pbeam.Delta) * 0.075f + 3); // one per 16 pixels
		}

		// Get position data for spline beam
		switch (pbeam.Type) {
			case TempEntType.BeamSpline: {
					// Why isn't attachment[0] being computed?
					for (int i = 1; i < pbeam.NumAttachments; i++) {
						if (!ComputeBeamEntPosition(pbeam.Entity[i].Get(), pbeam.AttachmentIndex[i], (pbeam.Flags & BeamFlags.UseHitboxes) != 0, out pbeam.Attachment[i])) {
							// This should never happen, but if for some reason the attachment doesn't exist, 
							// as a safety measure copy in the location of the previous attachment point (rather than bailing)
							pbeam.Attachment[i] = pbeam.Attachment[i - 1];
						}
					}
				}
				break;

			case TempEntType.BeamRingPoint: {
					float dr = pbeam.EndRadius - pbeam.StartRadius;
					if (dr != 0.0f) {
						TimeUnit_t frac = 1.0;
						// Go some portion of the way there based on life
						TimeUnit_t remaining = pbeam.Die - gpGlobals.CurTime;
						if (remaining < pbeam.Life && pbeam.Life > 0.0f) {
							frac = remaining / pbeam.Life;
						}
						frac = Math.Min(1.0, frac);
						frac = Math.Max(0.0, frac);

						frac = 1.0f - frac;

						// Start pos
						Vector3 endpos = pbeam.Attachment[2];
						endpos.X += (float)((pbeam.StartRadius + frac * dr) / 2.0);
						Vector3 startpos = pbeam.Attachment[2];
						startpos.X -= (float)((pbeam.StartRadius + frac * dr) / 2.0);

						pbeam.Attachment[0] = startpos;
						pbeam.Attachment[1] = endpos;

						MathLib.VectorSubtract(pbeam.Attachment[1], pbeam.Attachment[0], out pbeam.Delta);
						if (pbeam.Amplitude >= 0.50)
							pbeam.Segments = (int)(MathLib.VectorLength(pbeam.Delta) * 0.25f + 3); // one per 4 pixels
						else
							pbeam.Segments = (int)(MathLib.VectorLength(pbeam.Delta) * 0.075f + 3); // one per 16 pixels

					}
				}
				break;

			case TempEntType.BeamPoints:
				// UNDONE: Build culling volumes for other types of beams
				if (!CullBeam(in pbeam.Attachment[0], in pbeam.Attachment[1], false))
					return;
				break;
		}

		// update life cycle
		pbeam.T = pbeam.Freq + (pbeam.Die - gpGlobals.CurTime);
		if (pbeam.T != 0)
			pbeam.T = pbeam.Freq / pbeam.T;
		else
			pbeam.T = 1.0;

		// ------------------------------------------
		// check for zero fadeLength (means no fade)
		// ------------------------------------------
		if (pbeam.FadeLength == 0) {
			Assert(pbeam.Delta.IsValid());
			pbeam.FadeLength = pbeam.Delta.Length();
		}
	}

	private bool CullBeam(in Vector3 start, in Vector3 end, bool pvsOnly) {
		Vector3 mins = default, maxs = default;
		int i;

		for (i = 0; i < 3; i++) {
			if (start[i] < end[i]) {
				mins[i] = start[i];
				maxs[i] = end[i];
			}
			else {
				mins[i] = end[i];
				maxs[i] = start[i];
			}

			// Don't let it be zero sized
			if (mins[i] == maxs[i]) {
				maxs[i] += 1;
			}
		}

		// Check bbox
		if (engine.IsBoxVisible(in mins, in maxs)) {
			if (pvsOnly || !engine.CullBox(ref mins, ref maxs)) {
				// Beam is visible
				return true;
			}
		}

		// Beam is not visible
		return false;
	}

	static bool ComputeBeamEntPosition(C_BaseEntity? ent, int nAttachment, bool bInterpretAttachmentIndexAsHitboxIndex, out Vector3 pt) {
		if (ent == null) {
			pt = default;
			return false;
		}

		if (!bInterpretAttachmentIndexAsHitboxIndex) {
			if (ent.GetAttachment(nAttachment, out pt, out _))
				return true;
		}
		else {
			// todo
			throw new NotImplementedException();
		}

		// Player origins are at their feet
		if (ent.IsPlayer())
			pt = ent.WorldSpaceCenter();
		else
			pt = ent.GetRenderOrigin();

		return true;
	}

	private bool RecomputeBeamEndpoints(Beam pbeam) {
		if ((pbeam.Flags & BeamFlags.StartEntity) != 0) {
			if (ComputeBeamEntPosition(pbeam.Entity[0].Get(), pbeam.AttachmentIndex[0], (pbeam.Flags & BeamFlags.UseHitboxes) != 0, out pbeam.Attachment[0]))
				pbeam.Flags |= BeamFlags.StartVisible;
			else if ((pbeam.Flags & BeamFlags.Forever) == 0)
				pbeam.Flags &= ~(BeamFlags.StartEntity);

			// If we've never seen the start entity, don't display
			if ((pbeam.Flags & BeamFlags.StartVisible) == 0)
				return false;
		}

		if ((pbeam.Flags & BeamFlags.EndEntity) != 0) {
			if (ComputeBeamEntPosition(pbeam.Entity[1].Get(), pbeam.AttachmentIndex[1], (pbeam.Flags & BeamFlags.UseHitboxes) != 0, out pbeam.Attachment[1]))
				pbeam.Flags |= BeamFlags.EndVisible;
			else if ((pbeam.Flags & BeamFlags.Forever) == 0) {
				pbeam.Flags &= ~(BeamFlags.EndEntity);
				pbeam.Die = gpGlobals.CurTime;
				return false;
			}
			else
				return false;

			// If we've never seen the end entity, don't display
			if ((pbeam.Flags & BeamFlags.EndVisible) == 0)
				return false;
		}

		return true;
	}

	private Beam? CreateGenericBeam(ref BeamInfo beamInfo) {
		Beam? pBeam = BeamAlloc(beamInfo.Renderable);
		if (pBeam == null)
			return null;

		// In case we fail.
		pBeam.Die = gpGlobals.CurTime;

		// Need a valid model.
		if (beamInfo.ModelIndex < 0)
			return null;

		// Set it up
		SetupBeam(pBeam, ref beamInfo);

		return pBeam;
	}

	private Beam? BeamAlloc(bool renderable) {
		Beam? beam = null;
		if (FreeBeams != null) {
			beam = FreeBeams;
			FreeBeams = FreeBeams.Next;
			BeamFreeListLength--;
		}
		else {
			beam = new Beam();
		}

		beam.Next = ActiveBeams;
		ActiveBeams = beam;

		if (renderable)
			clientLeafSystem.AddRenderable(beam, RenderGroup.OpaqueEntity); // TODO: MOVE TO TRANSLUCENT!!!!!! VERY IMPORTANT FIXME
		else
			beam.m_RenderHandle = INVALID_CLIENT_RENDER_HANDLE;

		return beam;
	}

	private void BeamFree(Beam? beam) {
		FreeDeadTrails(ref beam!.Trail);
		clientLeafSystem.RemoveRenderable(beam.m_RenderHandle);
		beam.Reset();

		if (BeamFreeListLength < BEAM_FREELIST_MAX) {
			BeamFreeListLength++;

			// Now link into free list;
			beam.Next = FreeBeams;
			FreeBeams = beam;
		}
		else {
			beam.Dispose();
		}
	}

	private void FreeDeadTrails(ref BeamTrail? trail) {
		BeamTrail? kill;
		BeamTrail? p;

		// kill all the ones hanging direcly off the base pointer
		for (; ; )
		{
			kill = trail;
			if (kill != null && kill.Die < gpGlobals.CurTime) {
				trail = kill.Next;
				kill.Next = FreeTrails;
				FreeTrails = kill;
				continue;
			}
			break;
		}

		// kill off all the others
		for (p = trail; p != null; p = p.Next) {
			for (; ; )
			{
				kill = p.Next;
				if (kill != null && kill.Die < gpGlobals.CurTime) {
					p.Next = kill.Next;
					kill.Next = FreeTrails;
					FreeTrails = kill;
					continue;
				}
				break;
			}
		}
	}

	private void SetupBeam(Beam pBeam, ref BeamInfo beamInfo) {
		Model? pSprite = modelinfo.GetModel(beamInfo.ModelIndex);
		if (pSprite == null)
			return;

		pBeam.Type = (beamInfo.Type < 0) ? TempEntType.BeamPoints : beamInfo.Type;
		pBeam.ModelIndex = beamInfo.ModelIndex;
		pBeam.HaloIndex = beamInfo.HaloIndex;
		pBeam.HaloScale = beamInfo.HaloScale;
		pBeam.Frame = 0;
		pBeam.FrameRate = 0;
		pBeam.FrameCount = modelinfo.GetModelFrameCount(pSprite);
		pBeam.Freq = gpGlobals.CurTime * beamInfo.Speed;
		pBeam.Die = gpGlobals.CurTime + beamInfo.Life;
		pBeam.Width = beamInfo.Width;
		pBeam.EndWidth = beamInfo.EndWidth;
		pBeam.FadeLength = beamInfo.FadeLength;
		pBeam.Amplitude = beamInfo.Amplitude;
		pBeam.Brightness = beamInfo.Brightness;
		pBeam.Speed = beamInfo.Speed;
		pBeam.Life = beamInfo.Life;
		pBeam.Flags = 0;

		pBeam.Attachment[0] = beamInfo.Start;
		pBeam.Attachment[1] = beamInfo.End;
		MathLib.VectorSubtract(beamInfo.End, beamInfo.Start, out pBeam.Delta);
		Assert(pBeam.Delta.IsValid());

		if (beamInfo.Segments == -1) {
			if (pBeam.Amplitude >= 0.50f)
				pBeam.Segments = (int)(MathLib.VectorLength(in pBeam.Delta) * 0.25f + 3); // one per 4 pixels
			else
				pBeam.Segments = (int)(MathLib.VectorLength(in pBeam.Delta) * 0.075f + 3); // one per 16 pixels
		}
		else
			pBeam.Segments = beamInfo.Segments;
	}

	public void CreateBeamEntPoint(int startEntity, in Vector3 start, int endEntity, in Vector3 end, int modelIndex, int haloIndex, float haloScale, float life, float width, float endWidth, float fadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b) {
		BeamInfo beamInfo = new();

		if (startEntity <= 0) {
			beamInfo.Start = start;
			beamInfo.StartEnt = null;
		}
		else {
			beamInfo.StartEnt = cl_entitylist.GetEnt(BeamEnt.Entity(startEntity));
			beamInfo.StartAttachment = BeamEnt.Attachment(startEntity);

			if (beamInfo.StartEnt == null)
				return;

			beamInfo.Start = beamInfo.StartEnt.GetRenderOrigin();
		}

		if (endEntity <= 0) {
			beamInfo.End = end;
			beamInfo.EndEnt = null;
		}
		else {
			beamInfo.EndEnt = cl_entitylist.GetEnt(BeamEnt.Entity(endEntity));
			beamInfo.EndAttachment = BeamEnt.Attachment(endEntity);

			if (beamInfo.EndEnt == null)
				return;

			beamInfo.End = beamInfo.EndEnt.GetRenderOrigin();
		}

		beamInfo.ModelIndex = modelIndex;
		beamInfo.HaloIndex = haloIndex;
		beamInfo.HaloScale = haloScale;
		beamInfo.Life = life;
		beamInfo.Width = width;
		beamInfo.EndWidth = endWidth;
		beamInfo.FadeLength = fadeLength;
		beamInfo.Amplitude = amplitude;
		beamInfo.Brightness = brightness;
		beamInfo.Speed = speed;
		beamInfo.StartFrame = startFrame;
		beamInfo.FrameRate = framerate;
		beamInfo.Red = r;
		beamInfo.Green = g;
		beamInfo.Blue = b;

		CreateBeamEntPoint(ref beamInfo);
	}

	public Beam? CreateBeamEnts(ref BeamInfo beamInfo) {
		if (beamInfo.Life != 0 &&
			(beamInfo.StartEnt == null || beamInfo.StartEnt.GetModel() == null ||
			 beamInfo.EndEnt == null || beamInfo.EndEnt.GetModel() == null)) {
			return null;
		}

		beamInfo.Start = vec3_origin;
		beamInfo.End = vec3_origin;

		Beam? pBeam = CreateGenericBeam(ref beamInfo);
		if (pBeam == null)
			return null;

		pBeam.Type = beamInfo.Type;
		pBeam.Flags = BeamFlags.StartEntity | BeamFlags.EndEntity;

		pBeam.Entity[0].Set(beamInfo.StartEnt);
		pBeam.AttachmentIndex[0] = beamInfo.StartAttachment;
		pBeam.Entity[1].Set(beamInfo.EndEnt);
		pBeam.AttachmentIndex[1] = beamInfo.EndAttachment;

		SetBeamAttributes(pBeam, ref beamInfo);
		if (beamInfo.Life == 0) {
			pBeam.Flags |= BeamFlags.Forever;
		}

		UpdateBeam(pBeam, 0);

		return pBeam;
	}

	public void CreateBeamEnts(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life, float width, float m_nEndWidth, float m_nFadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b, int type = -1) {
		BeamInfo beamInfo = new() {
			Type = type < 0 ? TempEntType.BeamPoints : (TempEntType)type,
			StartEnt = cl_entitylist.GetEnt(BeamEnt.Entity(startEnt)),
			StartAttachment = BeamEnt.Attachment(startEnt),
			EndEnt = cl_entitylist.GetEnt(BeamEnt.Entity(endEnt)),
			EndAttachment = BeamEnt.Attachment(endEnt),
			ModelIndex = modelIndex,
			HaloIndex = haloIndex,
			HaloScale = haloScale,
			Life = life,
			Width = width,
			EndWidth = m_nEndWidth,
			FadeLength = m_nFadeLength,
			Amplitude = amplitude,
			Brightness = brightness,
			Speed = speed,
			StartFrame = startFrame,
			FrameRate = framerate,
			Red = r,
			Green = g,
			Blue = b
		};

		CreateBeamEnts(ref beamInfo);
	}

	public Beam? CreateBeamFollow(ref BeamInfo beamInfo) {
		throw new NotImplementedException();
	}

	public void CreateBeamFollow(int startEnt, int modelIndex, int haloIndex, float haloScale, float life, float width, float endWidth, float fadeLength, float r, float g, float b, float brightness) {
		throw new NotImplementedException();
	}

	public Beam? CreateBeamPoints(ref BeamInfo beamInfo) {
		if (beamInfo.Life != 0 && !CullBeam(in beamInfo.Start, in beamInfo.End, true))
			return null;

		if (!beamInfo.ModelName.IsEmpty && beamInfo.ModelIndex == -1)
			beamInfo.ModelIndex = modelinfo.GetModelIndex(beamInfo.ModelName);

		if (!beamInfo.HaloName.IsEmpty && beamInfo.HaloIndex == -1)
			beamInfo.HaloIndex = modelinfo.GetModelIndex(beamInfo.HaloName);

		Beam? beam = CreateGenericBeam(ref beamInfo);
		if (beam == null)
			return null;

		SetBeamAttributes(beam, ref beamInfo);
		if (beamInfo.Life == 0)
			beam.Flags |= BeamFlags.Forever;

		return beam;
	}

	public void CreateBeamPoints(ref Vector3 start, ref Vector3 end, int modelIndex, int haloIndex, float haloScale, float life, float width, float endWidth, float fadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b) {
		BeamInfo beamInfo = new() {
			Start = start,
			End = end,
			ModelIndex = modelIndex,
			HaloIndex = haloIndex,
			HaloScale = haloScale,
			Life = life,
			Width = width,
			EndWidth = endWidth,
			FadeLength = fadeLength,
			Amplitude = amplitude,
			Brightness = brightness,
			Speed = speed,
			StartFrame = startFrame,
			FrameRate = framerate,
			Red = r,
			Green = g,
			Blue = b
		};
		CreateBeamPoints(ref beamInfo);
	}

	public Beam? CreateBeamRing(ref BeamInfo beamInfo) {
		throw new NotImplementedException();
	}

	public void CreateBeamRing(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life, float width, float endWidth, float fadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b, int flags = 0) {
		throw new NotImplementedException();
	}

	public Beam? CreateBeamRingPoint(ref BeamInfo beamInfo) {
		throw new NotImplementedException();
	}

	public void CreateBeamRingPoint(in Vector3 center, float startRadius, float endRadius, int modelIndex, int haloIndex, float haloScale, float life, float width, float m_nEndWidth, float fadeLength, float amplitude, float brightness, float speed, int startFrame, float framerate, float r, float g, float b, int flags = 0) {
		throw new NotImplementedException();
	}

	public void DrawBeam(C_Beam beam, ITraceFilter? entityBeamTraceFilter = null) {
		throw new NotImplementedException();
	}
	static readonly ConVar r_DrawBeams = new("r_DrawBeams", "1", FCvar.Cheat, "0=Off, 1=Normal, 2=Wireframe");


	public void DrawBeam(Beam beam) {
		if (r_DrawBeams.GetInt() == 0)
			return;

		// Don't draw really short beams
		if (beam.Delta.Length() < 0.1f)
			return;

		Model? sprite;
		Model? halosprite = null;

		if (beam.ModelIndex < 0) {
			beam.Die = gpGlobals.CurTime;
			return;
		}

		sprite = modelinfo.GetModel(beam.ModelIndex);
		if (sprite == null)
			return;

		if (modelinfo.GetModelSpriteHeight(sprite) == 0) // GetModelSpriteHeight has a check for sprite. If the modelindex now changed, we would try to use the wrong model. Which is not good.
		{
			DevMsg("Model is not a sprite!\n");
			return;
		}

		halosprite = modelinfo.GetModel(beam.HaloIndex);

		int frame = ((int)(float)(beam.Frame + gpGlobals.CurTime * beam.FrameRate) % beam.FrameCount);
		RenderMode rendermode = (beam.Flags & BeamFlags.Solid) != 0 ? RenderMode.Normal : RenderMode.TransAdd;

		// set color
		Span<float> srcColor = stackalloc float[4];
		Span<float> color = stackalloc float[4];

		srcColor[0] = beam.R;
		srcColor[1] = beam.G;
		srcColor[2] = beam.B;
		if ((beam.Flags & BeamFlags.FadeIn) != 0)
			MathLib.VectorScale(srcColor, (float)beam.T, color);
		else if ((beam.Flags & BeamFlags.FadeOut) != 0)
			MathLib.VectorScale(srcColor, (1.0f - (float)beam.T), color);
		else
			srcColor.CopyTo(color);

		MathLib.VectorScale(color, (1 / 255.0f), color);
		color.CopyTo(srcColor);
		MathLib.VectorScale(color, ((float)beam.Brightness / 255.0f), color);
		color[3] = 1f;

		switch (beam.Type) {
			case TempEntType.BeamDisk:
				DrawDisk(Beam.NOISE_DIVISIONS, beam.Noise, sprite, frame, rendermode,
					beam.Attachment[0], beam.Delta, beam.Width, beam.Amplitude,
					beam.Freq, beam.Speed, beam.Segments, color, beam.HDRColorScale);
				break;

			case TempEntType.BeamCylinder:
				DrawCylinder(Beam.NOISE_DIVISIONS, beam.Noise, sprite, frame, rendermode,
					beam.Attachment[0], beam.Delta, beam.Width, beam.Amplitude,
					beam.Freq, beam.Speed, beam.Segments, color, beam.HDRColorScale);
				break;

			case TempEntType.BeamPoints:
				if (halosprite != null) {
					DrawBeamWithHalo(beam, frame, rendermode, color, srcColor, sprite, halosprite, beam.HDRColorScale);
				}
				else {
					DrawSegs(Beam.NOISE_DIVISIONS, beam.Noise, sprite, frame, rendermode,
						beam.Attachment[0], beam.Delta, beam.Width, beam.EndWidth,
						beam.Amplitude, beam.Freq, beam.Speed, beam.Segments,
						beam.Flags, color, beam.FadeLength, beam.HDRColorScale);
				}
				break;

			case TempEntType.BeamFollow:
				DrawBeamFollow(sprite, beam, frame, rendermode, gpGlobals.FrameTime, color, beam.HDRColorScale);
				break;

			case TempEntType.BeamRing:
			case TempEntType.BeamRingPoint:
				DrawRing(Beam.NOISE_DIVISIONS, beam.Noise, Noise, sprite, frame, rendermode,
					beam.Attachment[0], beam.Delta, beam.Width, beam.Amplitude,
					beam.Freq, beam.Speed, beam.Segments, color, beam.HDRColorScale);
				break;

			case TempEntType.BeamSpline:
				DrawSplineSegs(Beam.NOISE_DIVISIONS, beam.Noise, sprite, halosprite,
					beam.HaloScale, frame, rendermode, beam.NumAttachments,
					beam.Attachment, beam.Width, beam.EndWidth, beam.Amplitude,
					beam.Freq, beam.Speed, beam.Segments, beam.Flags, color, beam.FadeLength, beam.HDRColorScale);
				break;

			case TempEntType.BeamLaser:
				DrawLaser(beam, frame, rendermode, color, sprite, halosprite, beam.HDRColorScale);
				break;

			case TempEntType.BeamTesla:
				DrawTesla(beam, frame, rendermode, color, sprite, beam.HDRColorScale);
				break;

			default:
				DevWarning(1, $"ViewRenderBeams.DrawBeam:  Unknown beam type {beam.Type}\n");
				break;
		}
	}

	private void DrawDisk(int nOISE_DIVISIONS, float[] noise, Model sprite, int frame, RenderMode rendermode, Vector3 vector3, Vector3 delta, float width, float amplitude, double freq, double speed, int segments, Span<float> color, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawCylinder(int nOISE_DIVISIONS, float[] noise, Model sprite, int frame, RenderMode rendermode, Vector3 vector3, Vector3 delta, float width, float amplitude, double freq, double speed, int segments, Span<float> color, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawBeamWithHalo(Beam beam, int frame, RenderMode rendermode, Span<float> color, Span<float> srcColor, Model sprite, Model halosprite, float hDRColorScale) {
		throw new NotImplementedException();
	}

	static TokenCache hdrColorScaleCache;
	private void DrawSegs(int noise_divisions, ReadOnlySpan<float> noise, Model spritemodel, int frame, RenderMode rendermode, in Vector3 source, in Vector3 delta, float startWidth, float endWidth, float scale, double freq, double speed, int segments, BeamFlags flags, Span<float> color, float fadeLength, float hdrColorScale) {
		int i, noiseIndex, noiseStep;
		float div, length, fraction, factor, vLast, vStep, brightness;

		Assert(fadeLength >= 0.0f);
		EngineSprite? pSprite = Draw_SetSpriteTexture(spritemodel, frame, rendermode);
		if (pSprite == null)
			return;

		if (segments < 2)
			return;

		IMaterial? pMaterial = pSprite.GetMaterial(rendermode);
		if (pMaterial != null) {
			IMaterialVar? hdrColorScaleVar = pMaterial.FindVarFast("$hdrcolorscale", ref hdrColorScaleCache);
			hdrColorScaleVar?.SetFloatValue(hdrColorScale);
		}

		length = MathLib.VectorLength(delta);
		float flMaxWidth = MathF.Max(startWidth, endWidth) * 0.5f;
		div = 1.0f / (segments - 1);

		if (length * div < flMaxWidth * 1.414f) {
			// Here, we have too many segments; we could get overlap... so lets have less segments
			segments = (int)(length / (flMaxWidth * 1.414f)) + 1;
			if (segments < 2) {
				segments = 2;
			}
		}

		if (segments > noise_divisions)     // UNDONE: Allow more segments?
		{
			segments = noise_divisions;
		}

		div = 1.0f / (segments - 1);
		length *= 0.01f;

		// UNDONE: Expose texture length scale factor to control "fuzziness"

		if ((flags & BeamFlags.NoTile) != 0) {
			// Don't tile
			vStep = div;
		}
		else {
			// Texture length texels per space pixel
			vStep = length * div;
		}

		// UNDONE: Expose this paramter as well(3.5)?  Texture scroll rate along beam
		vLast = MathLib.Fmodf((float)(freq * speed), 1); // Scroll speed 3.5 -- initial texture position, scrolls 3.5/sec (1.0 is entire texture)

		if ((flags & BeamFlags.SineNoise) != 0) {
			if (segments < 16) {
				segments = 16;
				div = 1.0f / (segments - 1);
			}
			scale *= 100;
			length = segments * (1.0f / 10);
		}
		else {
			scale *= length;
		}

		// Iterator to resample noise waveform (it needs to be generated in powers of 2)
		noiseStep = (int)((float)(noise_divisions - 1) * div * 65536.0f);
		noiseIndex = 0;

		if ((flags & BeamFlags.SineNoise) != 0) {
			noiseIndex = 0;
		}

		brightness = 1.0f;
		if ((flags & BeamFlags.ShadeIn) != 0) {
			brightness = 0;
		}

		// What fraction of beam should be faded
		Assert(fadeLength >= 0.0f);
		float fadeFraction = fadeLength / delta.Length();

		// BUGBUG: This code generates NANs when fadeFraction is zero! REVIST!
		fadeFraction = Math.Clamp(fadeFraction, 1e-6f, 1f);

		// Choose two vectors that are perpendicular to the beam
		ComputeBeamPerpendicular(delta, out Vector3 perp1);

		// Specify all the segments.
		using MatRenderContextPtr renderContext = new(materials);
		BeamSegDraw segDraw = new();
		segDraw.Start(renderContext.Context, segments, null);

		for (i = 0; i < segments; i++) {
			Assert(noiseIndex < (noise_divisions << 16));
			BeamSeg curSeg = default;
			curSeg.Alpha = 1;

			fraction = i * div;

			// Fade in our out beam to fadeLength

			if ((flags & BeamFlags.ShadeIn) != 0 && (flags & BeamFlags.ShadeOut) != 0) {
				if (fraction < 0.5) {
					brightness = 2 * (fraction / fadeFraction);
				}
				else {
					brightness = 2 * (1.0f - (fraction / fadeFraction));
				}
			}
			else if ((flags & BeamFlags.ShadeIn) != 0) {
				brightness = fraction / fadeFraction;
			}
			else if ((flags & BeamFlags.ShadeOut) != 0) {
				brightness = 1.0f - (fraction / fadeFraction);
			}

			// clamps
			if (brightness < 0) {
				brightness = 0;
			}
			else if (brightness > 1) {
				brightness = 1;
			}

			MathLib.VectorScale(color, brightness, out curSeg.Color);

			// UNDONE: Make this a spline instead of just a line?
			MathLib.VectorMA(source, fraction, delta, out curSeg.Pos);

			// Distort using noise
			if (scale != 0) {
				factor = noise[noiseIndex >> 16] * scale;
				if ((flags & BeamFlags.SineNoise) != 0) {
					MathLib.SinCos((float)(fraction * Math.PI * length + freq), out float s, out float c);
					MathLib.VectorMA(curSeg.Pos, factor * s, CurrentViewUp(), out curSeg.Pos);
					// Rotate the noise along the perpendicluar axis a bit to keep the bolt from looking diagonal
					MathLib.VectorMA(curSeg.Pos, factor * c, CurrentViewRight(), out curSeg.Pos);
				}
				else {
					MathLib.VectorMA(curSeg.Pos, factor, perp1, out curSeg.Pos);
				}
			}

			// Specify the next segment.
			if (endWidth == startWidth)
				curSeg.Width = startWidth * 2;
			else
				curSeg.Width = ((fraction * (endWidth - startWidth)) + startWidth) * 2;

			curSeg.TexCoord = vLast;
			segDraw.NextSeg(ref curSeg);


			vLast += vStep; // Advance texture scroll (v axis only)
			noiseIndex += noiseStep;
		}

		segDraw.End();
	}

	private void ComputeBeamPerpendicular(in Vector3 delta, out Vector3 perp) {
		Vector3 vecBeamCenter = delta;
		MathLib.VectorNormalize(ref vecBeamCenter);

		MathLib.CrossProduct(CurrentViewForward(), vecBeamCenter, out perp);
		MathLib.VectorNormalize(ref perp);
	}

	private EngineSprite? Draw_SetSpriteTexture(Model spritemodel, int frame, RenderMode rendermode) {
		EngineSprite? psprite;
		IMaterial? material;

		psprite = (EngineSprite?)modelinfo.GetModelExtraData(spritemodel);
		Assert(psprite);

		material = psprite!.GetMaterial(rendermode, frame);
		if (material == null)
			return null;

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Bind(material);
		return psprite;
	}

	private void DrawBeamFollow(Model sprite, Beam beam, int frame, RenderMode rendermode, double frameTime, Span<float> color, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawRing(int nOISE_DIVISIONS, float[] noise1, Action<Span<float>, int, float> noise2, Model sprite, int frame, RenderMode rendermode, Vector3 vector3, Vector3 delta, float width, float amplitude, double freq, double speed, int segments, Span<float> color, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawSplineSegs(int nOISE_DIVISIONS, float[] noise, Model sprite, Model? halosprite, float haloScale, int frame, RenderMode rendermode, int numAttachments, InlineArrayNewMaxBeamEnts<Vector3> attachment, float width, float endWidth, float amplitude, double freq, double speed, int segments, BeamFlags flags, Span<float> color, float fadeLength, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawLaser(Beam beam, int frame, RenderMode rendermode, Span<float> color, Model sprite, Model? halosprite, float hDRColorScale) {
		throw new NotImplementedException();
	}

	private void DrawTesla(Beam beam, int frame, RenderMode rendermode, Span<float> color, Model sprite, float hDRColorScale) {
		throw new NotImplementedException();
	}

	public void FreeBeam(Beam pBeam) {
		BeamFree(pBeam);
	}

	public void InitBeams() {
		NumBeamTrails = DEFAULT_PARTICLES;
		BeamTrails = new BeamTrail[NumBeamTrails];

		// Clear them out
		ClearBeams();
	}

	public void KillDeadBeams(BaseEntity? deadEntity) {
		Beam? beam;
		Beam? newlist;
		Beam? next;
		BeamTrail? head;  // Build a new list to replace m_pActiveBeams.

		beam = ActiveBeams;  // Old list.
		newlist = null;           // New list.

		while (beam != null) {
			next = beam.Next;
			if (beam.Entity[0].Get() != deadEntity)   // Link into new list.
			{
				beam.Next = newlist;
				newlist = beam;

				beam = next;
				continue;
			}

			beam.Flags &= ~(BeamFlags.StartEntity | BeamFlags.EndEntity);
			if (beam.Type != TempEntType.BeamFollow) {
				// Die Die Die!
				beam.Die = gpGlobals.CurTime - 0.1;

				// Kill off particles
				head = beam.Trail;
				while (head != null) {
					head.Die = gpGlobals.CurTime - 0.1;
					head = head.Next;
				}

				// Free the beam
				BeamFree(beam);
			}
			else {
				// Stay active
				beam.Next = newlist;
				newlist = beam;
			}
			beam = next;
		}

		// We now have a new list with the bogus stuff released.
		ActiveBeams = newlist;
	}

	public void ShutdownBeams() {
		throw new NotImplementedException();
	}

	public void UpdateBeamInfo(Beam pBeam, ref BeamInfo beamInfo) {
		pBeam.Attachment[0] = beamInfo.Start;
		pBeam.Attachment[1] = beamInfo.End;
		pBeam.Delta = beamInfo.End - beamInfo.Start;

		Assert(pBeam.Delta.IsValid());

		SetBeamAttributes(pBeam, ref beamInfo);
	}

	private void SetBeamAttributes(Beam pBeam, ref BeamInfo beamInfo) {
		pBeam.Frame = (TimeUnit_t)beamInfo.StartFrame;
		pBeam.FrameRate = beamInfo.FrameRate;
		pBeam.Flags |= beamInfo.Flags;

		pBeam.R = beamInfo.Red;
		pBeam.G = beamInfo.Green;
		pBeam.B = beamInfo.Blue;
	}

	public int TotalBeams {
		get {
			int c = 0;
			var beam = ActiveBeams;
			while (beam != null) {
				c++;
				beam = beam.Next;
			}

			return c;
		}
	}

	public void UpdateTempEntBeams() {
		if (ActiveBeams == null)
			return;

		// Get frame time
		TimeUnit_t frametime = gpGlobals.FrameTime;

		if (frametime == 0.0)
			return;

		// Draw temporary entity beams
		Beam? pPrev = null;
		Beam? pNext;
		for (Beam? pBeam = ActiveBeams; pBeam != null; pBeam = pNext) {
			// Need to store the next one since we may delete this one
			pNext = pBeam.Next;

			// Retire old beams
			if ((pBeam.Flags & BeamFlags.Forever) == 0 && pBeam.Die <= gpGlobals.CurTime) {
				// Reset links
				if (pPrev != null)
					pPrev.Next = pNext;
				else
					ActiveBeams = pNext;

				// Free the beam
				BeamFree(pBeam);

				pBeam = null;
				continue;
			}

			// Update beam state
			UpdateBeam(pBeam, frametime);

			// Compute bounds for the beam
			pBeam.ComputeBounds();

			// Indicates the beam moved
			if (pBeam.m_RenderHandle != INVALID_CLIENT_RENDER_HANDLE)
				clientLeafSystem.RenderableChanged(pBeam.m_RenderHandle);

			pPrev = pBeam;
		}
	}
}

public static class ViewRenderBeams_Exposed { static readonly ViewRenderBeams s_ViewRenderBeams = new(); public static readonly IViewRenderBeams beams = s_ViewRenderBeams; }
