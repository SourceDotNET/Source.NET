using Source.Common.Commands;
using Source.Common.Filesystem;

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.AudioSystem;

[InlineArray(SndDsp.CPSET_PRCS)]
public struct PrcArray
{
	Prc element;
}

[InlineArray(SndDsp.CPSET_STATES)]
public struct PsetStates
{
	int element;
}

// NOTE: do not reorder members of pset_t - g_psettemplates relies on it!!!

public class Pset
{
	public int type;                        // preset configuration type
	public int cprcs;                       // number of processors for this preset

	public PrcArray prcs;                   // processor preset data

	public float mix_min;                   // min dsp mix at close range
	public float mix_max;                   // max dsp mix at long range
	public float db_min;                    // if sndlvl of a new sound is < db_min, reduce mix_min/max by db_mixdrop
	public float db_mixdrop;                // reduce mix_min/max by n% if sndlvl of new sound less than db_min
	public float duration;                  // if > 0, duration of preset in seconds (duration 0 = infinite)
	public float fade;                      // fade out time, exponential fade

	public int csamp_duration;              // duration counter # samples

	public PsetStates w;                    // internal states
	public int fused;

	public void Clear() {
		type = 0;
		cprcs = 0;
		prcs = default;
		mix_min = mix_max = db_min = db_mixdrop = duration = fade = 0;
		csamp_duration = 0;
		w = default;
		fused = 0;
	}

	public void CopyFrom(Pset src) {
		type = src.type;
		cprcs = src.cprcs;
		prcs = src.prcs;
		mix_min = src.mix_min;
		mix_max = src.mix_max;
		db_min = src.db_min;
		db_mixdrop = src.db_mixdrop;
		duration = src.duration;
		fade = src.fade;
		csamp_duration = src.csamp_duration;
		w = src.w;
		fused = src.fused;
	}
}

[InlineArray(SndDsp.DSPCHANMAX)]
public struct DspPsets
{
	Pset? element;
}

public class Dsp
{
	public bool fused;
	public int cchan;                       // 1-5 channels, ie: mono, FrontLeft, FrontRight, RearLeft, RearRight, FrontCenter

	public DspPsets ppset;                  // current preset (1-5 channels)
	public int ipset;                       // current ipreset

	public DspPsets ppsetprev;              // previous preset (1-5 channels)
	public int ipsetprev;                   // previous ipreset

	public float xfade;                     // crossfade time between previous preset and new
	public float xfade_default;             // default xfade value, set in DSP_Alloc
	public bool bexpfade;                   // true if exponential crossfade

	public int ipsetsav_oneshot;            // previous preset before one-shot preset was set

	public Rmp xramp;                       // crossfade ramp

	public Pset GetPset(int i) => ppset[i]!;
	public void SetPset(int i, Pset? p) => ppset[i] = p;
	public Pset GetPsetPrev(int i) => ppsetprev[i]!;
	public void SetPsetPrev(int i, Pset? p) => ppsetprev[i] = p;

	public void Clear() {
		fused = false;
		cchan = 0;
		ppset = default;
		ipset = 0;
		ppsetprev = default;
		ipsetprev = 0;
		xfade = xfade_default = 0;
		bexpfade = false;
		ipsetsav_oneshot = 0;
		xramp = default;
	}
}

[InlineArray(6)]
public struct SurfaceReflArray
{
	float element;
}

// parameter batch

public struct AutoParams
{
	// passed in params

	public bool bskyabove;          // true if sky is mostly above player
	public int width;               // max width of room in inches
	public int length;              // max length of room in inches (length always > width)
	public int height;              // max height of room in inches
	public float fdiffusion;        // diffusion of room 0..1.0
	public float freflectivity;     // average reflectivity of all surfaces in room 0..1.0
	public SurfaceReflArray surface_refl; // reflectivity for left,right,front,back,ceiling,floor surfaces 0.0 for open surface (sky or no hit)

	// derived params

	public int shape;               // ADSP_ROOM, etc 0...4
	public int size;                // ADSP_SIZE_SMALL, etc	0...3
	public int len;                 // ADSP_LENGTH_SHORT, etc 0...3
	public int wid;                 // ADSP_WIDTH_NARROW, etc 0...3
	public int ht;                  // ADSP_HEIGHT_LOW, etc 0...3
	public int reflectivity;        // ADSP_DULL, etc 0..3
	public int diffusion;           // ADSP_EMPTY, etc 0...3
}

public static partial class SndDsp
{
	// DSP presets

	// A dsp preset comprises one or more dsp processors in linear, parallel or feedback configuration

	// preset configurations
	//
	public const int PSET_SIMPLE = 0;

	// x(n)--->P(0)--->y(n)

	public const int PSET_LINEAR = 1;

	// x(n)--->P(0)-->P(1)-->...P(m)--->y(n)


	public const int PSET_PARALLEL2 = 5;

	// x(n)--->P(0)-->(+)-->y(n)
	//      	       ^
	//		           |
	// x(n)--->P(1)-----

	public const int PSET_PARALLEL4 = 6;

	// x(n)--->P(0)-->P(1)-->(+)-->y(n)
	//      				  ^
	//		                  |
	// x(n)--->P(2)-->P(3)-----

	public const int PSET_PARALLEL5 = 7;

	// x(n)--->P(0)-->P(1)-->(+)-->P(4)-->y(n)
	//      				  ^
	//		                  |
	// x(n)--->P(2)-->P(3)-----

	public const int PSET_FEEDBACK = 8;

	// x(n)-P(0)--(+)-->P(1)-->P(2)---->y(n)
	//             ^				|
	//             |                v
	//		       -----P(4)<--P(3)--

	public const int PSET_FEEDBACK3 = 9;

	// x(n)---(+)-->P(0)--------->y(n)
	//         ^                |
	//         |                v
	//		   -----P(2)<--P(1)--

	public const int PSET_FEEDBACK4 = 10;

	// x(n)---(+)-->P(0)-------->P(3)--->y(n)
	//         ^              |
	//         |              v
	//		   ---P(2)<--P(1)--

	public const int PSET_MOD = 11;

	//
	// x(n)------>P(1)--P(2)--P(3)--->y(n)
	//                    ^
	// x(n)------>P(0)....:

	public const int PSET_MOD2 = 12;

	//
	// x(n)-------P(1)-->y(n)
	//              ^
	// x(n)-->P(0)..:


	public const int PSET_MOD3 = 13;

	//
	// x(n)-------P(1)-->P(2)-->y(n)
	//              ^
	// x(n)-->P(0)..:


	public const int CPSETS = 64;               // max number of presets simultaneously active

	public const int CPSET_PRCS = 5;            // max # of processors per dsp preset
	public const int CPSET_STATES = CPSET_PRCS + 3; // # of internal states

	static readonly Pset[] psets = CreatePool<Pset>(CPSETS);

	static Pset[]? g_psettemplates = null;
	static int g_cpsettemplates = 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static Span<Prc> Prcs(Pset ppset) => ppset.prcs;

	// returns true if preset will expire after duration

	static bool PSET_IsOneShot(Pset ppset) {
		return ppset.duration > 0.0;
	}

	// return true if preset is no longer active - duration has expired

	static bool PSET_HasExpired(Pset ppset) {
		if (!PSET_IsOneShot(ppset))
			return false;

		return ppset.csamp_duration <= 0;
	}

	// if preset is oneshot, update duration counter by SampleCount samples

	static void PSET_UpdateDuration(Pset ppset, int SampleCount) {
		if (PSET_IsOneShot(ppset)) {
			// if oneshot preset and not expired, decrement sample count

			if (ppset.csamp_duration > 0)
				ppset.csamp_duration -= SampleCount;
		}
	}

	// A dsp processor (prc) performs a single-sample function, such as pitch shift, delay, reverb, filter


	// init a preset - just clear state array

	static void PSET_Init(Pset? ppset) {
		// clear state array

		if (ppset != null)
			ppset.w = default;
	}

	// clear runtime slots

	static void PSET_InitAll() {
		for (int i = 0; i < CPSETS; i++)
			psets[i].Clear();
	}

	// free the preset - free all processors

	static void PSET_Free(Pset? ppset) {
		if (ppset != null) {
			// free processors

			PRC_FreeAll(Prcs(ppset), ppset.cprcs);

			// clear

			ppset.Clear();
		}
	}

	static void PSET_FreeAll() { for (int i = 0; i < CPSETS; i++) PSET_Free(psets[i]); }

	// return preset struct, given index into preset template array
	// NOTE: should not ever be more than 2 or 3 of these active simultaneously

	static Pset? PSET_Alloc(int ipsettemplate) {
		Pset ppset;
		bool fok;

		// don't excede array bounds

		if (ipsettemplate >= g_cpsettemplates)
			ipsettemplate = 0;

		// find free slot
		int i = 0;
		for (i = 0; i < CPSETS; i++) {
			if (psets[i].fused == 0)
				break;
		}

		if (i == CPSETS)
			return null;

		if (das_debug.GetInt() != 0) {
			int nSlots = 0;
			for (int j = 0; j < CPSETS; j++) {
				if (psets[j].fused != 0)
					nSlots++;
			}
			DevMsg($"total preset slots used: {nSlots} \n");
		}


		ppset = psets[i];

		// clear preset

		ppset.Clear();

		// copy template into preset

		ppset.CopyFrom(g_psettemplates![ipsettemplate]);

		ppset.fused = 1;

		// clear state array

		PSET_Init(ppset);

		// init all processors, set up processor function pointers

		fok = PRC_InitAll(Prcs(ppset), ppset.cprcs);

		if (!fok) {
			// failed to init one or more processors
			Warning("Sound DSP: preset failed to init.\n");
			PRC_FreeAll(Prcs(ppset), ppset.cprcs);
			return null;
		}

		// if preset has duration, setup duration sample counter

		if (PSET_IsOneShot(ppset))
			ppset.csamp_duration = SEC_TO_SAMPS(ppset.duration);

		return ppset;
	}

	// batch version of PSET_GetNext for linear array of processors.  For performance.

