#if GAME_DLL
using Game.Server;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Physics;
using Source.Common.SoundEmitterSystem;

using System.Numerics;

namespace Game.Shared;

public struct ImpactSound
{
	public object? GameData;
	public int EntityIndex;
	public SoundEntityChannel SoundChannel;
	public float Volume;
	public float ImpactSpeed;
	public ushort SurfaceProps;
	public ushort SurfacePropsHit;
	public Vector3 Origin;
}

public struct BreakSound
{
	public Vector3 Origin;
	public int SurfacePropsBreak;
}

public static class PhysicsSound
{
	public static void PlayImpactSounds(List<ImpactSound> list) {
		for (int i = list.Count - 1; i >= 0; --i) {
			ImpactSound sound = list[i];
			SurfaceData_ptr surf = physprops.GetSurfaceData(sound.SurfaceProps)!;
			if (surf.Sounds.ImpactHard != 0) {
				SurfaceData_ptr? hit = physprops.GetSurfaceData(sound.SurfacePropsHit);
				UtlSymId_t soundName = surf.Sounds.ImpactHard;
				if (hit != null && surf.Sounds.ImpactSoft != 0) {
					if (hit.Audio.HardnessFactor < surf.Audio.HardThreshold ||
						(surf.Audio.HardVelocityThreshold > 0 && surf.Audio.HardVelocityThreshold > sound.ImpactSpeed)) {
						soundName = surf.Sounds.ImpactSoft;
					}
				}
				ReadOnlySpan<char> soundStr = physprops.GetString(soundName);

				SoundParameters parms = new();
				if (!BaseEntity.GetParametersForSound(soundStr, ref parms, null))
					break;

				if (sound.Volume > 1)
					sound.Volume = 1;
				PASAttenuationFilter filter = new(sound.Origin, parms.SoundLevel);
				scoped EmitSound_t ep = new();
				ep.Channel = (int)sound.SoundChannel;
				ep.SoundName = ((ReadOnlySpan<char>)parms.SoundName).SliceNullTerminatedString();
				ep.Volume = parms.Volume * sound.Volume;
				ep.SoundLevel = parms.SoundLevel;
				ep.Pitch = parms.Pitch;
				ep.Origin = ref sound.Origin;

				BaseEntity.EmitSound(filter, 0, in ep);
			}
		}
		list.Clear();
	}

	public static void AddImpactSound(List<ImpactSound> list, object? gameData, int entityIndex, SoundEntityChannel soundChannel, IPhysicsObject obj, int surfaceProps, int surfacePropsHit, float volume, float impactSpeed) {
		impactSpeed += 1e-4f;
		Span<ImpactSound> sounds = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
		for (int i = sounds.Length - 1; i >= 0; --i) {
			ref ImpactSound sound = ref sounds[i];

			if (surfaceProps == sound.SurfaceProps || list.Count > 4) {
				if (volume > sound.Volume) {
					obj.GetPosition(out sound.Origin, out _);
					sound.GameData = gameData;
					sound.EntityIndex = entityIndex;
					sound.SoundChannel = soundChannel;
					sound.SurfacePropsHit = (ushort)surfacePropsHit;
				}
				sound.Volume += volume;
				sound.ImpactSpeed = MathF.Max(impactSpeed, sound.ImpactSpeed);
				return;
			}
		}

		ImpactSound newSound = default;
		newSound.GameData = gameData;
		newSound.EntityIndex = entityIndex;
		newSound.SoundChannel = soundChannel;
		obj.GetPosition(out newSound.Origin, out _);
		newSound.SurfaceProps = (ushort)surfaceProps;
		newSound.SurfacePropsHit = (ushort)surfacePropsHit;
		newSound.Volume = volume;
		newSound.ImpactSpeed = impactSpeed;
		list.Add(newSound);
	}

	public static void AddBreakSound(List<BreakSound> list, in Vector3 origin, ushort surfaceProps) {
		SurfaceData_ptr surf = physprops.GetSurfaceData(surfaceProps)!;
		if (surf.Sounds.BreakSound == 0)
			return;

		Span<BreakSound> sounds = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
		for (int i = sounds.Length - 1; i >= 0; --i) {
			ref BreakSound sound = ref sounds[i];
			if (list.Count > 2 && surfaceProps == sound.SurfacePropsBreak) {
				sound.Origin = (sound.Origin + origin) * 0.5f;
				return;
			}
		}
		BreakSound newSound;
		newSound.Origin = origin;
		newSound.SurfacePropsBreak = surfaceProps;
		list.Add(newSound);
	}

	public static void PlayBreakSounds(List<BreakSound> list) {
		for (int i = list.Count - 1; i >= 0; --i) {
			BreakSound sound = list[i];

			SurfaceData_ptr surf = physprops.GetSurfaceData(sound.SurfacePropsBreak)!;
			ReadOnlySpan<char> soundStr = physprops.GetString(surf.Sounds.BreakSound);
			SoundParameters parms = new();
			if (!BaseEntity.GetParametersForSound(soundStr, ref parms, null))
				return;

			PASAttenuationFilter filter = new(sound.Origin, parms.SoundLevel);
			scoped EmitSound_t ep = new();
			ep.Channel = (int)SoundEntityChannel.Static;
			ep.SoundName = ((ReadOnlySpan<char>)parms.SoundName).SliceNullTerminatedString();
			ep.Volume = parms.Volume;
			ep.SoundLevel = parms.SoundLevel;
			ep.Pitch = parms.Pitch;
			ep.Origin = ref sound.Origin;
			BaseEntity.EmitSound(filter, 0, in ep);
		}
		list.Clear();
	}
}
#endif