	// ppset - preset array
	// pbuffer - input sample data
	// SampleCount - size of input buffer
	// OP:	OP_LEFT				- process left channel in place
	//		OP_RIGHT			- process right channel in place
	//		OP_LEFT_DUPLICATe	- process left channel, duplicate into right

#if GMOD_DLL
	static int g_DspCurrentPreset;
	static bool DSP_UsesSfxVolume() => g_DspCurrentPreset >= 35 && g_DspCurrentPreset <= 38;

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void PSET_GetNextN(Pset ppset, Span<PortableSamplePair> pbuffer, int SampleCount, int op) {
		PSET_GetNextNUnscaled(ppset, pbuffer, SampleCount, op);

		if (!DSP_UsesSfxVolume())
			return;

		float sfxVolume = Source.Common.Audio.SndGain.volume_sfx.GetFloat();
		for (int i = 0; i < SampleCount; i++) {
			pbuffer[i].Left = (int)(pbuffer[i].Left * sfxVolume);
			pbuffer[i].Right = (int)(pbuffer[i].Right * sfxVolume);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static int PSET_GetNext(Pset ppset, int x) {
		int y = PSET_GetNextUnscaled(ppset, x);
		return DSP_UsesSfxVolume() ? (int)(y * Source.Common.Audio.SndGain.volume_sfx.GetFloat()) : y;
	}
#endif

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
#if GMOD_DLL
	static void PSET_GetNextNUnscaled(Pset ppset, Span<PortableSamplePair> pbuffer, int SampleCount, int op) {
#else
	static void PSET_GetNextN(Pset ppset, Span<PortableSamplePair> pbuffer, int SampleCount, int op) {
#endif
		Span<PortableSamplePair> pbf = pbuffer;
		Span<Prc> pprc;
		int count = ppset.cprcs;

		switch (ppset.type) {
			default:
			case PSET_SIMPLE: {
					// x(n)--->P(0)--->y(n)

					Prcs(ppset)[0].pdata!.GetNextN(pbf, SampleCount, op);
					return;
				}
			case PSET_LINEAR: {

					//      w0     w1     w2
					// x(n)--->P(0)-->P(1)-->...P(count-1)--->y(n)

					//      w0     w1     w2     w3     w4     w5
					// x(n)--->P(0)-->P(1)-->P(2)-->P(3)-->P(4)-->y(n)

					// call batch processors in sequence - no internal state for batch processing

					// point to first processor

					pprc = Prcs(ppset);

					for (int i = 0; i < count; i++)
						pprc[i].pdata!.GetNextN(pbf, SampleCount, op);

					return;
				}
		}
	}


	// Get next sample from this preset.  called once for every sample in buffer
	// ppset is pointer to preset
	// x is input sample

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
#if GMOD_DLL
	static int PSET_GetNextUnscaled(Pset ppset, int x) {
#else
	static int PSET_GetNext(Pset ppset, int x) {
#endif

		// pset_simple and pset_linear have no internal state:
		// this is REQUIRED for all presets that have a batch getnextN equivalent!

		if (ppset.type == PSET_SIMPLE) {
			// x(n)--->P(0)--->y(n)

			return Prcs(ppset)[0].pdata!.GetNext(x);
		}

		Span<Prc> pprc;
		int count = ppset.cprcs;

		if (ppset.type == PSET_LINEAR) {
			int y = x;

			//      w0     w1     w2
			// x(n)--->P(0)-->P(1)-->...P(count-1)--->y(n)

			//      w0     w1     w2     w3     w4     w5
			// x(n)--->P(0)-->P(1)-->P(2)-->P(3)-->P(4)-->y(n)

			// call processors in reverse order, from count to 1

			//for (int i = count; i > 0; i--, pprc--)
			//	w[i] = pprc->pfnGetNext (pprc->pdata, w[i-1]);

			// return w[count];


			// point to first processor, update sequentially, no state preserved

			pprc = Prcs(ppset);
			int ip = 0;

			switch (count) {
				default:
				case 5:
					y = pprc[ip].pdata!.GetNext(y);
					ip++;
					goto case 4;
				case 4:
					y = pprc[ip].pdata!.GetNext(y);
					ip++;
					goto case 3;
				case 3:
					y = pprc[ip].pdata!.GetNext(y);
					ip++;
					goto case 2;
				case 2:
					y = pprc[ip].pdata!.GetNext(y);
					ip++;
					goto case 1;
				case 1:
				case 0:
					y = pprc[ip].pdata!.GetNext(y);
					break;
			}

			return y;
		}

		// all other preset types have internal state:

		// initialize 0'th element of state array

		Span<int> w = ppset.w;
		w[0] = x;

		switch (ppset.type) {
			default:

			case PSET_PARALLEL2: {   //     w0      w1    w3
									 // x(n)--->P(0)-->(+)-->y(n)
									 //      	       ^
									 //	   w0      w2  |
									 // x(n)--->P(1)-----

					pprc = Prcs(ppset);

					w[3] = w[1] + w[2];

					w[1] = pprc[0].pdata!.GetNext(w[0]);
					pprc = pprc[1..];
					w[2] = pprc[0].pdata!.GetNext(w[0]);

					return w[3];
				}

			case PSET_PARALLEL4: {   //     w0      w1     w2    w5
									 // x(n)--->P(0)-->P(1)-->(+)-->y(n)
									 //      				  ^
									 //	   w0      w3     w4  |
									 // x(n)--->P(2)-->P(3)-----


					pprc = Prcs(ppset);

					w[5] = w[2] + w[4];

					w[2] = pprc[1].pdata!.GetNext(w[1]);
					w[4] = pprc[3].pdata!.GetNext(w[3]);

					w[1] = pprc[0].pdata!.GetNext(w[0]);
					w[3] = pprc[2].pdata!.GetNext(w[0]);

					return w[5];
				}

			case PSET_PARALLEL5: {   //     w0      w1     w2    w5     w6
									 // x(n)--->P(0)-->P(1)-->(+)--P(4)-->y(n)
									 //      				  ^
									 //	   w0      w3     w4  |
									 // x(n)--->P(2)-->P(3)-----

					pprc = Prcs(ppset);

					w[5] = w[2] + w[4];

					w[2] = pprc[1].pdata!.GetNext(w[1]);
					w[4] = pprc[3].pdata!.GetNext(w[3]);

					w[1] = pprc[0].pdata!.GetNext(w[0]);
					w[3] = pprc[2].pdata!.GetNext(w[0]);

					return pprc[4].pdata!.GetNext(w[5]);
				}

			case PSET_FEEDBACK: {
					//    w0    w1   w2     w3      w4    w7
					// x(n)-P(0)--(+)-->P(1)-->P(2)-->---->y(n)
					//             ^				|
					//             |  w6     w5     v
					//		       -----P(4)<--P(3)--

					pprc = Prcs(ppset);

					// start with adders

					w[2] = w[1] + w[6];

					// evaluate in reverse order

					w[6] = pprc[4].pdata!.GetNext(w[5]);
					w[5] = pprc[3].pdata!.GetNext(w[4]);

					w[4] = pprc[2].pdata!.GetNext(w[3]);
					w[3] = pprc[1].pdata!.GetNext(w[2]);
					w[1] = pprc[0].pdata!.GetNext(w[0]);

					return w[4];
				}
			case PSET_FEEDBACK3: {
					//     w0     w1     w2
					// x(n)---(+)-->P(0)--------->y(n)
					//         ^                |
					//         |  w4     w3     v
					//		   -----P(2)<--P(1)--

					pprc = Prcs(ppset);

					// start with adders

					w[1] = w[0] + w[4];

					// evaluate in reverse order

					w[4] = pprc[2].pdata!.GetNext(w[3]);
					w[3] = pprc[1].pdata!.GetNext(w[2]);
					w[2] = pprc[0].pdata!.GetNext(w[1]);

					return w[2];
				}
			case PSET_FEEDBACK4: {
					//     w0    w1      w2           w5
					// x(n)---(+)-->P(0)-------->P(3)--->y(n)
					//         ^              |
					//         | w4     w3    v
					//		   ---P(2)<--P(1)--

					pprc = Prcs(ppset);

					// start with adders

					w[1] = w[0] + w[4];

					// evaluate in reverse order

					w[5] = pprc[3].pdata!.GetNext(w[2]);
					w[4] = pprc[2].pdata!.GetNext(w[3]);
					w[3] = pprc[1].pdata!.GetNext(w[2]);
					w[2] = pprc[0].pdata!.GetNext(w[1]);

					return w[2];
				}
			case PSET_MOD: {
					//		w0		  w1    w3     w4
					// x(n)------>P(1)--P(2)--P(3)--->y(n)
					//      w0        w2  ^
					// x(n)------>P(0)....:

					pprc = Prcs(ppset);

					w[4] = pprc[3].pdata!.GetNext(w[3]);

					w[3] = pprc[2].pdata!.GetNext(w[1]);

					// modulate processor 2

					pprc[2].pdata!.Mod((float)w[2] / (float)PMAX);

					// get modulator output

					w[2] = pprc[0].pdata!.GetNext(w[0]);

					w[1] = pprc[1].pdata!.GetNext(w[0]);

					return w[4];
				}
			case PSET_MOD2: {
					//      w0           w2
					// x(n)---------P(1)-->y(n)
					//      w0    w1  ^
					// x(n)-->P(0)....:

					pprc = Prcs(ppset);

					// modulate processor 1

					pprc[1].pdata!.Mod((float)w[1] / (float)PMAX);

					// get modulator output

					w[1] = pprc[0].pdata!.GetNext(w[0]);

					w[2] = pprc[1].pdata!.GetNext(w[0]);

					return w[2];

				}
			case PSET_MOD3: {
					//      w0           w2      w3
					// x(n)----------P(1)-->P(2)-->y(n)
					//      w0    w1   ^
					// x(n)-->P(0).....:

					pprc = Prcs(ppset);

					w[3] = pprc[2].pdata!.GetNext(w[2]);

					// modulate processor 1

					pprc[1].pdata!.Mod((float)w[1] / (float)PMAX);

					// get modulator output

					w[1] = pprc[0].pdata!.GetNext(w[0]);

					w[2] = pprc[1].pdata!.GetNext(w[0]);

					return w[2];
				}
		}
	}


	/////////////
	// DSP system
	/////////////

	// Main interface

	//     Whenever the preset # changes on any of these processors, the old processor is faded out, new is faded in.
	//     dsp_chan is optionally set when a sound is played - a preset is sent with the start_static/dynamic sound.
	//
	// sound1---->dsp_chan-->  -------------(+)---->dsp_water--->dsp_player--->out
	// sound2---->dsp_chan-->  |             |
	// sound3--------------->  ----dsp_room---
	//                         |             |
	//                         --dsp_indirect-

	//  dsp_room	- set this cvar to a preset # to change the room dsp.  room fx are more prevalent farther from player.
	//					use: when player moves into a new room, all sounds played in room take on its reverberant character
	//  dsp_water	- set this cvar (once) to a preset # for serial underwater sound.
	//					use: when player goes under water, all sounds pass through this dsp (such as low pass filter)
	//	dsp_player	- set this cvar to a preset # to cause all sounds to run through the effect (serial, in-line).
	//					use: player is deafened, player fires special weapon, player is hit by special weapon.
	//  dsp_facingaway- set this cvar to a preset # appropriate for sounds which are played facing away from player (weapon,voice)
	//
	//  dsp_spatial - set by system to create modulated spatial delays for left/right/front/back ears - delay value
	//					modulates by distance to nearest l/r surface in world

	// Dsp presets


	public static readonly ConVar dsp_room = new("dsp_room", "0", FCvar.Demo);              // room dsp preset - sounds more distant from player (1ch)
	public static readonly ConVar dsp_water = new("dsp_water", "14", FCvar.Demo);           // "14" underwater dsp preset - sound when underwater (1-2ch)
	public static readonly ConVar dsp_player = new("dsp_player", "0", FCvar.Demo | FCvar.ServerCanExecute);           // dsp on player - sound when player hit by special device (1-2ch)
	public static readonly ConVar dsp_facingaway = new("dsp_facingaway", "0", FCvar.Demo);      // "30" sounds that face away from player (weapons, voice) (1-4ch)
	public static readonly ConVar dsp_speaker = new("dsp_speaker", "50", FCvar.Demo);           // "50" small distorted speaker sound (1ch)
	public static readonly ConVar dsp_spatial = new("dsp_spatial", "40", FCvar.Demo);           // spatial delays for l/r front/rear ears
	public static readonly ConVar dsp_automatic = new("dsp_automatic", "0", FCvar.Demo);            // automatic room type detection. if non zero, replaces dsp_room

	static int ipset_room_prev;
	static int ipset_water_prev;
	static int ipset_player_prev;
	static int ipset_facingaway_prev;
	static int ipset_speaker_prev;
	static int ipset_spatial_prev;
	static int ipset_automatic_prev;

	// legacy room_type support

	public static readonly ConVar dsp_room_type = new("room_type", "0", FCvar.Demo);
	static int ipset_room_typeprev;


	// DSP processors

	public static int idsp_room;
	public static int idsp_water;
	public static int idsp_player;
	public static int idsp_facingaway;
	public static int idsp_speaker;
	public static int idsp_spatial;
	public static int idsp_automatic;

	public static readonly ConVar dsp_off = new("dsp_off", "0", FCvar.Cheat);                      // set to 1 to disable all dsp processing
	public static readonly ConVar dsp_slow_cpu = new("dsp_slow_cpu", "0", FCvar.Archive | FCvar.Demo);  // set to 1 if cpu bound - ie: does not process dsp_room fx
	public static readonly ConVar snd_profile = new("snd_profile", "0", FCvar.Demo);                    // 1 - profile dsp, 2 - mix, 3 - load sound, 4 - all sound
	public static readonly ConVar dsp_volume = new("dsp_volume", "1.0", FCvar.Archive | FCvar.Demo);    // 0.0 - 2.0; master dsp volume control
	public static readonly ConVar dsp_vol_5ch = new("dsp_vol_5ch", "0.5", FCvar.Demo);                  // 0.0 - 1.0; attenuate master dsp volume for 5ch surround
	public static readonly ConVar dsp_vol_4ch = new("dsp_vol_4ch", "0.5", FCvar.Demo);                  // 0.0 - 1.0; attenuate master dsp volume for 4ch surround
	public static readonly ConVar dsp_vol_2ch = new("dsp_vol_2ch", "1.0", FCvar.Demo);                  // 0.0 - 1.0; attenuate master dsp volume for 2ch surround

	public static readonly ConVar dsp_enhance_stereo = new("dsp_enhance_stereo", "0", FCvar.Archive);   // 1) use dsp_spatial delays on all reverb channels

	// DSP preset executor

	public const int CDSPS = 32;                // max number dsp executors active
	public const int DSPCHANMAX = 5;            // max number of channels dsp can process (allocs a separte processor for each chan)

	static readonly Dsp[] dsps = CreatePool<Dsp>(CDSPS);

	static void DSP_Init(int idsp) {
		Dsp pdsp;

		Assert(idsp < CDSPS);

		if (idsp < 0 || idsp >= CDSPS)
			return;

		pdsp = dsps[idsp];

		pdsp.Clear();
	}

	public static void DSP_Free(int idsp) {
		Dsp pdsp;

		Assert(idsp < CDSPS);

		if (idsp < 0 || idsp >= CDSPS)
			return;

		pdsp = dsps[idsp];

		for (int i = 0; i < pdsp.cchan; i++) {
			if (pdsp.GetPset(i) != null)
				PSET_Free(pdsp.GetPset(i));

			if (pdsp.GetPsetPrev(i) != null)
				PSET_Free(pdsp.GetPsetPrev(i));
		}

		pdsp.Clear();
	}

	// Init all dsp processors - called once, during engine startup

	public static void DSP_InitAll(bool bLoadPresetFile) {
		// only load template file on engine startup

		if (bLoadPresetFile)
			DSP_LoadPresetFile();

		// order is important, don't rearange.

		FLT_InitAll();
		DLY_InitAll();
		RVA_InitAll();
		LFOWAV_InitAll();
		LFO_InitAll();

		CRS_InitAll();
		PTC_InitAll();
		ENV_InitAll();
		EFO_InitAll();
		MDY_InitAll();
		AMP_InitAll();

		PSET_InitAll();

		for (int idsp = 0; idsp < CDSPS; idsp++)
			DSP_Init(idsp);
	}

	// free all resources associated with dsp - called once, during engine shutdown

	public static void DSP_FreeAll() {
		// order is important, don't rearange.

		for (int idsp = 0; idsp < CDSPS; idsp++)
			DSP_Free(idsp);

		AMP_FreeAll();
		MDY_FreeAll();
		EFO_FreeAll();
		ENV_FreeAll();
		PTC_FreeAll();
		CRS_FreeAll();

		LFO_FreeAll();
		LFOWAV_FreeAll();
		RVA_FreeAll();
		DLY_FreeAll();
		FLT_FreeAll();
	}


	// allocate a new dsp processor chain, kill the old processor.  Called during dsp init only.
	// ipset is new preset
	// xfade is crossfade time when switching between presets (milliseconds)
	// cchan is how many simultaneous preset channels to allocate (1-4)
	// return index to new dsp

	public static int DSP_Alloc(int ipset, float xfade, int cchan) {
		Dsp pdsp;
		int i;
		int idsp;
		int cchans = Math.Clamp(cchan, 1, DSPCHANMAX);

		// find free slot

		for (idsp = 0; idsp < CDSPS; idsp++) {
			if (!dsps[idsp].fused)
				break;
		}

		if (idsp >= CDSPS)
			return -1;

		pdsp = dsps[idsp];

		DSP_Init(idsp);

		pdsp.fused = true;

		pdsp.cchan = cchans;

		// allocate a preset processor for each channel

		pdsp.ipset = ipset;
		pdsp.ipsetprev = 0;
		pdsp.ipsetsav_oneshot = 0;

		for (i = 0; i < pdsp.cchan; i++) {
			pdsp.SetPset(i, PSET_Alloc(ipset));
			pdsp.SetPsetPrev(i, null);
		}

		// set up crossfade time in seconds

		pdsp.xfade = xfade / 1000.0F;
		pdsp.xfade_default = pdsp.xfade;

		RMP_SetEnd(ref pdsp.xramp);

		return idsp;
	}

	// call modulation function of specified processor within dsp preset

	// idsp - dsp preset
	// channel - channel 1-5 (l,r,rl,rr,fc)
	// iproc - which processor to change (normally 0)
	// value - new parameter value for processor

	// NOTE: routine returns with no result or error if any parameter is invalid.

	public static void DSP_ChangePresetValue(int idsp, int channel, int iproc, float value) {

		Dsp pdsp;
		Pset? ppset;        // preset
		DspProcessor? pfnMod;   // modulation function

		if (idsp < 0 || idsp >= CDSPS)
			return;

		if (channel >= DSPCHANMAX)
			return;

		if (iproc >= CPSET_PRCS)
			return;

		// get ptr to processor preset

		pdsp = dsps[idsp];

		// assert that this dsp processor has enough separate channels

		Assert(channel <= pdsp.cchan);

		ppset = pdsp.GetPset(channel);

		if (ppset == null)
			return;

		// get ptr to modulation function

		pfnMod = Prcs(ppset)[iproc].pdata;

		if (pfnMod == null)
			return;

		// call modulation function with new value

		pfnMod.Mod(value);
	}


	const int DSP_AUTOMATIC = 1;        // corresponds to Generic preset

	// if dsp_room == DSP_AUTOMATIC, then use dsp_automatic value for dsp
	// any subsequent reset of dsp_room will disable automatic room detection.

	// return true if automatic room detection is enabled

	public static bool DSP_CheckDspAutoEnabled() {
		return dsp_room.GetInt() == DSP_AUTOMATIC;
	}

	// set dsp_automatic preset, used in place of dsp_room when automatic room detection enabled

	public static void DSP_SetDspAuto(int dsp_preset) {
		// set dsp_preset into dsp_automatic

		dsp_automatic.SetValue(dsp_preset);
	}

	// wrapper on dsp_room GetInt so that dsp_automatic can override

	public static int dsp_room_GetInt() {
		// if dsp_automatic is not enabled, get room

		if (!DSP_CheckDspAutoEnabled())
			return dsp_room.GetInt();

		// automatic room detection is on, get dsp_automatic instead of dsp_room

		return dsp_automatic.GetInt();
	}

	// wrapper on idsp_room preset so that idsp_automatic can override

	public static int Get_idsp_room() {

		// if dsp_automatic is not enabled, get room

		if (!DSP_CheckDspAutoEnabled())
			return idsp_room;

		// automatic room detection is on, return dsp_automatic preset instead of dsp_room preset

		return idsp_automatic;
	}


	// free previous preset if not 0

	static void DSP_FreePrevPreset(Dsp pdsp) {
		// free previous presets if non-null - ie: rapid change of preset just kills old without xfade

		if (pdsp.ipsetprev != 0) {
			for (int i = 0; i < pdsp.cchan; i++) {
				if (pdsp.GetPsetPrev(i) != null) {
					PSET_Free(pdsp.GetPsetPrev(i));
					pdsp.SetPsetPrev(i, null);
				}
			}

			pdsp.ipsetprev = 0;
		}

	}

	// alloc new preset if different from current
	//		xfade from prev to new preset
	//		free previous preset, copy current into previous, set up xfade from previous to new

	public static void DSP_SetPreset(int idsp, int ipsetnew) {
		Dsp pdsp;
		Pset?[] ppsetnew = new Pset?[DSPCHANMAX];

		Assert(idsp >= 0 && idsp < CDSPS);

		pdsp = dsps[idsp];

		// validate new preset range

		if (ipsetnew >= g_cpsettemplates || ipsetnew < 0)
			return;

		// ignore if new preset is same as current preset

		if (ipsetnew == pdsp.ipset)
			return;

		// alloc new presets (each channel is a duplicate preset)

		Assert(pdsp.cchan <= DSPCHANMAX);

		for (int i = 0; i < pdsp.cchan; i++) {
			ppsetnew[i] = PSET_Alloc(ipsetnew);
			if (ppsetnew[i] == null) {
				DevMsg("WARNING: DSP preset failed to allocate.\n");
				return;
			}
		}

		Assert(pdsp != null);

		// free PREVIOUS previous preset if not 0

		DSP_FreePrevPreset(pdsp);

		for (int i = 0; i < pdsp.cchan; i++) {
			// current becomes previous

			pdsp.SetPsetPrev(i, pdsp.GetPset(i));

			// new becomes current

			pdsp.SetPset(i, ppsetnew[i]);
		}

		pdsp.ipsetprev = pdsp.ipset;
		pdsp.ipset = ipsetnew;

		if (idsp == idsp_room || idsp == idsp_automatic) {
			// set up new dsp mix min & max, db_min & db_drop params so that new channels get new mix values

			// NOTE: only new sounds will get the new mix min/max values set in their dspmix param
			// NOTE: so - no crossfade is needed between dspmix and dspmix prev, but this also means
			// NOTE: that currently playing ambients will not see changes to dspmix at all.

			float mix_min = pdsp.GetPset(0).mix_min;
			float mix_max = pdsp.GetPset(0).mix_max;
			float db_min = pdsp.GetPset(0).db_min;
			float db_mixdrop = pdsp.GetPset(0).db_mixdrop;

			dsp_mix_min.SetValue(mix_min);
			dsp_mix_max.SetValue(mix_max);
			dsp_db_min.SetValue(db_min);
			dsp_db_mixdrop.SetValue(db_mixdrop);
		}

		RMP_SetEnd(ref pdsp.xramp);

		// make sure previous dsp preset has data

		Assert(pdsp.GetPsetPrev(0) != null);

		// shouldn't be crossfading if current dsp preset == previous dsp preset

		Assert(pdsp.ipset != pdsp.ipsetprev);

		// if new preset is one-shot, keep previous preset to restore when one-shot times out
		// but: don't restore previous one-shots!

		pdsp.ipsetsav_oneshot = 0;

		if (PSET_IsOneShot(pdsp.GetPset(0)) && !PSET_IsOneShot(pdsp.GetPsetPrev(0)))
			pdsp.ipsetsav_oneshot = pdsp.ipsetprev;

		// get new xfade time from previous preset (ie: fade out time). if 0 use default. if < 0, use exponential xfade

		if (MathF.Abs(pdsp.GetPsetPrev(0).fade) > 0.0F) {
			pdsp.xfade = MathF.Abs(pdsp.GetPsetPrev(0).fade);
			pdsp.bexpfade = pdsp.GetPsetPrev(0).fade < 0;
		}
		else {
			// no previous preset - use defauts, set in DSP_Alloc

			pdsp.xfade = pdsp.xfade_default;
			pdsp.bexpfade = false;
		}

		RMP_Init(ref pdsp.xramp, pdsp.xfade, 0, PMAX, false);
	}


	const int DSP_AUTO_BASE = 60;       // presets 60-100 in g_psettemplates are reserved as autocreated presets
	const int DSP_CAUTO_PRESETS = 40;   // must be same as DAS_CNODES!!!

	// construct a dsp preset based on provided parameters,
	// preset is constructed within g_psettemplates[] array.
	// return preset #

	// select type 1..5 based on params
	// 1:simple reverb
	// 2:diffusor + reverb
	// 3:diffusor + delay + reverb
	// 4:simple delay
	// 5:diffusor + delay

	const double AROOM_SMALL = 10.0 * 12.0;     // small room
	const double AROOM_MEDIUM = 20.0 * 12.0;        // medium room
	const double AROOM_LARGE = 40.0 * 12.0;     // large room
	const double AROOM_HUGE = 100.0 * 12.0;     // huge room
	const double AROOM_GIGANTIC = 200.0 * 12.0;     // gigantic room

	const double AROOM_DUCT_WIDTH = 4.0 * 12.0;     // max width for duct
	const double AROOM_DUCT_HEIGHT = 6.0 * 12.0;

	const double AROOM_HALL_WIDTH = 8.0 * 12.0;     // max width for hall
	const double AROOM_HALL_HEIGHT = 16.0 * 12.0;       // max height for hall

	const double AROOM_TUNNEL_WIDTH = 20.0 * 12.0;      // max width for tunnel
	const double AROOM_TUNNEL_HEIGHT = 30.0 * 12.0;     // max height for tunnel

	const double AROOM_STREET_WIDTH = 12.0 * 12.0;      // min width for street

	const double AROOM_SHORT_LENGTH = 12.0 * 12.0;      // max length for short hall
	const double AROOM_MEDIUM_LENGTH = 24.0 * 12.0;     // min length for medium hall
	const double AROOM_LONG_LENGTH = 48.0 * 12.0;       // min length for long hall
	const double AROOM_VLONG_LENGTH = 96.0 * 12.0;      // min length for very long hall
	const double AROOM_XLONG_LENGTH = 192.0 * 12.0;     // min length for huge hall

	const double AROOM_LOW_HEIGHT = 4.0 * 12.0;     // short ceiling
	const double AROOM_MEDIUM_HEIGHT = 128;             // medium ceiling
	const double AROOM_TALL_HEIGHT = 18.0 * 12.0;       // tall ceiling
	const double AROOM_VTALL_HEIGHT = 32.0 * 12.0;      // very tall ceiling
	const double AROOM_XTALL_HEIGHT = 64.0 * 12.0;      // huge tall ceiling

	const double AROOM_NARROW_WIDTH = 6.0 * 12.0;       // narrow width
	const double AROOM_MEDIUM_WIDTH = 12.0 * 12.0;      // medium width
	const double AROOM_WIDE_WIDTH = 24.0 * 12.0;        // wide width
	const double AROOM_VWIDE_WIDTH = 48.0 * 12.0;       // very wide
	const double AROOM_XWIDE_WIDTH = 96.0 * 12.0;       // huge width

	static bool BETWEEN(double a, double b, double c) => (a > b) && (a <= c);

	static bool ADSP_IsShaft(in AutoParams pa) => pa.height > (3.0 * pa.length);
	static bool ADSP_IsRoom(in AutoParams pa) => pa.length <= (2.5 * pa.width);
	static bool ADSP_IsHall(in AutoParams pa) => (pa.length > (2.5 * pa.width)) && BETWEEN(pa.width, AROOM_DUCT_WIDTH, AROOM_HALL_WIDTH);
	static bool ADSP_IsTunnel(in AutoParams pa) => (pa.length > (4.0 * pa.width)) && (pa.width > AROOM_HALL_WIDTH);
	static bool ADSP_IsDuct(in AutoParams pa) => (pa.length > (4.0 * pa.width)) && (pa.width <= AROOM_DUCT_WIDTH);

	static bool ADSP_IsCourtyard(in AutoParams pa) => pa.length <= (2.5 * pa.width);
	static bool ADSP_IsAlley(in AutoParams pa) => (pa.length > (2.5 * pa.width)) && (pa.width <= AROOM_STREET_WIDTH);
	static bool ADSP_IsStreet(in AutoParams pa) => (pa.length > (2.5 * pa.width)) && (pa.width > AROOM_STREET_WIDTH);

	static bool ADSP_IsSmallRoom(in AutoParams pa) => pa.length <= AROOM_SMALL;
	static bool ADSP_IsMediumRoom(in AutoParams pa) => BETWEEN(pa.length, AROOM_SMALL, AROOM_MEDIUM); // && (BETWEEN(pa->width, AROOM_SMALL, AROOM_MEDIUM)))
	static bool ADSP_IsLargeRoom(in AutoParams pa) => BETWEEN(pa.length, AROOM_MEDIUM, AROOM_LARGE); // && BETWEEN(pa->width, AROOM_MEDIUM, AROOM_LARGE))
	static bool ADSP_IsHugeRoom(in AutoParams pa) => BETWEEN(pa.length, AROOM_LARGE, AROOM_HUGE); // && BETWEEN(pa->width, AROOM_LARGE, AROOM_HUGE))
	static bool ADSP_IsGiganticRoom(in AutoParams pa) => pa.length > AROOM_HUGE; // && (pa->width > AROOM_HUGE))

	static bool ADSP_IsShortLength(in AutoParams pa) => pa.length <= AROOM_SHORT_LENGTH;
	static bool ADSP_IsMediumLength(in AutoParams pa) => BETWEEN(pa.length, AROOM_SHORT_LENGTH, AROOM_MEDIUM_LENGTH);
	static bool ADSP_IsLongLength(in AutoParams pa) => BETWEEN(pa.length, AROOM_MEDIUM_LENGTH, AROOM_LONG_LENGTH);
	static bool ADSP_IsVLongLength(in AutoParams pa) => BETWEEN(pa.length, AROOM_LONG_LENGTH, AROOM_VLONG_LENGTH);
	static bool ADSP_IsXLongLength(in AutoParams pa) => pa.length > AROOM_VLONG_LENGTH;

	static bool ADSP_IsLowHeight(in AutoParams pa) => pa.height <= AROOM_LOW_HEIGHT;
	static bool ADSP_IsMediumHeight(in AutoParams pa) => BETWEEN(pa.height, AROOM_LOW_HEIGHT, AROOM_MEDIUM_HEIGHT);
	static bool ADSP_IsTallHeight(in AutoParams pa) => BETWEEN(pa.height, AROOM_MEDIUM_HEIGHT, AROOM_TALL_HEIGHT);
	static bool ADSP_IsVTallHeight(in AutoParams pa) => BETWEEN(pa.height, AROOM_TALL_HEIGHT, AROOM_VTALL_HEIGHT);
	static bool ADSP_IsXTallHeight(in AutoParams pa) => pa.height > AROOM_VTALL_HEIGHT;

	static bool ADSP_IsNarrowWidth(in AutoParams pa) => pa.width <= AROOM_NARROW_WIDTH;
	static bool ADSP_IsMediumWidth(in AutoParams pa) => BETWEEN(pa.width, AROOM_NARROW_WIDTH, AROOM_MEDIUM_WIDTH);
	static bool ADSP_IsWideWidth(in AutoParams pa) => BETWEEN(pa.width, AROOM_MEDIUM_WIDTH, AROOM_WIDE_WIDTH);
	static bool ADSP_IsVWideWidth(in AutoParams pa) => BETWEEN(pa.width, AROOM_WIDE_WIDTH, AROOM_VWIDE_WIDTH);
	static bool ADSP_IsXWideWidth(in AutoParams pa) => pa.width > AROOM_VWIDE_WIDTH;

	static bool ADSP_IsInside(in AutoParams pa) => !pa.bskyabove;

	// room diffusion

	const int ADSP_EMPTY = 0;
	const int ADSP_SPARSE = 1;
	const int ADSP_CLUTTERED = 2;
	const int ADSP_FULL = 3;
	const int ADSP_DIFFUSION_MAX = 4;

	const double AROOM_DIF_EMPTY = 0.01;    // 1% of space by volume is other objects
	const double AROOM_DIF_SPARSE = 0.1;        // 10% "
	const double AROOM_DIF_CLUTTERED = 0.3; // 30% "
	const double AROOM_DIF_FULL = 0.5;      // 50% "

	static bool ADSP_IsEmpty(in AutoParams pa) => pa.fdiffusion <= AROOM_DIF_EMPTY;
	static bool ADSP_IsSparse(in AutoParams pa) => BETWEEN(pa.fdiffusion, AROOM_DIF_EMPTY, AROOM_DIF_SPARSE);
	static bool ADSP_IsCluttered(in AutoParams pa) => BETWEEN(pa.fdiffusion, AROOM_DIF_SPARSE, AROOM_DIF_CLUTTERED);
	static bool ADSP_IsFull(in AutoParams pa) => pa.fdiffusion > AROOM_DIF_CLUTTERED;

	static bool ADSP_IsDiffuse(in AutoParams pa) => pa.diffusion > ADSP_SPARSE;

	// room acoustic reflectivity

	// tile									0.3  * 3.3 = 0.99
	// metal								0.25 * 3.3 = 0.83
	// concrete,rock,brick,glass,gravel		0.2  * 3.3 = 0.66
	// metal panel/vent, wood, water		0.1	 * 3.3 = 0.33
	// carpet,sand,snow,dirt				0.01 * 3.3 = 0.03

	const int ADSP_DULL = 0;
	const int ADSP_FLAT = 1;
	const int ADSP_REFLECTIVE = 2;
	const int ADSP_BRIGHT = 3;
	const int ADSP_REFLECTIVITY_MAX = 4;

	const double AROOM_REF_DULL = 0.04;
	const double AROOM_REF_FLAT = 0.50;
	const double AROOM_REF_REFLECTIVE = 0.80;
	const double AROOM_REF_BRIGHT = 0.99;

	static bool ADSP_IsDull(in AutoParams pa) => pa.freflectivity <= AROOM_REF_DULL;
	static bool ADSP_IsFlat(in AutoParams pa) => BETWEEN(pa.freflectivity, AROOM_REF_DULL, AROOM_REF_FLAT);
	static bool ADSP_IsReflective(in AutoParams pa) => BETWEEN(pa.freflectivity, AROOM_REF_FLAT, AROOM_REF_REFLECTIVE);
	static bool ADSP_IsBright(in AutoParams pa) => pa.freflectivity > AROOM_REF_REFLECTIVE;

	static bool ADSP_IsRefl(in AutoParams pa) => pa.reflectivity > ADSP_FLAT;

	// room shapes

	const int ADSP_ROOM = 0;
	const int ADSP_DUCT = 1;
	const int ADSP_HALL = 2;
	const int ADSP_TUNNEL = 3;
	const int ADSP_STREET = 4;
	const int ADSP_ALLEY = 5;
	const int ADSP_COURTYARD = 6;
	const int ADSP_OPEN_SPACE = 7;      // NOTE: 7..10 must remain in order !!!
	const int ADSP_OPEN_WALL = 8;
	const int ADSP_OPEN_STREET = 9;
	const int ADSP_OPEN_COURTYARD = 10;

	// room sizes

	const int ADSP_SIZE_SMALL = 0;      // NOTE: must remain 0..4!!!
	const int ADSP_SIZE_MEDIUM = 1;
	const int ADSP_SIZE_LARGE = 2;
	const int ADSP_SIZE_HUGE = 3;
	const int ADSP_SIZE_GIGANTIC = 4;
	const int ADSP_SIZE_MAX = 5;

	const int ADSP_LENGTH_SHORT = 0;
	const int ADSP_LENGTH_MEDIUM = 1;
	const int ADSP_LENGTH_LONG = 2;
	const int ADSP_LENGTH_VLONG = 3;
	const int ADSP_LENGTH_XLONG = 4;
	const int ADSP_LENGTH_MAX = 5;

	const int ADSP_WIDTH_NARROW = 0;
	const int ADSP_WIDTH_MEDIUM = 1;
	const int ADSP_WIDTH_WIDE = 2;
	const int ADSP_WIDTH_VWIDE = 3;
	const int ADSP_WIDTH_XWIDE = 4;
	const int ADSP_WIDTH_MAX = 5;

	const int ADSP_HEIGHT_LOW = 0;
	const int ADSP_HEIGTH_MEDIUM = 1;
	const int ADSP_HEIGHT_TALL = 2;
	const int ADSP_HEIGHT_VTALL = 3;
	const int ADSP_HEIGHT_XTALL = 4;
	const int ADSP_HEIGHT_MAX = 5;


	// convert numeric size params to #defined size params

	static void ADSP_GetSize(ref AutoParams pa) {
		pa.size = ((ADSP_IsSmallRoom(pa) ? 1 : 0) * ADSP_SIZE_SMALL) +
					((ADSP_IsMediumRoom(pa) ? 1 : 0) * ADSP_SIZE_MEDIUM) +
					((ADSP_IsLargeRoom(pa) ? 1 : 0) * ADSP_SIZE_LARGE) +
					((ADSP_IsHugeRoom(pa) ? 1 : 0) * ADSP_SIZE_HUGE) +
					((ADSP_IsGiganticRoom(pa) ? 1 : 0) * ADSP_SIZE_GIGANTIC);

		pa.len = ((ADSP_IsShortLength(pa) ? 1 : 0) * ADSP_LENGTH_SHORT) +
					((ADSP_IsMediumLength(pa) ? 1 : 0) * ADSP_LENGTH_MEDIUM) +
					((ADSP_IsLongLength(pa) ? 1 : 0) * ADSP_LENGTH_LONG) +
					((ADSP_IsVLongLength(pa) ? 1 : 0) * ADSP_LENGTH_VLONG) +
					((ADSP_IsXLongLength(pa) ? 1 : 0) * ADSP_LENGTH_XLONG);

		pa.wid = ((ADSP_IsNarrowWidth(pa) ? 1 : 0) * ADSP_WIDTH_NARROW) +
					((ADSP_IsMediumWidth(pa) ? 1 : 0) * ADSP_WIDTH_MEDIUM) +
					((ADSP_IsWideWidth(pa) ? 1 : 0) * ADSP_WIDTH_WIDE) +
					((ADSP_IsVWideWidth(pa) ? 1 : 0) * ADSP_WIDTH_VWIDE) +
					((ADSP_IsXWideWidth(pa) ? 1 : 0) * ADSP_WIDTH_XWIDE);

		pa.ht = ((ADSP_IsLowHeight(pa) ? 1 : 0) * ADSP_HEIGHT_LOW) +
					((ADSP_IsMediumHeight(pa) ? 1 : 0) * ADSP_HEIGTH_MEDIUM) +
					((ADSP_IsTallHeight(pa) ? 1 : 0) * ADSP_HEIGHT_TALL) +
					((ADSP_IsVTallHeight(pa) ? 1 : 0) * ADSP_HEIGHT_VTALL) +
					((ADSP_IsXTallHeight(pa) ? 1 : 0) * ADSP_HEIGHT_XTALL);

		pa.reflectivity =
					((ADSP_IsDull(pa) ? 1 : 0) * ADSP_DULL) +
					((ADSP_IsFlat(pa) ? 1 : 0) * ADSP_FLAT) +
					((ADSP_IsReflective(pa) ? 1 : 0) * ADSP_REFLECTIVE) +
					((ADSP_IsBright(pa) ? 1 : 0) * ADSP_BRIGHT);

		pa.diffusion =
					((ADSP_IsEmpty(pa) ? 1 : 0) * ADSP_EMPTY) +
					((ADSP_IsSparse(pa) ? 1 : 0) * ADSP_SPARSE) +
					((ADSP_IsCluttered(pa) ? 1 : 0) * ADSP_CLUTTERED) +
					((ADSP_IsFull(pa) ? 1 : 0) * ADSP_FULL);

		Assert(pa.size < ADSP_SIZE_MAX);
		Assert(pa.len < ADSP_LENGTH_MAX);
		Assert(pa.wid < ADSP_WIDTH_MAX);
		Assert(pa.ht < ADSP_HEIGHT_MAX);
		Assert(pa.reflectivity < ADSP_REFLECTIVITY_MAX);
		Assert(pa.diffusion < ADSP_DIFFUSION_MAX);

		if (pa.shape != ADSP_COURTYARD && pa.shape != ADSP_OPEN_COURTYARD) {
			// fix up size for streets, alleys, halls, ducts, tunnelsy

			if (pa.shape == ADSP_STREET || pa.shape == ADSP_ALLEY)
				pa.size = pa.wid;
			else
				pa.size = (pa.len + pa.wid) / 2;

		}

	}

	static void ADSP_GetOutsideSize(ref AutoParams pa) {
		ADSP_GetSize(ref pa);
	}

	// return # of sides that had max length or sky hits (out of 6 sides).

	static int ADSP_COpenSides(in AutoParams pa) {
		int count = 0;

		// only look at left,right,front,back walls - ignore floor, ceiling

		for (int i = 0; i < 4; i++) {
			if (pa.surface_refl[i] == 0.0)
				count++;
		}

		return count;
	}

	// given auto params, return shape and size of room

	static void ADSP_GetAutoShape(ref AutoParams pa) {

		// INSIDE:
		// shapes: duct, hall, tunnel, shaft (vertical duct, hall or tunnel)
		//		sizes: short->long, narrow->wide, low->tall
		// shapes: room
		//		sizes: small->large, low->tall

		// OUTSIDE:
		// shapes: street, alley
		//		sizes: short->long, narrow->wide
		// shapes: courtyard
		//		sizes: small->large

		// shapes: open_space, wall, open_street, open_corner, open_courtyard
		//		sizes: open, narrow->wide

		bool bshaft = false;
		int t;

		if (ADSP_IsInside(pa)) {
			if (ADSP_IsShaft(pa)) {
				// temp swap height and length

				bshaft = true;
				t = pa.height;
				pa.height = pa.length;
				pa.length = t;
				if (das_debug.GetInt() > 1)
					DevMsg("VERTICAL SHAFT Detected \n");
			}

			// get shape

			if (ADSP_IsDuct(pa)) {
				pa.shape = ADSP_DUCT;
				ADSP_GetSize(ref pa);
				if (das_debug.GetInt() > 1)
					DevMsg("DUCT Detected \n");
				goto autoshape_exit;
			}

			if (ADSP_IsHall(pa)) {
				// get size
				pa.shape = ADSP_HALL;
				ADSP_GetSize(ref pa);

				if (das_debug.GetInt() > 1)
					DevMsg("HALL Detected \n");

				goto autoshape_exit;
			}

			if (ADSP_IsTunnel(pa)) {
				// get size
				pa.shape = ADSP_TUNNEL;
				ADSP_GetSize(ref pa);

				if (das_debug.GetInt() > 1)
					DevMsg("TUNNEL Detected \n");

				goto autoshape_exit;
			}

			// default
			// (ADSP_IsRoom(pa))
			{
				// get size
				pa.shape = ADSP_ROOM;
				ADSP_GetSize(ref pa);

				if (das_debug.GetInt() > 1)
					DevMsg("ROOM Detected \n");

				goto autoshape_exit;
			}
		}

		// outside:

		if (ADSP_COpenSides(pa) > 0)    // side hit sky, or side has max length
		{
			// get shape - courtyard, street, wall or open space
			// 10..7
			pa.shape = ADSP_OPEN_COURTYARD - (ADSP_COpenSides(pa) - 1);
			ADSP_GetOutsideSize(ref pa);

			if (das_debug.GetInt() > 1)
				DevMsg("OPEN SIDED OUTDOOR AREA Detected \n");

			goto autoshape_exit;
		}

		// all sides closed:

		// get shape - closed street or alley or courtyard

		if (ADSP_IsCourtyard(pa)) {
			pa.shape = ADSP_COURTYARD;
			ADSP_GetOutsideSize(ref pa);

			if (das_debug.GetInt() > 1)
				DevMsg("OUTSIDE COURTYARD Detected \n");

			goto autoshape_exit;
		}

		if (ADSP_IsAlley(pa)) {
			pa.shape = ADSP_ALLEY;
			ADSP_GetOutsideSize(ref pa);

			if (das_debug.GetInt() > 1)
				DevMsg("OUTSIDE ALLEY Detected \n");
			goto autoshape_exit;
		}

		// default to 'street' if sides are closed

		// if (ADSP_IsStreet(pa))
		{
			pa.shape = ADSP_STREET;
			ADSP_GetOutsideSize(ref pa);
			if (das_debug.GetInt() > 1)
				DevMsg("OUTSIDE STREET Detected \n");
			goto autoshape_exit;
		}

	autoshape_exit:

		// swap height & length if needed

		if (bshaft) {
			t = pa.height;
			pa.height = pa.length;
			pa.length = t;
		}
	}

	static readonly int[] MapReflectivityToDLYCutoff = [
		1000,   // DULL
		2000,   // FLAT
		4000,   // REFLECTIVE
		6000    // BRIGHT
	];

	static readonly float[] MapSizeToDLYFeedback = [
		0.9f, // 0.6,	// SMALL
		0.8f, // 0.5,	// MEDIUM
		0.7f, // 0.4,	// LARGE
		0.6f, // 0.3,	// HUGE
		0.5f, // 0.2,	// GIGANTIC
	];

	static void ADSP_SetupAutoDelay(ref Prc pprc_dly, in AutoParams pa) {
		// shapes:
		// inside: duct, long hall, long tunnel, large room
		// outside: open courtyard, street wall, space
		// outside: closed courtyard, alley, street

		// size 0..4
		// len 0..3
		// wid 0..3
		// reflectivity: 0..3
		// diffusion 0..3

		// dtype: delay type DLY_PLAIN, DLY_LOWPASS, DLY_ALLPASS
		// delay: delay in milliseconds (room max size in feet)
		// feedback: feedback 0-1.0
		// gain: final gain of output stage, 0-1.0

		int size = pa.length * 2;

		if (pa.shape == ADSP_ALLEY || pa.shape == ADSP_STREET || pa.shape == ADSP_OPEN_STREET)
			size = pa.width * 2;

		pprc_dly.type = PRC_DLY;

		pprc_dly.prm[dly_idtype] = DLY_LOWPASS;        // delay with feedback

		pprc_dly.prm[dly_idelay] = Math.Clamp(size / 12.0F, 5.0F, 500.0F);

		pprc_dly.prm[dly_ifeedback] = MapSizeToDLYFeedback[pa.len];

		// reduce gain based on distance reflection travels
		//	float g = 1.0 - ( clamp(pprc_dly->prm[dly_idelay], 10.0, 1000.0) / (1000.0 - 10.0) );
		//	pprc_dly->prm[dly_igain]		= g;

		pprc_dly.prm[dly_iftype] = FLT_LP;
		if (ADSP_IsInside(pa))
			pprc_dly.prm[dly_icutoff] = MapReflectivityToDLYCutoff[pa.reflectivity];
		else
			pprc_dly.prm[dly_icutoff] = (int)((float)MapReflectivityToDLYCutoff[pa.reflectivity] * 0.75);

		pprc_dly.prm[dly_iqwidth] = 0;

		pprc_dly.prm[dly_iquality] = QUA_LO;

		float l = Math.Clamp(pa.length * 2.0F / 12.0F, 14.0F, 500.0F);
		float w = Math.Clamp(pa.width * 2.0F / 12.0F, 14.0F, 500.0F);

		// convert to multitap delay

		pprc_dly.prm[dly_idtype] = DLY_LOWPASS_4TAP;

		pprc_dly.prm[dly_idelay] = l;
		pprc_dly.prm[dly_itap1] = w;
		pprc_dly.prm[dly_itap2] = l; // max(7, l * 0.7 );
		pprc_dly.prm[dly_itap3] = l; // max(7, w * 0.7 );

		pprc_dly.prm[dly_igain] = 1.0f;
	}

	static readonly int[] MapReflectivityToRVACutoff = [
		1000,   // DULL
		2000,   // FLAT
		4000,   // REFLECTIVE
		6000    // BRIGHT
	];

	static readonly float[] MapSizeToRVANumDelays = [
		3,  // SMALL	3 reverbs
		6,  // MEDIUM	6 reverbs
		6,  // LARGE	6 reverbs
		9,  // HUGE		9 reverbs
		12, // GIGANTIC	12 reverbs
	];

	static readonly float[] MapSizeToRVAFeedback = [
		0.75f,  // SMALL
		0.8f,   // MEDIUM
		0.9f,   // LARGE
		0.95f,  // HUGE
		0.98f,  // GIGANTIC
	];

	static void ADSP_SetupAutoReverb(ref Prc pprc_rva, in AutoParams pa) {
		// shape: hall, tunnel or room
		// size 0..4
		// reflectivity: 0..3
		// diffusion 0..3

		// size: 0-2.0 scales nominal delay parameters (18 to 47 ms * scale = delay)
		// numdelays: 0-12 controls # of parallel or series delays
		// decay: 0-2.0 scales feedback parameters (.7 to .9 * scale/2.0 = feedback)
		// fparallel: if true, filters are built into delays, otherwise filter output only
		// fmoddly: if true, all delays are modulating delays
		float gain = 1.0f;

		pprc_rva.type = PRC_RVA;

		pprc_rva.prm[rva_size_max] = 50.0f;
		pprc_rva.prm[rva_size_min] = 30.0f;

		if (ADSP_IsRoom(pa))
			pprc_rva.prm[rva_inumdelays] = MapSizeToRVANumDelays[pa.size];
		else
			pprc_rva.prm[rva_inumdelays] = MapSizeToRVANumDelays[pa.len];

		pprc_rva.prm[rva_ifeedback] = 0.9f;

		pprc_rva.prm[rva_icutoff] = MapReflectivityToRVACutoff[pa.reflectivity];

		pprc_rva.prm[rva_ifparallel] = 1;
		pprc_rva.prm[rva_imoddly] = ADSP_IsEmpty(pa) ? 0 : 4;
		pprc_rva.prm[rva_imodrate] = 3.48f;

		pprc_rva.prm[rva_iftaps] = 0;  // 0.1 // use extra delay taps to increase density

		pprc_rva.prm[rva_width] = Math.Clamp((float)pa.width / 12.0F, 6.0F, 500.0F);    // in feet
		pprc_rva.prm[rva_depth] = Math.Clamp((float)pa.length / 12.0F, 6.0F, 500.0F);
		pprc_rva.prm[rva_height] = Math.Clamp((float)pa.height / 12.0F, 6.0F, 500.0F);

		// room
		pprc_rva.prm[rva_fbwidth] = 0.9f; // MapSizeToRVAFeedback[pa->size];	// larger size = more feedback
		pprc_rva.prm[rva_fbdepth] = 0.9f; // MapSizeToRVAFeedback[pa->size];
		pprc_rva.prm[rva_fbheight] = 0.5f; // MapSizeToRVAFeedback[pa->size];

		// feedback is based on size of room:

		if (ADSP_IsInside(pa)) {
			if (pa.shape == ADSP_HALL) {
				pprc_rva.prm[rva_fbwidth] = 0.7f; //MapSizeToRVAFeedback[pa->wid];
				pprc_rva.prm[rva_fbdepth] = -0.5f; //MapSizeToRVAFeedback[pa->len];
				pprc_rva.prm[rva_fbheight] = 0.3f; //MapSizeToRVAFeedback[pa->ht];
			}

			if (pa.shape == ADSP_TUNNEL) {
				pprc_rva.prm[rva_fbwidth] = 0.9f;
				pprc_rva.prm[rva_fbdepth] = -0.8f; // fixed pre-delay, no feedback
				pprc_rva.prm[rva_fbheight] = 0.3f;
			}
		}
		else {
			if (pa.shape == ADSP_ALLEY) {
				pprc_rva.prm[rva_fbwidth] = 0.9f;
				pprc_rva.prm[rva_fbdepth] = -0.8f; // fixed pre-delay, no feedback
				pprc_rva.prm[rva_fbheight] = 0.0f;
			}
		}

		if (!ADSP_IsInside(pa))
			pprc_rva.prm[rva_fbheight] = 0.0f;

		pprc_rva.prm[rva_igain] = gain;
	}

	// return index to processor given processor type and preset
	// skips N processors of similar type
	// returns -1 if type not found

	static int ADSP_FindProc(Pset ppset, int proc_type, int skip) {
		int skipcount = skip;

		for (int i = 0; i < ppset.cprcs; i++) {
			// look for match on processor type

			if (Prcs(ppset)[i].type == proc_type) {
				// skip first N procs of similar type,

				// return index to processor

				if (skipcount == 0)
					return i;

				skipcount--;
			}

		}

		return -1;
	}

	// interpolate parameter:
	// pnew - target preset
	// pmin - preset with parameter with min value
	// pmax - preset with parameter with max value
	// proc_type - type of processor to look for ie: PRC_RVA or PRC_DLY
	// skipprocs - skip n processors of type
	// iparam - which parameter within processor to interpolate
	// index -
	// index_max:  use index/index_max as interpolater between pmin param and pmax param
	// if bexp is true, interpolate exponentially as (index/index_max)^2

	// NOTE: returns with no result if processor type is not found in all presets.

	static void ADSP_InterpParam(Pset pnew, Pset pmin, Pset pmax, int proc_type, int skipprocs, int iparam, int index, int index_max, bool bexp) {
		// find processor index in pnew
		int iproc_new = ADSP_FindProc(pnew, proc_type, skipprocs);
		int iproc_min = ADSP_FindProc(pmin, proc_type, skipprocs);
		int iproc_max = ADSP_FindProc(pmax, proc_type, skipprocs);

		// make sure processor type found in all presets

		if (iproc_new < 0 || iproc_min < 0 || iproc_max < 0)
			return;

		float findex = (float)index / (float)index_max;
		float vmin = Prcs(pmin)[iproc_min].prm[iparam];
		float vmax = Prcs(pmax)[iproc_max].prm[iparam];
		float vinterp;

		// interpolate

		if (!bexp)
			vinterp = vmin + (vmax - vmin) * findex;
		else
			vinterp = vmin + (vmax - vmin) * findex * findex;

		Prcs(pnew)[iproc_new].prm[iparam] = vinterp;

		return;
	}

	// directly set parameter

	static void ADSP_SetParam(Pset pnew, int proc_type, int skipprocs, int iparam, float value) {
		int iproc_new = ADSP_FindProc(pnew, proc_type, skipprocs);

		if (iproc_new >= 0)
			Prcs(pnew)[iproc_new].prm[iparam] = value;
	}

	// directly set parameter if min or max is negative

	static void ADSP_SetParamIfNegative(Pset pnew, Pset pmin, Pset pmax, int proc_type, int skipprocs, int iparam, int index, int index_max, bool bexp, float value) {
		// find processor index in pnew
		int iproc_new = ADSP_FindProc(pnew, proc_type, skipprocs);
		int iproc_min = ADSP_FindProc(pmin, proc_type, skipprocs);
		int iproc_max = ADSP_FindProc(pmax, proc_type, skipprocs);

		// make sure processor type found in all presets

		if (iproc_new < 0 || iproc_min < 0 || iproc_max < 0)
			return;

		float vmin = Prcs(pmin)[iproc_min].prm[iparam];
		float vmax = Prcs(pmax)[iproc_max].prm[iparam];

		if (vmin < 0.0 || vmax < 0.0)
			ADSP_SetParam(pnew, proc_type, skipprocs, iparam, value);
		else
			ADSP_InterpParam(pnew, pmin, pmax, proc_type, skipprocs, iparam, index, index_max, bexp);

		return;
	}

	// given min and max preset and auto parameters, create new preset
	// NOTE: the # and type of processors making up pmin and pmax presets must be identical!

	static void ADSP_InterpolatePreset(Pset pnew, Pset pmin, Pset pmax, ref AutoParams pa, int iskip) {
		int i;

		// if size > mid size, then copy basic processors from MAX preset,
		// otherwise, copy from MIN preset

		if (iskip == 0) {
			// only copy on 1st call

			if (pa.size > ADSP_SIZE_MEDIUM)
				pnew.CopyFrom(pmax);
			else
				pnew.CopyFrom(pmin);
		}

		// DFR

		// interpolate all DFR params on size

		for (i = 0; i < dfr_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_DFR, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		// RVA

		// interpolate size_max, size_min, feedback, #delays, moddly, imodrate, based on ap size

		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_ifeedback, pa.size, ADSP_SIZE_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_size_min, pa.size, ADSP_SIZE_MAX, true);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_size_max, pa.size, ADSP_SIZE_MAX, true);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_igain, pa.size, ADSP_SIZE_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_inumdelays, pa.size, ADSP_SIZE_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_imoddly, pa.size, ADSP_SIZE_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_imodrate, pa.size, ADSP_SIZE_MAX, false);

		// interpolate width,depth,height based on ap width length & height - exponential interpolation
		// if pmin or pmax parameters are < 0, directly set value from w/l/h

		float w = Math.Clamp((float)pa.width / 12.0F, 6.0F, 500.0F);   // in feet
		float l = Math.Clamp((float)pa.length / 12.0F, 6.0F, 500.0F);
		float h = Math.Clamp((float)pa.height / 12.0F, 6.0F, 500.0F);

		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_RVA, iskip, rva_width, pa.wid, ADSP_WIDTH_MAX, true, w);
		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_RVA, iskip, rva_depth, pa.len, ADSP_LENGTH_MAX, true, l);
		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_RVA, iskip, rva_height, pa.ht, ADSP_HEIGHT_MAX, true, h);

		// interpolate w/d/h feedback based on ap w/d/f

		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_fbwidth, pa.wid, ADSP_WIDTH_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_fbdepth, pa.len, ADSP_LENGTH_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_RVA, iskip, rva_fbheight, pa.ht, ADSP_HEIGHT_MAX, false);

		// interpolate cutoff based on ap reflectivity
		// NOTE: cutoff goes from max to min! ie: small bright - large dull

		ADSP_InterpParam(pnew, pmax, pmin, PRC_RVA, iskip, rva_icutoff, pa.reflectivity, ADSP_REFLECTIVITY_MAX, false);

		// don't interpolate: fparallel, ftaps

		// DLY

		// directly set delay value from pa->length if pmin or pmax value is < 0

		l = Math.Clamp(pa.length * 2.0F / 12.0F, 14.0F, 500.0F);
		w = Math.Clamp(pa.width * 2.0F / 12.0F, 14.0F, 500.0F);

		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_DLY, iskip, dly_idelay, pa.len, ADSP_LENGTH_MAX, true, l);

		// interpolate feedback, gain, based on max size (length)

		ADSP_InterpParam(pnew, pmin, pmax, PRC_DLY, iskip, dly_ifeedback, pa.len, ADSP_LENGTH_MAX, false);
		ADSP_InterpParam(pnew, pmin, pmax, PRC_DLY, iskip, dly_igain, pa.len, ADSP_LENGTH_MAX, false);

		// directly set tap value from pa->width if pmin or pmax value is < 0

		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_DLY, iskip, dly_itap1, pa.len, ADSP_LENGTH_MAX, true, w);
		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_DLY, iskip, dly_itap2, pa.len, ADSP_LENGTH_MAX, true, l);
		ADSP_SetParamIfNegative(pnew, pmin, pmax, PRC_DLY, iskip, dly_itap3, pa.len, ADSP_LENGTH_MAX, true, l);

		// interpolate cutoff and qwidth based on reflectivity NOTE: this can affect gain!
		// NOTE: cutoff goes from max to min! ie: small bright - large dull

		ADSP_InterpParam(pnew, pmax, pmin, PRC_DLY, iskip, dly_icutoff, pa.len, ADSP_LENGTH_MAX, false);
		ADSP_InterpParam(pnew, pmax, pmin, PRC_DLY, iskip, dly_iqwidth, pa.len, ADSP_LENGTH_MAX, false);

		// interpolate all other parameters for all other processor types based on size

		// PRC_MDY, PRC_AMP, PRC_FLT, PTC, CRS, ENV, EFO, LFO

		for (i = 0; i < mdy_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_MDY, iskip, i, pa.len, ADSP_LENGTH_MAX, false);

		for (i = 0; i < amp_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_AMP, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < flt_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_FLT, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < ptc_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_PTC, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < crs_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_CRS, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < env_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_ENV, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < efo_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_EFO, iskip, i, pa.size, ADSP_SIZE_MAX, false);

		for (i = 0; i < lfo_cparam; i++)
			ADSP_InterpParam(pnew, pmin, pmax, PRC_LFO, iskip, i, pa.size, ADSP_SIZE_MAX, false);

	}

	// these convars store the index to the first preset for each shape type in dsp_presets.txt

	public static readonly ConVar adsp_room_min = new("adsp_room_min", "102");
	public static readonly ConVar adsp_duct_min = new("adsp_duct_min", "106");
	public static readonly ConVar adsp_hall_min = new("adsp_hall_min", "110");
	public static readonly ConVar adsp_tunnel_min = new("adsp_tunnel_min", "114");
	public static readonly ConVar adsp_street_min = new("adsp_street_min", "118");
	public static readonly ConVar adsp_alley_min = new("adsp_alley_min", "122");
	public static readonly ConVar adsp_courtyard_min = new("adsp_courtyard_min", "126");
	public static readonly ConVar adsp_openspace_min = new("adsp_openspace_min", "130");
	public static readonly ConVar adsp_openwall_min = new("adsp_openwall_min", "130");
	public static readonly ConVar adsp_openstreet_min = new("adsp_openstreet_min", "118");
	public static readonly ConVar adsp_opencourtyard_min = new("adsp_opencourtyard_min", "126");

	// given room parameters, construct and return a dsp preset representing the room.
	// bskyabove, width, length, height, fdiffusion, freflectivity are all passed-in room parameters
	// psurf_refl is a passed-in array of reflectivity values for 6 surfaces
	// inode is the location within g_psettemplates[] that the dsp preset will be constructed (inode = dsp preset#)
	// cnode should always = DSP_CAUTO_PRESETS
	// returns idsp preset.

	public static int DSP_ConstructPreset(bool bskyabove, int width, int length, int height, float fdiffusion, float freflectivity, ReadOnlySpan<float> psurf_refl, int inode, int cnodes) {
		AutoParams ap;

		Pset new_pset;  // preset
		Pset pset_min;
		Pset pset_max;

		int ipreset;
		int ipset_min;
		int ipset_max;

		if (inode >= DSP_CAUTO_PRESETS) {
			Assert(false);  // check DAS_CNODES == DSP_CAUTO_PRESETS!!!
			return 0;
		}

		// fill parameter struct

		ap = default;
		ap.bskyabove = bskyabove;
		ap.width = width;
		ap.length = length;
		ap.height = height;
		ap.fdiffusion = fdiffusion;
		ap.freflectivity = freflectivity;

		for (int i = 0; i < 6; i++)
			ap.surface_refl[i] = psurf_refl[i];

		if (ap.bskyabove)
			ap.surface_refl[4] = 0.0f;

		// select shape, size based on params

		ADSP_GetAutoShape(ref ap);

		// set up min/max presets based on shape

		switch (ap.shape) {
			default:
			case ADSP_ROOM: ipset_min = adsp_room_min.GetInt(); break;
			case ADSP_DUCT: ipset_min = adsp_duct_min.GetInt(); break;
			case ADSP_HALL: ipset_min = adsp_hall_min.GetInt(); break;
			case ADSP_TUNNEL: ipset_min = adsp_tunnel_min.GetInt(); break;
			case ADSP_STREET: ipset_min = adsp_street_min.GetInt(); break;
			case ADSP_ALLEY: ipset_min = adsp_alley_min.GetInt(); break;
			case ADSP_COURTYARD: ipset_min = adsp_courtyard_min.GetInt(); break;
			case ADSP_OPEN_SPACE: ipset_min = adsp_openspace_min.GetInt(); break;
			case ADSP_OPEN_WALL: ipset_min = adsp_openwall_min.GetInt(); break;
			case ADSP_OPEN_STREET: ipset_min = adsp_openstreet_min.GetInt(); break;
			case ADSP_OPEN_COURTYARD: ipset_min = adsp_opencourtyard_min.GetInt(); break;
		}

		// presets in dsp_presets.txt are ordered as:

		// <shape><empty><min>
		// <shape><empty><max>
		// <shape><diffuse><min>
		// <shape><diffuse><max>
		if (ADSP_IsDiffuse(ap))
			ipset_min += 2;

		ipset_max = ipset_min + 1;

		pset_min = g_psettemplates![ipset_min];
		pset_max = g_psettemplates[ipset_max];

		// given min and max preset and auto parameters, create new preset

		// interpolate between 1st instances of each processor type (ie: PRC_DLY) appearing in preset

		new_pset = new();
		ADSP_InterpolatePreset(new_pset, pset_min, pset_max, ref ap, 0);

		// interpolate between 2nd instances of each processor type (ie: PRC_DLY) appearing in preset

		ADSP_InterpolatePreset(new_pset, pset_min, pset_max, ref ap, 1);

		// copy constructed preset back into node's template location

		ipreset = DSP_AUTO_BASE + inode;

		g_psettemplates[ipreset] = new_pset;

		return ipreset;
	}

	///////////////////////////////////////
	// Helpers: called only from DSP_Process
	///////////////////////////////////////


	// return true if batch processing version of preset exists

	static bool FBatchPreset(Pset ppset) {

		switch (ppset.type) {
			case PSET_LINEAR:
				return true;
			case PSET_SIMPLE:
				return true;
			default:
				return false;
		}
	}

	// Helper: called only from DSP_Process
	// mix front stereo buffer to mono buffer, apply dsp fx

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_ProcessStereoToMono(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		int count = sampleCount;
		int ib = 0;
		int av;
		int x;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			if (FBatchPreset(pdsp.GetPset(0))) {
				// convert Stereo to Mono in place, then batch process fx: perf KDB

				// front->left + front->right / 2 into front->left, front->right duplicated.

				while (count-- != 0) {
					pbf[ib].Left = (pbf[ib].Left + pbf[ib].Right) >> 1;
					ib++;
				}

				// process left (mono), duplicate output into right

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT_DUPLICATE);
			}
			else {
				// avg left and right -> mono fx -> duplcate out left and right
				while (count-- != 0) {
					av = (pbf[ib].Left + pbf[ib].Right) >> 1;
					x = PSET_GetNext(pdsp.GetPset(0), av);
					x = CLIP_DSP(x);
					pbf[ib].Left = pbf[ib].Right = x;
					ib++;
				}
			}
			return;
		}

		// crossfading to current preset from previous preset

		{
			int r;
			int fl;
			int fr;
			int flp;
			int frp;
			int xf_fl;
			int xf_fr;
			bool bexp = pdsp.bexpfade;
			bool bfadetostereo = pdsp.ipset == 0;
			bool bfadefromstereo = pdsp.ipsetprev == 0;

			Assert(!(bfadetostereo && bfadefromstereo));    // don't call if ipset & ipsetprev both 0!

			if (bfadetostereo || bfadefromstereo) {
				// special case if fading to or from preset 0, stereo passthrough

				while (count-- != 0) {
					av = (pbf[ib].Left + pbf[ib].Right) >> 1;

					// get current preset values

					if (pdsp.ipset != 0)
						fl = fr = PSET_GetNext(pdsp.GetPset(0), av);
					else {
						fl = pbf[ib].Left;
						fr = pbf[ib].Right;
					}

					// get previous preset values

					if (pdsp.ipsetprev != 0)
						frp = flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);
					else {
						flp = pbf[ib].Left;
						frp = pbf[ib].Right;
					}

					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);
					flp = CLIP_DSP(flp);
					frp = CLIP_DSP(frp);

					// get current ramp value

					r = RMP_GetNext(ref pdsp.xramp);

					// crossfade from previous to current preset

					if (!bexp) {
						xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE(fr, frp, r);  // crossfade front left previous to front left
					}
					else {
						xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE_EXP(fr, frp, r);  // crossfade front left previous to front left
					}

					pbf[ib].Left = xf_fl;          // crossfaded front left, duplicate in right channel
					pbf[ib].Right = xf_fr;

					ib++;
				}

				return;
			}

			// crossfade mono to mono preset

			while (count-- != 0) {
				av = (pbf[ib].Left + pbf[ib].Right) >> 1;

				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), av);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);

				fl = CLIP_DSP(fl);
				flp = CLIP_DSP(flp);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				// crossfade from previous to current preset

				if (!bexp)
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
				else
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left

				pbf[ib].Left = xf_fl;          // crossfaded front left, duplicate in right channel
				pbf[ib].Right = xf_fl;

				ib++;
			}
		}
	}

	// Helper: called only from DSP_Process
	// DSP_Process stereo in to stereo out (if more than 2 procs, ignore them)

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_ProcessStereoToStereo(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		int count = sampleCount;
		int ib = 0;
		int fl, fr;

		if (!bcrossfading) {

			if (pdsp.ipset == 0)
				return;

			if (FBatchPreset(pdsp.GetPset(0)) && FBatchPreset(pdsp.GetPset(1))) {

				// process left & right

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(1), pbfront, sampleCount, OP_RIGHT);
			}
			else {
				// left -> left fx, right -> right fx
				while (count-- != 0) {
					fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
					fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);

					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);

					pbf[ib].Left = fl;
					pbf[ib].Right = fr;
					ib++;
				}
			}
			return;
		}

		// crossfading to current preset from previous preset

		{
			int r;
			int flp, frp;
			int xf_fl, xf_fr;
			bool bexp = pdsp.bexpfade;

			while (count-- != 0) {
				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
				fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), pbf[ib].Left);
				frp = PSET_GetNext(pdsp.GetPsetPrev(1), pbf[ib].Right);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				fl = CLIP_DSP(fl);
				fr = CLIP_DSP(fr);
				flp = CLIP_DSP(flp);
				frp = CLIP_DSP(frp);

				// crossfade from previous to current preset
				if (!bexp) {
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE(fr, frp, r);
				}
				else {
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE_EXP(fr, frp, r);
				}

				pbf[ib].Left = xf_fl;          // crossfaded front left
				pbf[ib].Right = xf_fr;

				ib++;
			}
		}
	}

	// Helper: called only from DSP_Process
	// DSP_Process quad in to mono out (front left = front right)

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_ProcessQuadToMono(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		Span<PortableSamplePair> pbr = pbrear;       // pointer to buffer of rear stereo samples to process
		int count = sampleCount;
		int ib = 0;
		int x;
		int av;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			if (FBatchPreset(pdsp.GetPset(0))) {

				// convert Quad to Mono in place, then batch process fx: perf KDB

				// left front + rear -> left, right front + rear -> right
				while (count-- != 0) {
					pbf[ib].Left = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right) >> 2;
					ib++;
				}

				// process left (mono), duplicate into right

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT_DUPLICATE);

				// copy processed front to rear

				count = sampleCount;

				ib = 0;

				while (count-- != 0) {
					pbr[ib].Left = pbf[ib].Left;
					pbr[ib].Right = pbf[ib].Right;
					ib++;
				}

			}
			else {
				// avg fl,fr,rl,rr into mono fx, duplicate on all channels
				while (count-- != 0) {
					av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right) >> 2;
					x = PSET_GetNext(pdsp.GetPset(0), av);
					x = CLIP_DSP(x);
					pbr[ib].Left = pbr[ib].Right = pbf[ib].Left = pbf[ib].Right = x;
					ib++;
				}
			}
			return;
		}

		{
			int r;
			int fl, fr, rl, rr;
			int flp, frp, rlp, rrp;
			int xf_fl, xf_fr, xf_rl, xf_rr;
			bool bexp = pdsp.bexpfade;
			bool bfadetoquad = pdsp.ipset == 0;
			bool bfadefromquad = pdsp.ipsetprev == 0;

			if (bfadetoquad || bfadefromquad) {
				// special case if previous or current preset is 0 (quad passthrough)

				while (count-- != 0) {
					av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right) >> 2;

					// get current preset values

					// current preset is 0, which implies fading to passthrough quad output
					// need to fade from mono to quad

					if (pdsp.ipset != 0)
						rl = rr = fl = fr = PSET_GetNext(pdsp.GetPset(0), av);
					else {
						fl = pbf[ib].Left;
						fr = pbf[ib].Right;
						rl = pbr[ib].Left;
						rr = pbr[ib].Right;
					}

					// get previous preset values

					if (pdsp.ipsetprev != 0)
						rrp = rlp = frp = flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);
					else {
						flp = pbf[ib].Left;
						frp = pbf[ib].Right;
						rlp = pbr[ib].Left;
						rrp = pbr[ib].Right;
					}

					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);
					flp = CLIP_DSP(flp);
					frp = CLIP_DSP(frp);
					rl = CLIP_DSP(rl);
					rr = CLIP_DSP(rr);
					rlp = CLIP_DSP(rlp);
					rrp = CLIP_DSP(rrp);

					// get current ramp value

					r = RMP_GetNext(ref pdsp.xramp);

					// crossfade from previous to current preset

					if (!bexp) {
						xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE(rr, rrp, r);  // crossfade front left previous to front left
					}
					else {
						xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE_EXP(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE_EXP(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE_EXP(rr, rrp, r);  // crossfade front left previous to front left
					}

					pbf[ib].Left = xf_fl;
					pbf[ib].Right = xf_fr;
					pbr[ib].Left = xf_rl;
					pbr[ib].Right = xf_rr;

					ib++;
				}

				return;
			}

			while (count-- != 0) {

				av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right) >> 2;

				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), av);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				fl = CLIP_DSP(fl);
				flp = CLIP_DSP(flp);

				// crossfade from previous to current preset
				if (!bexp)
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
				else
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left

				pbf[ib].Left = xf_fl;          // crossfaded front left, duplicated to all channels
				pbf[ib].Right = xf_fl;
				pbr[ib].Left = xf_fl;
				pbr[ib].Right = xf_fl;

				ib++;
			}
		}
	}

	// Helper: called only from DSP_Process
	// DSP_Process quad in to stereo out (preserve stereo spatialization, throw away front/rear)

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_ProcessQuadToStereo(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		Span<PortableSamplePair> pbr = pbrear;       // pointer to buffer of rear stereo samples to process
		int count = sampleCount;
		int ib = 0;
		int fl, fr;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			if (FBatchPreset(pdsp.GetPset(0)) && FBatchPreset(pdsp.GetPset(1))) {

				// convert Quad to Stereo in place, then batch process fx: perf KDB

				// left front + rear -> left, right front + rear -> right

				while (count-- != 0) {
					pbf[ib].Left = (pbf[ib].Left + pbr[ib].Left) >> 1;
					pbf[ib].Right = (pbf[ib].Right + pbr[ib].Right) >> 1;
					ib++;
				}

				// process left & right

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(1), pbfront, sampleCount, OP_RIGHT);

				// copy processed front to rear

				count = sampleCount;

				ib = 0;

				while (count-- != 0) {
					pbr[ib].Left = pbf[ib].Left;
					pbr[ib].Right = pbf[ib].Right;
					ib++;
				}

			}
			else {
				// left front + rear -> left fx, right front + rear -> right fx
				while (count-- != 0) {
					fl = PSET_GetNext(pdsp.GetPset(0), (pbf[ib].Left + pbr[ib].Left) >> 1);
					fr = PSET_GetNext(pdsp.GetPset(1), (pbf[ib].Right + pbr[ib].Right) >> 1);
					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);

					pbr[ib].Left = pbf[ib].Left = fl;
					pbr[ib].Right = pbf[ib].Right = fr;
					ib++;
				}
			}
			return;
		}

		// crossfading to current preset from previous preset

		{
			int r;
			int rl, rr;
			int flp, frp, rlp, rrp;
			int xf_fl, xf_fr, xf_rl, xf_rr;
			int avl, avr;
			bool bexp = pdsp.bexpfade;
			bool bfadetoquad = pdsp.ipset == 0;
			bool bfadefromquad = pdsp.ipsetprev == 0;

			if (bfadetoquad || bfadefromquad) {
				// special case if previous or current preset is 0 (quad passthrough)

				while (count-- != 0) {
					avl = (pbf[ib].Left + pbr[ib].Left) >> 1;
					avr = (pbf[ib].Right + pbr[ib].Right) >> 1;

					// get current preset values

					// current preset is 0, which implies fading to passthrough quad output
					// need to fade from stereo to quad

					if (pdsp.ipset != 0) {
						rl = fl = PSET_GetNext(pdsp.GetPset(0), avl);
						rr = fr = PSET_GetNext(pdsp.GetPset(0), avr);
					}
					else {
						fl = pbf[ib].Left;
						fr = pbf[ib].Right;
						rl = pbr[ib].Left;
						rr = pbr[ib].Right;
					}

					// get previous preset values

					if (pdsp.ipsetprev != 0) {
						rlp = flp = PSET_GetNext(pdsp.GetPsetPrev(0), avl);
						rrp = frp = PSET_GetNext(pdsp.GetPsetPrev(0), avr);
					}
					else {
						flp = pbf[ib].Left;
						frp = pbf[ib].Right;
						rlp = pbr[ib].Left;
						rrp = pbr[ib].Right;
					}

					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);
					flp = CLIP_DSP(flp);
					frp = CLIP_DSP(frp);
					rl = CLIP_DSP(rl);
					rr = CLIP_DSP(rr);
					rlp = CLIP_DSP(rlp);
					rrp = CLIP_DSP(rrp);

					// get current ramp value

					r = RMP_GetNext(ref pdsp.xramp);

					// crossfade from previous to current preset

					if (!bexp) {
						xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE(rr, rrp, r);  // crossfade front left previous to front left
					}
					else {
						xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE_EXP(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE_EXP(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE_EXP(rr, rrp, r);  // crossfade front left previous to front left
					}

					pbf[ib].Left = xf_fl;
					pbf[ib].Right = xf_fr;
					pbr[ib].Left = xf_rl;
					pbr[ib].Right = xf_rr;

					ib++;
				}

				return;
			}

			while (count-- != 0) {
				avl = (pbf[ib].Left + pbr[ib].Left) >> 1;
				avr = (pbf[ib].Right + pbr[ib].Right) >> 1;

				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), avl);
				fr = PSET_GetNext(pdsp.GetPset(1), avr);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), avl);
				frp = PSET_GetNext(pdsp.GetPsetPrev(1), avr);


				fl = CLIP_DSP(fl);
				fr = CLIP_DSP(fr);

				// get previous preset values

				flp = CLIP_DSP(flp);
				frp = CLIP_DSP(frp);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				// crossfade from previous to current preset
				if (!bexp) {
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE(fr, frp, r);
				}
				else {
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE_EXP(fr, frp, r);
				}

				pbf[ib].Left = xf_fl;          // crossfaded front left
				pbf[ib].Right = xf_fr;

				pbr[ib].Left = xf_fl;          // duplicate front channel to rear channel
				pbr[ib].Right = xf_fr;

				ib++;
			}
		}
	}

	// Helper: called only from DSP_Process
	// DSP_Process quad in to quad out

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_ProcessQuadToQuad(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		Span<PortableSamplePair> pbr = pbrear;       // pointer to buffer of rear stereo samples to process
		int count = sampleCount;
		int ib = 0;
		int fl, fr, rl, rr;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			// each channel gets its own processor

			if (FBatchPreset(pdsp.GetPset(0)) && FBatchPreset(pdsp.GetPset(1)) && FBatchPreset(pdsp.GetPset(2)) && FBatchPreset(pdsp.GetPset(3))) {
				// batch process fx front & rear, left & right: perf KDB

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(1), pbfront, sampleCount, OP_RIGHT);
				PSET_GetNextN(pdsp.GetPset(2), pbrear, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(3), pbrear, sampleCount, OP_RIGHT);
			}
			else {
				while (count-- != 0) {
					fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
					fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);
					rl = PSET_GetNext(pdsp.GetPset(2), pbr[ib].Left);
					rr = PSET_GetNext(pdsp.GetPset(3), pbr[ib].Right);

					pbf[ib].Left = CLIP_DSP(fl);
					pbf[ib].Right = CLIP_DSP(fr);
					pbr[ib].Left = CLIP_DSP(rl);
					pbr[ib].Right = CLIP_DSP(rr);

					ib++;
				}
			}
			return;
		}

		// crossfading to current preset from previous preset

		{
			int r;
			int flp, frp, rlp, rrp;
			int xf_fl, xf_fr, xf_rl, xf_rr;
			bool bexp = pdsp.bexpfade;

			while (count-- != 0) {
				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
				fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);
				rl = PSET_GetNext(pdsp.GetPset(2), pbr[ib].Left);
				rr = PSET_GetNext(pdsp.GetPset(3), pbr[ib].Right);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), pbf[ib].Left);
				frp = PSET_GetNext(pdsp.GetPsetPrev(1), pbf[ib].Right);
				rlp = PSET_GetNext(pdsp.GetPsetPrev(2), pbr[ib].Left);
				rrp = PSET_GetNext(pdsp.GetPsetPrev(3), pbr[ib].Right);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				// crossfade from previous to current preset
				if (!bexp) {
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE(fr, frp, r);
					xf_rl = XFADE(rl, rlp, r);
					xf_rr = XFADE(rr, rrp, r);
				}
				else {
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE_EXP(fr, frp, r);
					xf_rl = XFADE_EXP(rl, rlp, r);
					xf_rr = XFADE_EXP(rr, rrp, r);
				}

				pbf[ib].Left = CLIP_DSP(xf_fl);            // crossfaded front left
				pbf[ib].Right = CLIP_DSP(xf_fr);
				pbr[ib].Left = CLIP_DSP(xf_rl);
				pbr[ib].Right = CLIP_DSP(xf_rr);

				ib++;
			}
		}
	}


	// Helper: called only from DSP_Process
	// DSP_Process quad + center in to mono out (front left = front right)

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_Process5To1(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, Span<PortableSamplePair> pbcenter, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		Span<PortableSamplePair> pbr = pbrear;       // pointer to buffer of rear stereo samples to process
		Span<PortableSamplePair> pbc = pbcenter;     // pointer to buffer of center mono samples to process
		int count = sampleCount;
		int ib = 0;
		int x;
		int av;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			if (FBatchPreset(pdsp.GetPset(0))) {

				// convert Quad + Center to Mono in place, then batch process fx: perf KDB

				// left front + rear -> left, right front + rear -> right
				while (count-- != 0) {
					// pbf->left = ((pbf->left + pbf->right + pbr->left + pbr->right + pbc->left) / 5);

					av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right + pbc[ib].Left) * 51;  // 51/255 = 1/5
					av >>= 8;
					pbf[ib].Left = av;
					ib++;
				}

				// process left (mono), duplicate into right

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT_DUPLICATE);

				// copy processed front to rear & center

				count = sampleCount;

				ib = 0;

				while (count-- != 0) {
					pbr[ib].Left = pbf[ib].Left;
					pbr[ib].Right = pbf[ib].Right;
					pbc[ib].Left = pbf[ib].Left;
					ib++;
				}

			}
			else {
				// avg fl,fr,rl,rr,fc into mono fx, duplicate on all channels
				while (count-- != 0) {
					// av = ((pbf->left + pbf->right + pbr->left + pbr->right + pbc->left) / 5);
					av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right + pbc[ib].Left) * 51;  // 51/255 = 1/5
					av >>= 8;
					x = PSET_GetNext(pdsp.GetPset(0), av);
					x = CLIP_DSP(x);
					pbr[ib].Left = pbr[ib].Right = pbf[ib].Left = pbf[ib].Right = pbc[ib].Left = x;
					ib++;
				}
			}
			return;
		}

		{
			int r;
			int fl, fr, rl, rr, fc;
			int flp, frp, rlp, rrp, fcp;
			int xf_fl, xf_fr, xf_rl, xf_rr, xf_fc;
			bool bexp = pdsp.bexpfade;
			bool bfadetoquad = pdsp.ipset == 0;
			bool bfadefromquad = pdsp.ipsetprev == 0;

			if (bfadetoquad || bfadefromquad) {
				// special case if previous or current preset is 0 (quad passthrough)

				while (count-- != 0) {
					// av = ((pbf->left + pbf->right + pbr->left + pbr->right) >> 2);

					av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right + pbc[ib].Left) * 51;  // 51/255 = 1/5
					av >>= 8;

					// get current preset values

					// current preset is 0, which implies fading to passthrough quad output
					// need to fade from mono to quad

					if (pdsp.ipset != 0)
						fc = rl = rr = fl = fr = PSET_GetNext(pdsp.GetPset(0), av);
					else {
						fl = pbf[ib].Left;
						fr = pbf[ib].Right;
						rl = pbr[ib].Left;
						rr = pbr[ib].Right;
						fc = pbc[ib].Left;
					}

					// get previous preset values

					if (pdsp.ipsetprev != 0)
						fcp = rrp = rlp = frp = flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);
					else {
						flp = pbf[ib].Left;
						frp = pbf[ib].Right;
						rlp = pbr[ib].Left;
						rrp = pbr[ib].Right;
						fcp = pbc[ib].Left;
					}

					fl = CLIP_DSP(fl);
					fr = CLIP_DSP(fr);
					flp = CLIP_DSP(flp);
					frp = CLIP_DSP(frp);
					rl = CLIP_DSP(rl);
					rr = CLIP_DSP(rr);
					rlp = CLIP_DSP(rlp);
					rrp = CLIP_DSP(rrp);
					fc = CLIP_DSP(fc);
					fcp = CLIP_DSP(fcp);

					// get current ramp value

					r = RMP_GetNext(ref pdsp.xramp);

					// crossfade from previous to current preset

					if (!bexp) {
						xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE(rr, rrp, r);  // crossfade front left previous to front left
						xf_fc = XFADE(fc, fcp, r);  // crossfade front left previous to front left
					}
					else {
						xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
						xf_fr = XFADE_EXP(fr, frp, r);  // crossfade front left previous to front left
						xf_rl = XFADE_EXP(rl, rlp, r);  // crossfade front left previous to front left
						xf_rr = XFADE_EXP(rr, rrp, r);  // crossfade front left previous to front left
						xf_fc = XFADE_EXP(fc, fcp, r);  // crossfade front left previous to front left
					}

					pbf[ib].Left = xf_fl;
					pbf[ib].Right = xf_fr;
					pbr[ib].Left = xf_rl;
					pbr[ib].Right = xf_rr;
					pbc[ib].Left = xf_fc;

					ib++;
				}

				return;
			}

			while (count-- != 0) {

				// av = ((pbf->left + pbf->right + pbr->left + pbr->right) >> 2);
				av = (pbf[ib].Left + pbf[ib].Right + pbr[ib].Left + pbr[ib].Right + pbc[ib].Left) * 51;  // 51/255 = 1/5
				av >>= 8;

				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), av);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), av);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				fl = CLIP_DSP(fl);
				flp = CLIP_DSP(flp);

				// crossfade from previous to current preset
				if (!bexp)
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
				else
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left

				pbf[ib].Left = xf_fl;          // crossfaded front left, duplicated to all channels
				pbf[ib].Right = xf_fl;
				pbr[ib].Left = xf_fl;
				pbr[ib].Right = xf_fl;
				pbc[ib].Left = xf_fl;

				ib++;
			}
		}
	}

	// Helper: called only from DSP_Process
	// DSP_Process quad + center in to quad + center out

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	static void DSP_Process5To5(Dsp pdsp, Span<PortableSamplePair> pbfront, Span<PortableSamplePair> pbrear, Span<PortableSamplePair> pbcenter, int sampleCount, bool bcrossfading) {
		Span<PortableSamplePair> pbf = pbfront;      // pointer to buffer of front stereo samples to process
		Span<PortableSamplePair> pbr = pbrear;       // pointer to buffer of rear stereo samples to process
		Span<PortableSamplePair> pbc = pbcenter;     // pointer to buffer of center mono samples to process

		int count = sampleCount;
		int ib = 0;
		int fl, fr, rl, rr, fc;

		if (!bcrossfading) {
			if (pdsp.ipset == 0)
				return;

			// each channel gets its own processor

			if (FBatchPreset(pdsp.GetPset(0)) && FBatchPreset(pdsp.GetPset(1)) && FBatchPreset(pdsp.GetPset(2)) && FBatchPreset(pdsp.GetPset(3))) {
				// batch process fx front & rear, left & right: perf KDB

				PSET_GetNextN(pdsp.GetPset(0), pbfront, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(1), pbfront, sampleCount, OP_RIGHT);
				PSET_GetNextN(pdsp.GetPset(2), pbrear, sampleCount, OP_LEFT);
				PSET_GetNextN(pdsp.GetPset(3), pbrear, sampleCount, OP_RIGHT);
				PSET_GetNextN(pdsp.GetPset(4), pbcenter, sampleCount, OP_LEFT);
			}
			else {
				while (count-- != 0) {
					fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
					fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);
					rl = PSET_GetNext(pdsp.GetPset(2), pbr[ib].Left);
					rr = PSET_GetNext(pdsp.GetPset(3), pbr[ib].Right);
					fc = PSET_GetNext(pdsp.GetPset(4), pbc[ib].Left);

					pbf[ib].Left = CLIP_DSP(fl);
					pbf[ib].Right = CLIP_DSP(fr);
					pbr[ib].Left = CLIP_DSP(rl);
					pbr[ib].Right = CLIP_DSP(rr);
					pbc[ib].Left = CLIP_DSP(fc);

					ib++;
				}
			}
			return;
		}

		// crossfading to current preset from previous preset

		{
			int r;
			int flp, frp, rlp, rrp, fcp;
			int xf_fl, xf_fr, xf_rl, xf_rr, xf_fc;
			bool bexp = pdsp.bexpfade;

			while (count-- != 0) {
				// get current preset values

				fl = PSET_GetNext(pdsp.GetPset(0), pbf[ib].Left);
				fr = PSET_GetNext(pdsp.GetPset(1), pbf[ib].Right);
				rl = PSET_GetNext(pdsp.GetPset(2), pbr[ib].Left);
				rr = PSET_GetNext(pdsp.GetPset(3), pbr[ib].Right);
				fc = PSET_GetNext(pdsp.GetPset(4), pbc[ib].Left);

				// get previous preset values

				flp = PSET_GetNext(pdsp.GetPsetPrev(0), pbf[ib].Left);
				frp = PSET_GetNext(pdsp.GetPsetPrev(1), pbf[ib].Right);
				rlp = PSET_GetNext(pdsp.GetPsetPrev(2), pbr[ib].Left);
				rrp = PSET_GetNext(pdsp.GetPsetPrev(3), pbr[ib].Right);
				fcp = PSET_GetNext(pdsp.GetPsetPrev(4), pbc[ib].Left);

				// get current ramp value

				r = RMP_GetNext(ref pdsp.xramp);

				// crossfade from previous to current preset
				if (!bexp) {
					xf_fl = XFADE(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE(fr, frp, r);
					xf_rl = XFADE(rl, rlp, r);
					xf_rr = XFADE(rr, rrp, r);
					xf_fc = XFADE(fc, fcp, r);
				}
				else {
					xf_fl = XFADE_EXP(fl, flp, r);  // crossfade front left previous to front left
					xf_fr = XFADE_EXP(fr, frp, r);
					xf_rl = XFADE_EXP(rl, rlp, r);
					xf_rr = XFADE_EXP(rr, rrp, r);
					xf_fc = XFADE_EXP(fc, fcp, r);
				}

				pbf[ib].Left = CLIP_DSP(xf_fl);            // crossfaded front left
				pbf[ib].Right = CLIP_DSP(xf_fr);
				pbr[ib].Left = CLIP_DSP(xf_rl);
				pbr[ib].Right = CLIP_DSP(xf_rr);
				pbc[ib].Left = CLIP_DSP(xf_fc);

				ib++;
			}
		}
	}

	// This is an evil hack, but we need to restore the old presets after letting the sound system update for a few frames, so we just
	//  "defer" the restore until the top of the next call to CheckNewDspPresets.  I put in a bit of warning in case we ever have code
	//  outside of this time period modifying any of the dsp convars.  It doesn't seem to be an issue just save/loading between levels
	static bool g_bNeedPresetRestore = false;

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	class PreserveDSP(ConVar cvar)
	{
		[CvarIgnore] public readonly ConVar cvar = cvar;
		public float oldvalue;
	}

	static readonly PreserveDSP[] g_PreserveDSP = [
		new(dsp_room),
		new(dsp_water),
		new(dsp_player),
		new(dsp_facingaway),
		new(dsp_speaker),
		new(dsp_spatial),
		new(dsp_automatic)
	];

	//-----------------------------------------------------------------------------
	// Purpose: Called at the top of CheckNewDspPresets to restore ConVars to real values
	//-----------------------------------------------------------------------------
	static void DSP_CheckRestorePresets() {
		if (!g_bNeedPresetRestore)
			return;

		g_bNeedPresetRestore = false;

		// Restore
		foreach (PreserveDSP slot in g_PreserveDSP) {
			ConVar cv = slot.cvar;
			Assert(cv);
			if (cv.GetFloat() != 0.0f) {
				// NOTE: dsp_speaker is being (correctly) save/restored by maps, which would trigger this warning
				//Warning( "DSP_CheckRestorePresets:  Value of %s was changed between DSP_ClearState and CheckNewDspPresets, not restoring to old value\n", cv->GetName() );
				continue;
			}
			cv.SetValue(slot.oldvalue);
		}

		// reinit all dsp processors (only load preset file on engine init, however)

		AllocDsps(false);

		// flush dsp automatic nodes

		g_bdas_init_nodes = false;
		g_bdas_room_init = false;
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public static void DSP_ClearState() {
		// if we already cleared dsp state, and a restore is pending,
		// don't clear again

		if (g_bNeedPresetRestore)
			return;

		// always save a cleared dsp automatic value to force reset of all adsp code

		dsp_automatic.SetValue(0);

		// Tracker 7155:  YWB:  This is a pretty ugly hack to zero out all of the dsp convars and bootstrap the dsp system into using them for a few frames

		foreach (PreserveDSP slot in g_PreserveDSP) {
			ConVar cv = slot.cvar;
			Assert(cv);
			slot.oldvalue = cv.GetFloat();
			cv.SetValue(0);
		}

		// force all dsp presets to end crossfades, end one-shot presets, & release and reset all resources
		// immediately.

		FreeDsps(false); // free all dsp states, but don't discard preset templates

		// This forces the ConVars which we set to zero above to be reloaded to their old values at the time we issue the CheckNewDspPresets
		//  command.  This seems to happen early enough in level changes were we don't appear to be trying to stomp real settings...

		g_bNeedPresetRestore = true;
	}

	// return true if dsp's preset is one-shot and it has expired

	static bool DSP_HasExpired(int idsp) {
		Dsp pdsp;

		Assert(idsp < CDSPS);

		if (idsp < 0 || idsp >= CDSPS)
			return false;

		pdsp = dsps[idsp];

		// if first preset has expired, dsp has expired

		if (PSET_IsOneShot(pdsp.GetPset(0)))
			return PSET_HasExpired(pdsp.GetPset(0));
		else
			return false;
	}

	// returns true if dsp is crossfading from previous dsp preset

	static bool DSP_IsCrossfading(int idsp) {
		Dsp pdsp;

		Assert(idsp < CDSPS);

		if (idsp < 0 || idsp >= CDSPS)
			return false;

		pdsp = dsps[idsp];

		return !RMP_HitEnd(ref pdsp.xramp);

	}

	// returns previous preset # before oneshot preset was set

	static int DSP_OneShotPrevious(int idsp) {
		Dsp pdsp;
		int idsp_prev;

		Assert(idsp < CDSPS);

		if (idsp < 0 || idsp >= CDSPS)
			return 0;

		pdsp = dsps[idsp];

		idsp_prev = pdsp.ipsetsav_oneshot;

		return idsp_prev;
	}

	// given idsp (processor index), return true if
	// both current and previous presets are 0 for this processor

	static bool DSP_PresetIsOff(int idsp) {
		Dsp pdsp;

		if (idsp < 0 || idsp >= CDSPS)
			return true;

		Assert(idsp < CDSPS);                   // make sure idsp is valid

		pdsp = dsps[idsp];

		// if current and previous preset 0, return - preset 0 is 'off'

		return pdsp.ipset == 0 && pdsp.ipsetprev == 0;
	}

	// returns true if dsp is off for room effects

	public static bool DSP_RoomDSPIsOff() {
		return DSP_PresetIsOff(Get_idsp_room());
	}

	// Main DSP processing routine:
	// process samples in buffers using pdsp processor
	// continue crossfade between 2 dsp processors if crossfading on switch
	// pfront - front stereo buffer to process
	// prear - rear stereo buffer to process (may be NULL)
	// pcenter - front center mono buffer (may be NULL)
	// sampleCount - number of samples in pbuf to process
	// This routine also maps the # processing channels in the pdsp to the number of channels
	// supplied.  ie: if the pdsp has 4 channels and pbfront and pbrear are both non-null, the channels
	// map 1:1 through the processors.

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void DSP_Process(int idsp, PortableSamplePair[] pbfront, PortableSamplePair[]? pbrear, PortableSamplePair[]? pbcenter, int sampleCount) {
		bool bcrossfading;
		int cchan_in;                               // input channels (2,4 or 5)
		int cprocs;                                 // output cannels (1, 2 or 4)
		Dsp pdsp;

#if GMOD_DLL
		g_DspCurrentPreset = 0;
#endif

		if (idsp < 0 || idsp >= CDSPS)
			return;

		// Don't pull dsp data in if player is not connected (during load/level change)
		if (!soundServices.IsConnected())
			return;

		Assert(idsp < CDSPS);                   // make sure idsp is valid

		pdsp = dsps[idsp];

		Assert(pbfront != null);

		// return right away if fx processing is turned off

		if (dsp_off.GetInt() != 0)
			return;

		// if current and previous preset 0, return - preset 0 is 'off'

		if (pdsp.ipset == 0 && pdsp.ipsetprev == 0)
			return;

		if (sampleCount < 0)
			return;

		bcrossfading = !RMP_HitEnd(ref pdsp.xramp);

		// if not crossfading, and previous channel is not null, free previous

		if (!bcrossfading)
			DSP_FreePrevPreset(pdsp);

		// if current and previous preset 0 (ie: just freed previous), return - preset 0 is 'off'

		if (pdsp.ipset == 0 && pdsp.ipsetprev == 0)
			return;

#if GMOD_DLL
		g_DspCurrentPreset = pdsp.ipset != 0 ? pdsp.ipset : pdsp.ipsetprev;
#endif

		cchan_in = (pbrear != null ? 4 : 2) + (pbcenter != null ? 1 : 0);
		cprocs = pdsp.cchan;

		Assert(cchan_in == 2 || cchan_in == 4 || cchan_in == 5);

		// if oneshot preset, update the duration counter (only update front left counter)

		PSET_UpdateDuration(pdsp.GetPset(0), sampleCount);

		// NOTE: when mixing between different channel sizes,
		// always AVERAGE down to fewer channels and DUPLICATE up more channels.
		// The following routines always process cchan_in channels.
		// ie: QuadToMono still updates 4 values in buffer

		// DSP_Process stereo in to mono out (ie: left and right are averaged)

		if (cchan_in == 2 && cprocs == 1) {
			DSP_ProcessStereoToMono(pdsp, pbfront, pbrear, sampleCount, bcrossfading);
			return;
		}

		// DSP_Process stereo in to stereo out (if more than 2 procs, ignore them)

		if (cchan_in == 2 && cprocs >= 2) {
			DSP_ProcessStereoToStereo(pdsp, pbfront, pbrear, sampleCount, bcrossfading);
			return;
		}


		// DSP_Process quad in to mono out

		if (cchan_in == 4 && cprocs == 1) {
			DSP_ProcessQuadToMono(pdsp, pbfront, pbrear, sampleCount, bcrossfading);
			return;
		}


		// DSP_Process quad in to stereo out (preserve stereo spatialization, loose front/rear)

		if (cchan_in == 4 && cprocs == 2) {
			DSP_ProcessQuadToStereo(pdsp, pbfront, pbrear, sampleCount, bcrossfading);
			return;
		}


		// DSP_Process quad in to quad out

		if (cchan_in == 4 && cprocs == 4) {
			DSP_ProcessQuadToQuad(pdsp, pbfront, pbrear, sampleCount, bcrossfading);
			return;
		}

		// DSP_Process quad + center in to mono out

		if (cchan_in == 5 && cprocs == 1) {
			DSP_Process5To1(pdsp, pbfront, pbrear, pbcenter, sampleCount, bcrossfading);
			return;
		}

		if (cchan_in == 5 && cprocs == 2) {
			// undone: not used in AllocDsps
			Assert(false);
			//DSP_Process5to2( pdsp, pbfront, pbrear, pbcenter, sampleCount, bcrossfading );
			return;
		}

		if (cchan_in == 5 && cprocs == 4) {
			// undone: not used in AllocDsps
			Assert(false);
			//DSP_Process5to4( pdsp, pbfront, pbrear, pbcenter, sampleCount, bcrossfading );
			return;
		}

		// DSP_Process quad + center in to quad + center out

		if (cchan_in == 5 && cprocs == 5) {
			DSP_Process5To5(pdsp, pbfront, pbrear, pbcenter, sampleCount, bcrossfading);
			return;
		}

	}

	// DSP helpers

	// free all dsp processors

	public static void FreeDsps(bool bReleaseTemplateMemory) {

		DSP_Free(idsp_room);
		DSP_Free(idsp_water);
		DSP_Free(idsp_player);
		DSP_Free(idsp_facingaway);
		DSP_Free(idsp_speaker);
		DSP_Free(idsp_spatial);
		DSP_Free(idsp_automatic);

		idsp_room = 0;
		idsp_water = 0;
		idsp_player = 0;
		idsp_facingaway = 0;
		idsp_speaker = 0;
		idsp_spatial = 0;
		idsp_automatic = 0;

		for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
			PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);
			if (specialBuffer.SpecialDSP != 0) {
				DSP_Free(specialBuffer.IdspSpecialDsp);
				specialBuffer.IdspSpecialDsp = 0;
				specialBuffer.PrevSpecialDSP = 0;
				specialBuffer.SpecialDSP = 0;
			}
		}

		DSP_FreeAll();

		// only unlock and free psettemplate memory on engine shutdown

		if (bReleaseTemplateMemory)
			DSP_ReleaseMemory();
	}

	// alloc dsp processors, load dsp preset array from file on engine init only

	public static bool AllocDsps(bool bLoadPresetFile) {
		int csurround = g_AudioDevice!.IsSurround() ? 2 : 0;        // surround channels to allocate
		int ccenter = g_AudioDevice.IsSurroundCenter() ? 1 : 0; // center channels to allocate

		DSP_InitAll(bLoadPresetFile);

		idsp_room = -1;
		idsp_water = -1;
		idsp_player = -1;
		idsp_facingaway = -1;
		idsp_speaker = -1;
		idsp_spatial = -1;
		idsp_automatic = -1;

		// alloc dsp room channel (mono, stereo if dsp_stereo is 1)

		// dsp room is mono, 300ms default fade time

		idsp_room = DSP_Alloc(dsp_room.GetInt(), 200, 1);

		// dsp automatic overrides dsp_room, if dsp_room set to DSP_AUTOMATIC (1)

		idsp_automatic = DSP_Alloc(dsp_automatic.GetInt(), 200, 1);

		// alloc stereo or quad series processors for player or water

		// water and player presets are mono

		idsp_water = DSP_Alloc(dsp_water.GetInt(), 100, 1);
		idsp_player = DSP_Alloc(dsp_player.GetInt(), 100, 1);

		// alloc facing away filters (stereo, quad or 5ch)

		idsp_facingaway = DSP_Alloc(dsp_facingaway.GetInt(), 100, 2 + csurround + ccenter);

		// alloc speaker preset (mono)

		idsp_speaker = DSP_Alloc(dsp_speaker.GetInt(), 300, 1);

		// alloc spatial preset (2-5 chan)

		idsp_spatial = DSP_Alloc(dsp_spatial.GetInt(), 300, 2 + csurround + ccenter);

		// init prev values

		ipset_room_prev = dsp_room.GetInt();
		ipset_water_prev = dsp_water.GetInt();
		ipset_player_prev = dsp_player.GetInt();
		ipset_facingaway_prev = dsp_facingaway.GetInt();
		ipset_room_typeprev = dsp_room_type.GetInt();
		ipset_speaker_prev = dsp_speaker.GetInt();
		ipset_spatial_prev = dsp_spatial.GetInt();
		ipset_automatic_prev = dsp_automatic.GetInt();

		if (idsp_room < 0 || idsp_water < 0 || idsp_player < 0 || idsp_facingaway < 0 || idsp_speaker < 0 || idsp_spatial < 0 || idsp_automatic < 0) {
			DevMsg("WARNING: DSP processor failed to initialize! \n");

			FreeDsps(true);
			return false;
		}

		return true;
	}

	// count number of dsp presets specified in preset file
	// counts outer {} pairs, ignoring inner {} pairs.

	static int DSP_CountFilePresets(ReadOnlySpan<char> pstart) {
		int cpresets = 0;
		bool binpreset = false;
		bool blookleft = false;
		Span<char> com_token = stackalloc char[1024];

		while (true) {
			pstart = SndParse.COM_Parse(pstart, com_token);

			if (com_token[0] == '\0')
				break;

			if (com_token[0] == '{')  // left paren
			{
				if (!binpreset) {
					cpresets++;         // found preset:
					blookleft = true;   // look for another left
					binpreset = true;
				}
				else {
					blookleft = false; // inside preset: next, look for matching right paren
				}

				continue;
			}

			if (com_token[0] == '}')  // right paren
			{
				if (binpreset) {
					if (!blookleft)     // looking for right paren
					{
						blookleft = true; // found it, now look for another left
					}
					else {
						// expected inner left paren, found outer right - end of preset definition
						binpreset = false;
						blookleft = true;
					}
				}
				else {
					// error - unexpected } paren
					DevMsg("PARSE ERROR!!! dsp_presets.txt: unexpected '}' \n");
					continue;
				}
			}

		}

		return cpresets;
	}

	// token map for dsp_preset.txt

	static readonly (string sz, int i)[] gdsp_stringmap = [
		// PROCESSOR TYPE:
		("NULL", PRC_NULL),
		("DLY", PRC_DLY),
		("RVA", PRC_RVA),
		("FLT", PRC_FLT),
		("CRS", PRC_CRS),
		("PTC", PRC_PTC),
		("ENV", PRC_ENV),
		("LFO", PRC_LFO),
		("EFO", PRC_EFO),
		("MDY", PRC_MDY),
		("DFR", PRC_DFR),
		("AMP", PRC_AMP),

		// FILTER TYPE:
		("LP", FLT_LP),
		("HP", FLT_HP),
		("BP", FLT_BP),

		// FILTER QUALITY:
		("LO", QUA_LO),
		("MED", QUA_MED),
		("HI", QUA_HI),
		("VHI", QUA_VHI),

		// DELAY TYPE:
		("PLAIN", DLY_PLAIN),
		("ALLPASS", DLY_ALLPASS),
		("LOWPASS", DLY_LOWPASS),
		("DLINEAR", DLY_LINEAR),
		("FLINEAR", DLY_FLINEAR),
		("LOWPASS_4TAP", DLY_LOWPASS_4TAP),
		("PLAIN_4TAP", DLY_PLAIN_4TAP),

		// LFO TYPE:
		("SIN", LFO_SIN),
		("TRI", LFO_TRI),
		("SQR", LFO_SQR),
		("SAW", LFO_SAW),
		("RND", LFO_RND),
		("LOG_IN", LFO_LOG_IN),
		("LOG_OUT", LFO_LOG_OUT),
		("LIN_IN", LFO_LIN_IN),
		("LIN_OUT", LFO_LIN_OUT),

		// ENVELOPE TYPE:
		("LIN", ENV_LIN),
		("EXP", ENV_EXP),

		// PRESET CONFIGURATION TYPE:
		("SIMPLE", PSET_SIMPLE),
		("LINEAR", PSET_LINEAR),
		("PARALLEL2", PSET_PARALLEL2),
		("PARALLEL4", PSET_PARALLEL4),
		("PARALLEL5", PSET_PARALLEL5),
		("FEEDBACK", PSET_FEEDBACK),
		("FEEDBACK3", PSET_FEEDBACK3),
		("FEEDBACK4", PSET_FEEDBACK4),
		("MOD1", PSET_MOD),
		("MOD2", PSET_MOD2),
		("MOD3", PSET_MOD3)
	];

	static bool isnumber(char c) => c == '+' || c == '-' || c == '0' || c == '1' || c == '2' || c == '3' || c == '4' || c == '5' || c == '6' || c == '7' || c == '8' || c == '9';

	internal static float strtof(ReadOnlySpan<char> psz) {
		psz = psz.SliceNullTerminatedString().TrimStart();
		int end = 0;
		while (end < psz.Length && (char.IsDigit(psz[end]) || psz[end] is '.' or '-' or '+' or 'e' or 'E'))
			end++;
		while (end > 0 && !float.TryParse(psz[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			end--;
		return end > 0 && float.TryParse(psz[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0.0f;
	}

	// given ptr to null term. string, return integer or float value from g_dsp_stringmap

	static float DSP_LookupStringToken(ReadOnlySpan<char> psz, int ipset) {
		int i;
		float fipset = (float)ipset;

		if (isnumber(psz[0]))
			return strtof(psz);

		psz = psz.SliceNullTerminatedString();
		for (i = 0; i < gdsp_stringmap.Length; i++) {
			if (stricmp(gdsp_stringmap[i].sz, psz) == 0)
				return gdsp_stringmap[i].i;
		}

		// not found

		DevMsg($"DSP PARSE ERROR! token not found in dsp_presets.txt. Preset: {fipset,3:F0} \n");
		return 0;
	}

	// load dsp preset file, parse presets into g_psettemplate array
	// format for each preset:
	// { <preset #> <preset type> <#processors> <gain> { <processor type> <param0>...<param15> } {...} {...} }

	internal const char CHAR_LEFT_PAREN = '{';
	internal const char CHAR_RIGHT_PAREN = '}';

	// free preset template memory

	static void DSP_ReleaseMemory() {
		if (g_psettemplates != null)
			g_psettemplates = null;
	}

	static bool DSP_LoadPresetFile() {
		ReadOnlySpan<char> pstart;
		bool bResult = false;
		int cpresets;
		int ipreset;
		int itype;
		int cproc;
		float mix_min;
		float mix_max;
		float db_min;
		float db_mixdrop;
		int j;
		bool fdone;
		float duration;
		float fadeout;

		string szFile = "scripts/dsp_presets.txt";

		IFileHandle? file = filesystem.Open(szFile, FileOpenOptions.Read, "GAME");
		if (file == null) {
			Error($"DSP_LoadPresetFile: unable to open '{szFile}'\n");
			return false;
		}
		string pbuffer;
		using (StreamReader reader = new StreamReader(file.Stream))
			pbuffer = reader.ReadToEnd();
		file.Dispose();

		Span<char> com_token = stackalloc char[1024];

		pstart = pbuffer;

		// figure out how many presets we're loading - count outer parens.

		cpresets = DSP_CountFilePresets(pstart);

		g_cpsettemplates = cpresets;

		g_psettemplates = CreatePool<Pset>(cpresets);
		if (g_psettemplates == null) {
			Warning("DSP Preset Loader: Out of memory.\n");
			goto load_exit;
		}


		// parse presets into g_psettemplates array

		pstart = pbuffer;

		// for each preset...

		for (j = 0; j < cpresets; j++) {
			// check for end of file or next CHAR_LEFT_PAREN

			while (true) {
				pstart = SndParse.COM_Parse(pstart, com_token);

				if (com_token[0] == '\0')
					break;

				if (com_token[0] != CHAR_LEFT_PAREN)
					continue;

				break;
			}

			// found start of a new preset definition

			// get preset #, type, cprocessors, gain

			pstart = SndParse.COM_Parse(pstart, com_token);
			ipreset = atoi(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			itype = (int)DSP_LookupStringToken(com_token, ipreset);

			pstart = SndParse.COM_Parse(pstart, com_token);
			mix_min = strtof(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			mix_max = strtof(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			duration = strtof(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			fadeout = strtof(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			db_min = strtof(com_token);

			pstart = SndParse.COM_Parse(pstart, com_token);
			db_mixdrop = strtof(com_token);


			g_psettemplates[ipreset].fused = 1;
			g_psettemplates[ipreset].mix_min = mix_min;
			g_psettemplates[ipreset].mix_max = mix_max;
			g_psettemplates[ipreset].duration = duration;
			g_psettemplates[ipreset].fade = fadeout;
			g_psettemplates[ipreset].db_min = db_min;
			g_psettemplates[ipreset].db_mixdrop = db_mixdrop;

			// parse each processor for this preset

			fdone = false;
			cproc = 0;

			while (true) {
				// find CHAR_LEFT_PAREN - start of new processor

				while (true) {
					pstart = SndParse.COM_Parse(pstart, com_token);

					if (com_token[0] == '\0')
						break;

					if (com_token[0] == CHAR_LEFT_PAREN)
						break;

					if (com_token[0] == CHAR_RIGHT_PAREN) {
						// if found right paren, no more processors: done with this preset
						fdone = true;
						break;
					}
				}

				if (fdone)
					break;

				// get processor type

				pstart = SndParse.COM_Parse(pstart, com_token);
				Prcs(g_psettemplates[ipreset])[cproc].type = (int)DSP_LookupStringToken(com_token, ipreset);

				// get param 0..n or stop when hit closing CHAR_RIGHT_PAREN

				int ip = 0;

				while (true) {
					pstart = SndParse.COM_Parse(pstart, com_token);

					if (com_token[0] == '\0')
						break;

					if (com_token[0] == CHAR_RIGHT_PAREN)
						break;

					Prcs(g_psettemplates[ipreset])[cproc].prm[ip++] = DSP_LookupStringToken(com_token, ipreset);

					// cap at max params

					ip = Math.Min(ip, CPRCPARAMS);
				}

				cproc++;
				if (cproc > CPSET_PRCS)
					DevMsg($"DSP PARSE ERROR!!! dsp_presets.txt: missing }} or too many processors in preset #: {ipreset} \n");
				cproc = Math.Min(cproc, CPSET_PRCS); // don't overflow # procs
			}

			// if cproc == 1, type is always SIMPLE

			if (cproc == 1)
				itype = PSET_SIMPLE;

			g_psettemplates[ipreset].type = itype;
			g_psettemplates[ipreset].cprcs = cproc;

		}

		bResult = true;

	load_exit:
		return bResult;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called by client on level shutdown to clear ear ringing dsp effects
	//  could be extended to other stuff
	//-----------------------------------------------------------------------------
	public static void DSP_FastReset(int dspType) {
		// Restore
		foreach (PreserveDSP slot in g_PreserveDSP) {
			if (slot.cvar == dsp_player) {
				slot.oldvalue = dspType;
				return;
			}
		}
	}

	// Helper to check for change in preset of any of 4 processors
	// if switching to a new preset, alloc new preset, simulate both presets in DSP_Process & xfade,
	// called a few times per frame.

	public static void CheckNewDspPresets() {
		bool b_slow_cpu = dsp_slow_cpu.GetInt() != 0;

		DSP_CheckRestorePresets();

		//  room fx are on only if cpu is not slow

		int iroom = b_slow_cpu ? 0 : dsp_room.GetInt();
		int ifacingaway = b_slow_cpu ? 0 : dsp_facingaway.GetInt();
		int iroomtype = b_slow_cpu ? 0 : dsp_room_type.GetInt();
		int ispatial = b_slow_cpu ? 0 : dsp_spatial.GetInt();
		int iautomatic = b_slow_cpu ? 0 : dsp_automatic.GetInt();

		// always use dsp to process these

		int iwater = dsp_water.GetInt();
		int iplayer = dsp_player.GetInt();
		int ispeaker = dsp_speaker.GetInt();

		// check for expired one-shot presets on player and room.
		// Only check if a) no new preset has been set and b) not crossfading from previous preset (ie; previous is null)

		if (iplayer == ipset_player_prev && !DSP_IsCrossfading(idsp_player)) {
			if (DSP_HasExpired(idsp_player)) {
				iplayer = DSP_OneShotPrevious(idsp_player);    // preset has expired - revert to previous preset before one-shot
				dsp_player.SetValue(iplayer);
			}
		}

		if (iroom == ipset_room_prev && !DSP_IsCrossfading(idsp_room)) {
			if (DSP_HasExpired(idsp_room)) {
				iroom = DSP_OneShotPrevious(idsp_room);     // preset has expired - revert to previous preset before one-shot
				dsp_room.SetValue(iroom);
			}
		}


		// legacy code support for "room_type" Cvar

		if (iroomtype != ipset_room_typeprev) {
			// force dsp_room = room_type

			ipset_room_typeprev = iroomtype;
			dsp_room.SetValue(iroomtype);
		}

		// NOTE: don't change presets if currently crossfading from a previous preset

		if (iroom != ipset_room_prev && !DSP_IsCrossfading(idsp_room)) {
			DSP_SetPreset(idsp_room, iroom);
			ipset_room_prev = iroom;

			// force room_type = dsp_room

			dsp_room_type.SetValue(iroom);
			ipset_room_typeprev = iroom;
		}

		if (iwater != ipset_water_prev && !DSP_IsCrossfading(idsp_water)) {
			DSP_SetPreset(idsp_water, iwater);
			ipset_water_prev = iwater;
		}

		if (iplayer != ipset_player_prev && !DSP_IsCrossfading(idsp_player)) {
			DSP_SetPreset(idsp_player, iplayer);
			ipset_player_prev = iplayer;
		}

		if (ifacingaway != ipset_facingaway_prev && !DSP_IsCrossfading(idsp_facingaway)) {
			DSP_SetPreset(idsp_facingaway, ifacingaway);
			ipset_facingaway_prev = ifacingaway;
		}

		if (ispeaker != ipset_speaker_prev && !DSP_IsCrossfading(idsp_speaker)) {
			DSP_SetPreset(idsp_speaker, ispeaker);
			ipset_speaker_prev = ispeaker;
		}

		if (ispatial != ipset_spatial_prev && !DSP_IsCrossfading(idsp_spatial)) {
			DSP_SetPreset(idsp_spatial, ispatial);
			ipset_spatial_prev = ispatial;
		}

		if (iautomatic != ipset_automatic_prev && !DSP_IsCrossfading(idsp_automatic)) {
			DSP_SetPreset(idsp_automatic, iautomatic);
			ipset_automatic_prev = iautomatic;
		}

		for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
			PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);
			if (specialBuffer.SpecialDSP != specialBuffer.PrevSpecialDSP && !DSP_IsCrossfading(specialBuffer.IdspSpecialDsp)) {
				DSP_SetPreset(specialBuffer.IdspSpecialDsp, specialBuffer.SpecialDSP);
				specialBuffer.PrevSpecialDSP = specialBuffer.SpecialDSP;
			}
		}
	}

	// create idsp_room preset from set of values, reload the preset.
	// modifies psettemplates in place.

	// ipreset is the preset # ie: 40
	// iproc is the processor to modify within the preset (typically 0)
	// pvalues is an array of floating point parameters
	// cparams is the # of elements in pvalues

	// USED FOR DEBUG ONLY.

	public static void DSP_DEBUGSetParams(int ipreset, int iproc, ReadOnlySpan<float> pvalues, int cparams) {
		Pset new_pset;  // preset
		int cparam = Math.Clamp(cparams, 0, CPRCPARAMS);

		// copy template preset from template array

		new_pset = new();
		new_pset.CopyFrom(g_psettemplates![ipreset]);

		// get iproc processor

		ref Prc pprct = ref Prcs(new_pset)[iproc];

		// copy parameters in to processor

		for (int i = 0; i < cparam; i++)
			pprct.prm[i] = pvalues[i];

		// copy constructed preset back into template location

		g_psettemplates[ipreset] = new_pset;

		// setup new preset

		dsp_room.SetValue(0);

		CheckNewDspPresets();

		dsp_room.SetValue(ipreset);

		CheckNewDspPresets();
	}

	// reload entire preset file, reset all current dsp presets
	// NOTE: this is debug code only.  It doesn't do all mem free work correctly!

	public static void DSP_DEBUGReloadPresetFile() {
		int iroom = dsp_room.GetInt();
		int iwater = dsp_water.GetInt();
		int iplayer = dsp_player.GetInt();
		//	int ifacingaway		= dsp_facingaway.GetInt();
		//	int iroomtype		= dsp_room_type.GetInt();
		int ispeaker = dsp_speaker.GetInt();
		int ispatial = dsp_spatial.GetInt();
		//	int iautomatic		= dsp_automatic.GetInt();

		// reload template array

		DSP_ReleaseMemory();

		DSP_LoadPresetFile();

		// force presets to reload

		dsp_room.SetValue(0);
		dsp_water.SetValue(0);
		dsp_player.SetValue(0);
		//dsp_facingaway.SetValue( 0 );
		//dsp_room_type.SetValue( 0 );
		dsp_speaker.SetValue(0);
		dsp_spatial.SetValue(0);
		//dsp_automatic.SetValue( 0 );

		List<int> specialDSPs = [];
		for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
			PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);

			specialDSPs.Add(specialBuffer.SpecialDSP);
			specialBuffer.SpecialDSP = 0;
		}

		CheckNewDspPresets();

		dsp_room.SetValue(iroom);
		dsp_water.SetValue(iwater);
		dsp_player.SetValue(iplayer);
		//dsp_facingaway.SetValue( ifacingaway );
		//dsp_room_type.SetValue( iroomtype );
		dsp_speaker.SetValue(ispeaker);
		dsp_spatial.SetValue(ispatial);
		//dsp_automatic.SetValue( iautomatic );

		int nSpecialDSPNum = 0;
		for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
			PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);

			specialBuffer.SpecialDSP = specialDSPs[nSpecialDSPNum];
			nSpecialDSPNum++;
		}

		CheckNewDspPresets();

		// flush dsp automatic nodes

		g_bdas_init_nodes = false;
		g_bdas_room_init = false;
	}
}
