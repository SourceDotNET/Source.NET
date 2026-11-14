using Source.Common;
using Source.Common.Audio;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Buffers.Binary;
using System.Numerics;

namespace Source.AudioSystem;

public static partial class SndDma
{
	// If this is nonzero, we will only spatialize some of the static
	// channels each frame. The round robin will spatialize 1 / (2 ^ x)
	// of the spatial channels each frame.
	public static readonly ConVar snd_spatialize_roundrobin = new("snd_spatialize_roundrobin", "0", 0, "Lowend optimization: if nonzero, spatialize only a fraction of sound channels each frame. 1/2^x of channels will be spatialized per frame.");

	static uint s_roundrobin = 0; ///< number of times this function is called.
								  ///< used instead of host_frame because that number
								  ///< isn't necessarily available here (sez Yahn).

	/*
	============
	S_Update

	Called once each time through the main loop
	============
	*/
	static readonly ChannelList s_UpdateChannelList = new();

	public static void S_Update(AudioState? pAudioState) {
		Channel ch;

		if (!g_AudioDevice!.IsActive())
			return;

		lock (g_SndMutex) {
			// Update any client side sound fade
			S_UpdateSoundFade();

			if (pAudioState.HasValue) {
				AudioState audioState = pAudioState.Value;
				listener_origin = audioState.Origin;
				MathLib.AngleVectors(audioState.Angles, out listener_forward, out listener_right, out listener_up);
				s_bIsListenerUnderwater = audioState.IsUnderwater;
			}
			else {
				listener_origin = vec3_origin;
				listener_forward = vec3_origin;
				listener_right = vec3_origin;
				listener_up = vec3_origin;
				s_bIsListenerUnderwater = false;
			}

			g_AudioDevice.UpdateListener(listener_origin, listener_forward, listener_right, listener_up);

			int voiceChannelCount = 0;
			int voiceChannelMaxVolume = 0;

			// reset traceline counter for this frame
			g_snd_trace_count = 0;

			// calculate distance to nearest walls, update dsp_spatial
			// updates one wall only per frame (one trace per frame)
			SND_SetSpatialDelays();

			// updates dsp_room if automatic room detection enabled
			DAS_CheckNewRoomDSP();

			// update spatialization for static and dynamic sounds
			ChannelList list = s_UpdateChannelList;
			g_ActiveChannels.GetActiveChannels(list);

			if (snd_spatialize_roundrobin.GetInt() == 0) {
				// spatialize each channel each time
				for (int i = 0; i < list.Count; i++) {
					ch = list.GetChannel(i);
					Assert(ch.Sfx);
					Assert(ch.ActiveIndex > 0);

					SND_Spatialize(ch);         // respatialize channel

					if (ch.Sfx!.Source != null && ch.Sfx.Source.IsVoiceSource()) {
						voiceChannelCount++;
						voiceChannelMaxVolume = Math.Max(voiceChannelMaxVolume, ChannelGetMaxVol(ch));
					}
				}
			}
			else    // lowend performance improvement: spatialize only some  channels each frame.
			{
				uint robinmask = (uint)((1 << snd_spatialize_roundrobin.GetInt()) - 1);

				// now do static channels
				for (int i = 0; i < list.Count; ++i) {
					ch = list.GetChannel(i);
					Assert(ch.Sfx);
					Assert(ch.ActiveIndex > 0);

					// need to check bfirstpass because sound tracing may have been deferred
					if (ch.Flags.FirstPass || (robinmask & s_roundrobin) == (i & robinmask))
						SND_Spatialize(ch);         // respatialize channel

					if (ch.Sfx!.Source != null && ch.Sfx.Source.IsVoiceSource()) {
						voiceChannelCount++;
						voiceChannelMaxVolume = Math.Max(voiceChannelMaxVolume, ChannelGetMaxVol(ch));
					}
				}

				++s_roundrobin;
			}



			SND_ChannelTraceReset();

			// set new target for voice ducking
			float frametime = (float)soundServices.GetHostFrametime();
			S_UpdateVoiceDuck(voiceChannelCount, voiceChannelMaxVolume, frametime);

			// update x360 music volume
			g_DashboardMusicMixValue = MathLib.Approach(g_DashboardMusicMixTarget, g_DashboardMusicMixValue, g_DashboardMusicFadeRate * frametime);

			//
			// debugging output
			//
			if (snd_show.GetInt() != 0) {
				Con_NPrint_s np = default;
				np.TimeToLive = 2.0f;
				np.FixedWidthFont = true;

				int total = 0;

				ChannelList activeChannels = new();
				g_ActiveChannels.GetActiveChannels(activeChannels);
				for (int i = 0; i < activeChannels.Count; i++) {
					Channel channel = activeChannels.GetChannel(i);
					if (channel.Sfx == null)
						continue;

					np.Index = total + 2;
					if (channel.Flags.FromServer)
						np.Color = new(1.0f, 0.8f, 0.1f);
					else
						np.Color = new(0.1f, 0.9f, 1.0f);

					uint sampleCount = RemainingSamples(channel);
					float timeleft = (float)sampleCount / (float)channel.Sfx.Source!.SampleRate();
					bool bLooping = channel.Sfx.Source.IsLooped();

					if (snd_surround.GetInt() < 4) {
						soundServices.Con_NXPrintf(in np, $"{total + 1:00} l({(int)channel.FVolume[IFRONT_LEFT]:000}) r({(int)channel.FVolume[IFRONT_RIGHT]:000}) vol({channel.MasterVol:000}) ent({channel.SoundSource:000}) pos({(int)channel.Origin.X,6} {(int)channel.Origin.Y,6} {(int)channel.Origin.Z,6}) timeleft({timeleft:F6}) looped({(bLooping ? 1 : 0)}) {channel.Sfx.GetName().ToString(),50}");
					}
					else {
						soundServices.Con_NXPrintf(in np, $"{total + 1:00} l({(int)channel.FVolume[IFRONT_LEFT]:000}) c({(int)channel.FVolume[IFRONT_CENTER]:000}) r({(int)channel.FVolume[IFRONT_RIGHT]:000}) rl({(int)channel.FVolume[IREAR_LEFT]:000}) rr({(int)channel.FVolume[IREAR_RIGHT]:000}) vol({channel.MasterVol:000}) ent({channel.SoundSource:000}) pos({(int)channel.Origin.X,6} {(int)channel.Origin.Y,6} {(int)channel.Origin.Z,6}) timeleft({timeleft:F6}) looped({(bLooping ? 1 : 0)}) {channel.Sfx.GetName().ToString(),50}");
					}

					if (snd_visualize.GetInt() != 0)
						debugoverlay?.AddTextOverlay(channel.Origin, 0.05f, channel.Sfx.GetName());

					total++;
				}

				while (total <= 128) {
					soundServices.Con_NPrintf(total + 2, "");
					total++;
				}
			}
		}

#if GMOD_DLL
		if (get != null && get.Audio() != null)
			get.Audio()!.Update((uint)(long)((float)(Platform.Time - g_LastSoundFrame) * 1000.0f));
#endif

		if (s_bOnLoadScreen)
			return;

		// not time to update yet?
		double tNow = Platform.Time;
		// this is the last time we ran a sound frame
		g_LastSoundFrame = tNow;
		// this is the last time we did mixing (extraupdate also advances this if it mixes)
		g_LastMixTime = tNow;
		// mix some sound
		// try to stay at least one frame + mixahead ahead in the mix.
		g_EstFrameTime = (g_EstFrameTime * 0.9f) + ((float)soundServices.GetHostFrametime() * 0.1f);
		S_Update_(g_EstFrameTime + snd_mixahead.GetFloat());
	}

	[ConCommand(helpText: "Dump sounds to VXConsole")]
	static void snd_dumpclientsounds() {
		int total = 0;

		ChannelList list = new();
		g_ActiveChannels.GetActiveChannels(list);
		for (int i = 0; i < list.Count; i++) {
			Channel ch = list.GetChannel(i);
			if (ch.Sfx == null)
				continue;

			uint sampleCount = RemainingSamples(ch);
			float timeleft = (float)sampleCount / (float)ch.Sfx.Source!.SampleRate();
			bool bLooping = ch.Sfx.Source.IsLooped();
			ReadOnlySpan<char> pszclassname = GetClientClassname(ch.SoundSource);

			Msg($"{total + 1:00} {(ch.Flags.FromServer ? "SERVER" : "CLIENT")} l({(int)ch.FVolume[IFRONT_LEFT]:000}) c({(int)ch.FVolume[IFRONT_CENTER]:000}) r({(int)ch.FVolume[IFRONT_RIGHT]:000}) rl({(int)ch.FVolume[IREAR_LEFT]:000}) rr({(int)ch.FVolume[IREAR_RIGHT]:000}) vol({ch.MasterVol:000}) pos({(int)ch.Origin.X,6} {(int)ch.Origin.Y,6} {(int)ch.Origin.Z,6}) timeleft({timeleft:F6}) looped({(bLooping ? 1 : 0)}) {ch.Sfx.GetName().ToString(),50} chan:{ch.EntChannel} ent({ch.SoundSource:000}):{(!pszclassname.IsEmpty ? pszclassname.ToString() : "NULL")}\n");

			total++;
		}
	}

	//-----------------------------------------------------------------------------
	// Set g_soundtime to number of full samples that have been transfered out to hardware
	// since start.
	//-----------------------------------------------------------------------------
	static void GetSoundTime() {
		int fullsamples;
		int sampleOutCount;

		// size of output buffer in *full* 16 bit samples
		// A 2 channel device has a *full* sample consisting of a 16 bit LR pair.
		// A 1 channel device has a *full* sample consiting of a 16 bit single sample.
		fullsamples = g_AudioDevice!.DeviceSampleCount() / g_AudioDevice.DeviceChannels();

		// NOTE: it is possible to miscount buffers if it has wrapped twice between
		// calls to S_Update.  However, since the output buffer size is > 1 second of sound,
		// this should only occur for framerates lower than 1hz

		// sampleOutCount is counted in 16 bit *full* samples, of number of samples output to hardware
		// for current output buffer
		sampleOutCount = g_AudioDevice.GetOutputPosition();
		if (sampleOutCount < s_oldsampleOutCount) {
			// buffer wrapped
			s_buffers++;
			if (g_paintedtime > 0x70000000) {
				// time to chop things off to avoid 32 bit limits
				s_buffers = 0;
				g_paintedtime = fullsamples;
				S_StopAllSounds(true);
			}
		}

		s_oldsampleOutCount = sampleOutCount;

		if (soundServices.IsMovieRecording()) {
			// when recording a replay, we look at the record frame rate, not the engine frame rate

			{

				float t = (float)soundServices.GetHostTime();
				if (s_lastsoundtime != t) {
					g_soundtime += (int)(soundServices.GetHostFrametime() * g_AudioDevice.DeviceDmaSpeed());

					s_lastsoundtime = t;
				}
			}
		}
		else {
			// g_soundtime indicates how many *full* samples have actually been
			// played out to dma
			g_soundtime = s_buffers * fullsamples + sampleOutCount;
		}
	}

	public static void S_ExtraUpdate() {
		if (g_AudioDevice == null || soundServices == null)
			return;

		if (!g_AudioDevice.IsActive())
			return;

		if (s_bOnLoadScreen)
			return;

		if (snd_noextraupdate.GetInt() != 0 || soundServices.IsMovieRecording())
			return;     // don't pollute timings

		// If listener position and orientation has not yet been updated (ie: no call to S_Update since level load)
		// then don't mix.  Important - mixing with listener at 'false' origin causes
		// some sounds to incorrectly spatialize to 0 volume, killing them before they can play.

		if ((listener_origin == vec3_origin) &&
			(listener_forward == vec3_origin) &&
			(listener_right == vec3_origin) &&
			(listener_up == vec3_origin))
			return;

		// Only mix if you have used up 90% of the mixahead buffer
		double tNow = Platform.Time;
		double delta = (tNow - g_LastMixTime);
		// we know we were at least snd_mixahead seconds ahead of the output the last time we did mixing
		// if we're not close to running out just exit to avoid small mix batches
		if (delta > 0 && delta < (snd_mixahead.GetFloat() * 0.9))
			return;
		g_LastMixTime = tNow;

		soundServices.OnExtraUpdate();
		// Shouldn't have to do any work here if your framerate hasn't dropped
		S_Update_(snd_mixahead.GetFloat());
	}

	static void S_Update_Guts(float mixAheadTime) {
		DEBUG_StartSoundMeasure(4, 0);

		// Update our perception of audio time.
		// 'g_soundtime' tells how many samples have
		// been played out of the dma buffer since sound system startup.
		// 'g_paintedtime' indicates how many samples we've actually mixed
		// and sent to the dma buffer since sound system startup.
		GetSoundTime();

		//	if ( g_soundtime > g_paintedtime )
		//	{
		//		// if soundtime > paintedtime, then the dma buffer
		//		// has played out more sound than we've actually
		//		// mixed.  We need to call S_Update_ more often.
		//
		//		DevMsg ("S_Update_ : Underflow\n");
		//		paintedtime = g_soundtime;
		//	}
		//	(kdb) above code doesn't handle underflow correctly
		//	should actually zero out the paintbuffer to advance to the new
		//	time.

		// mix ahead of current position
		uint endtime = (uint)g_AudioDevice!.PaintBegin(mixAheadTime, g_soundtime, g_paintedtime);

		int samples = (int)endtime - g_paintedtime;
		samples = samples < 0 ? 0 : samples;
		if (samples != 0) {
			lock (g_SndMutex) {
				DEBUG_StartSoundMeasure(2, samples);

				MIX_PaintChannels((int)endtime, s_bIsListenerUnderwater);

				MXR_DebugShowMixVolumes();

				MXR_UpdateAllDuckerVolumes();

				DEBUG_StopSoundMeasure(2, 0);
			}
		}

		g_AudioDevice.PaintEnd();
		DEBUG_StopSoundMeasure(4, samples);
	}

	const int THREADED_MIX_TIME = 33;

	public static readonly ConVar snd_ShowThreadFrameTime = new("snd_ShowThreadFrameTime", "0");

	static volatile bool g_bMixThreadExit = false;
	static Thread? g_hMixThread;
	static void S_Update_Thread() {
		float frameTime = THREADED_MIX_TIME * 0.001f;
		double lastFrameTime = Platform.Time;

		while (!g_bMixThreadExit) {
			double t0 = Platform.Time;

			S_Update_Guts(frameTime + snd_mixahead.GetFloat());

			double updateTime = (Platform.Time - t0) * 1000.0;
			// try to maintain a steadier rate by compensating for fluctuating mix times
			double sleepTime = THREADED_MIX_TIME - updateTime;
			if (sleepTime > 0)
				Thread.Sleep((int)sleepTime);

			// mimic a frametime needed for sound update
			double t1 = Platform.Time;
			frameTime = (float)(t1 - lastFrameTime);
			lastFrameTime = t1;

			if (snd_ShowThreadFrameTime.GetBool())
				Msg($"S_Update_Thread: frameTime: {frameTime * 1000.0f:F2} ms\n");
		}
	}

	public static void S_ShutdownMixThread() {
		if (g_hMixThread != null) {
			g_bMixThreadExit = true;
			g_hMixThread.Join();
			g_hMixThread = null;
		}
	}

	static void S_Update_(float mixAheadTime) {
		if (g_hMixThread == null) {
			g_bMixThreadExit = false;
			g_hMixThread = new Thread(S_Update_Thread) { Name = "SndMix", IsBackground = true };
			g_hMixThread.Start();
		}
	}

	/*
	===============================================================================

	console functions

	===============================================================================
	*/

	static void S_DspParms(in TokenizedCommand args) {
		if (args.ArgC() == 1) {
			// if dsp_parms with no arguments, reload entire preset file

			DSP_DEBUGReloadPresetFile();

			return;
		}

		if (args.ArgC() < 4) {
			Msg("Usage: dsp_parms PRESET# PROC# param0 param1 ...up to param15 \n");
			return;
		}

		int cparam = Math.Min(args.ArgC() - 4, 16);

		Span<float> parms = stackalloc float[16];
		for (int i = 0; i < 16; i++)
			parms[i] = 0;

		// get preset & proc
		int idsp, iproc;
		idsp = atoi(args[1]);
		iproc = atoi(args[2]);

		// get params
		for (int i = 0; i < cparam; i++)
			parms[i] = strtof(args[i + 4]);

		// set up params & switch preset
		DSP_DEBUGSetParams(idsp, iproc, parms, cparam);
	}

	[ConCommand("dsp_reload")]
	static void dsp_parm(in TokenizedCommand args) => S_DspParms(in args);

	static void S_Play(ReadOnlySpan<char> pszName, bool flush = false) {
		int inCache = 0;
		SfxTable pSfx;

		string szName = new(pszName);
		if (pszName.LastIndexOf('.') == -1)
			szName += ".wav";

		pSfx = S_FindName(szName, new Span<int>(ref inCache));
		if (inCache != 0 && flush)
			pSfx.Source!.CacheUnload();

		StartSoundParams parms = new();
		parms.StaticSound = false;
		parms.SoundSource = soundServices.GetViewEntity();
		parms.EntChannel = SoundEntityChannel.Replace;
		parms.Sfx = pSfx;
		parms.Origin = listener_origin;
		parms.Volume = 1.0f;
		parms.SoundLevel = SoundLevel.LvlNone;
		parms.Flags = 0;
		parms.Pitch = PITCH_NORM;

		S_StartSound(ref parms);
	}

	static void S_Play(in TokenizedCommand args) {
		bool bFlush = stricmp(args[0], "playflush") == 0;
		for (int i = 1; i < args.ArgC(); ++i)
			S_Play(args[i], bFlush);
	}

	static int S_PlayVol_hash = 543;
	static void S_PlayVol(in TokenizedCommand args) {
		float vol;
		string name;
		SfxTable? pSfx;

		for (int i = 1; i < args.ArgC(); i += 2) {
			if (args[i].LastIndexOf('.') == -1)
				name = new string(args[i]) + ".wav";
			else
				name = new(args[i]);

			pSfx = S_PrecacheSound(name);
			vol = strtof(args[i + 1]);

			StartSoundParams parms = new();
			parms.StaticSound = false;
			parms.SoundSource = S_PlayVol_hash++;
			parms.EntChannel = SoundEntityChannel.Auto;
			parms.Sfx = pSfx;
			parms.Origin = listener_origin;
			parms.Volume = vol;
			parms.SoundLevel = SoundLevel.LvlNone;
			parms.Flags = 0;
			parms.Pitch = PITCH_NORM;

			S_StartDynamicSound(ref parms);
		}
	}

	[ConCommand(helpText: "Usage:  sndplaydelay delay_in_sec (negative to skip ahead) soundname", flags: FCvar.ServerCanExecute)]
	static void sndplaydelay(in TokenizedCommand args) {
		if (args.ArgC() != 3) {
			Msg("Usage:  sndplaydelay delay_in_sec (negative to skip ahead) soundname\n");
			return;
		}

		SfxTable pSfx;

		float delay = strtof(args[1]);

		string szName = new(args[2]);
		if (args[2].LastIndexOf('.') == -1)
			szName += ".wav";

		pSfx = S_FindName(szName, null);

		StartSoundParams parms = new();
		parms.StaticSound = false;
		parms.SoundSource = soundServices.GetViewEntity();
		parms.EntChannel = SoundEntityChannel.Replace;
		parms.Sfx = pSfx;
		parms.Origin = listener_origin;
		parms.Volume = 1.0f;
		parms.SoundLevel = SoundLevel.LvlNone;
		parms.Flags = 0;
		parms.Pitch = PITCH_NORM;
		parms.Delay = delay;

		S_StartSound(ref parms);

	}

	static void S_SoundList() {
		SfxTable sfx;
		AudioSource? pSource;
		int size, total;

		total = 0;
		foreach (var kvp in s_Sounds) {
			sfx = kvp.Value;

			pSource = sfx.Source;
			if (pSource == null || !pSource.IsCached())
				continue;

			size = pSource.SampleSize() * pSource.SampleCount();
			total += size;

			if (pSource.IsLooped())
				Msg("L");
			else
				Msg(" ");
			Msg($"({pSource.SampleSize(),2}b) {size,6} : {sfx.GetName()}\n");
		}
		Msg($"Total resident: {total}\n");
	}

	// start measuring sound perf, 100 reps
	// type 1 - dsp, 2 - mix, 3 - load sound, 4 - all sound
	// set type via ConVar snd_profile

	public static void DEBUG_StartSoundMeasure(int type, int samplecount) {
		if (type != g_snd_profile_type)
			return;

		if (samplecount != 0)
			g_snd_samplecount += (uint)samplecount;

		g_snd_call_time_debug = (uint)Platform.MSTime;
	}

	// show sound measurement after 25 reps - show as % of total frame
	// type 1 - dsp, 2 - mix, 3 - load sound, 4 - all sound

	// BUGBUG: snd_profile 4 reports a lower average because it's average cost
	// PER CALL and most calls (via SoundExtraUpdate()) don't do any work and
	// bring the average down.  If you want an average PER FRAME instead, it's generally higher.
	public static void DEBUG_StopSoundMeasure(int type, int samplecount) {
		if (type != g_snd_profile_type)
			return;

		if (samplecount != 0)
			g_snd_samplecount += (uint)samplecount;

		// add total time since last frame

		g_snd_frametime_total += (uint)Platform.MSTime - g_snd_frametime;

		// performance timing

		g_snd_time_debug += (uint)Platform.MSTime - g_snd_call_time_debug;

		if (++g_snd_count_debug >= 100) {
			switch (g_snd_profile_type) {
				case 1:
					Msg($"dsp: ({((float)g_snd_time_debug) / 100.0:F2}) millisec   ");
					Msg($"({100.0 * ((float)g_snd_time_debug) / ((float)g_snd_frametime_total):F2}) pct of frame \n");
					break;
				case 2:
					Msg($"mix+dsp:({((float)g_snd_time_debug) / 100.0:F2}) millisec   ");
					Msg($"({100.0 * ((float)g_snd_time_debug) / ((float)g_snd_frametime_total):F2}) pct of frame \n");
					break;
				case 3:
					//if ( (((float)g_snd_time_debug) / 100.0) < 0.01 )
					//	break;
					Msg($"snd load: ({((float)g_snd_time_debug) / 100.0:F2}) millisec   ");
					Msg($"({100.0 * ((float)g_snd_time_debug) / ((float)g_snd_frametime_total):F2}) pct of frame \n");
					break;
				case 4:
					Msg($"sound: ({((float)g_snd_time_debug) / 100.0:F2}) millisec   ");
					Msg($"({100.0 * ((float)g_snd_time_debug) / ((float)g_snd_frametime_total):F2}) pct of frame ({g_snd_samplecount} samples) \n");
					break;
			}

			g_snd_count_debug = 0;
			g_snd_time_debug = 0;
			g_snd_samplecount = 0;
			g_snd_frametime_total = 0;
		}

		g_snd_frametime = (uint)Platform.MSTime;
	}

	// speak a sentence from console; works by passing in "!sentencename"
	// or "sentence"

	static int S_Say_hash = 543;
	static void S_Say(in TokenizedCommand args) {
		SfxTable? pSfx;

		if (!g_AudioDevice!.IsActive())
			return;

		string sound = new(args[1]);

		// DEBUG - test performance of dsp code
		if (stricmp(sound, "dsp") == 0) {
			uint time;
			int i;
			int count = 10000;
			int idsp;

			for (i = 0; i < PAINTBUFFER_SIZE; i++) {
				g_paintbuffer[i].Left = RandomInt(0, 2999);
				g_paintbuffer[i].Right = RandomInt(0, 2999);
			}

			Msg("Start profiling 10,000 calls to DSP\n");

			idsp = dsp_room.GetInt();

			// get system time

			time = (uint)Platform.MSTime;

			for (i = 0; i < count; i++) {
				// SX_RoomFX(PAINTBUFFER_SIZE, TRUE, TRUE);

				DSP_Process(idsp, g_paintbuffer, null, null, PAINTBUFFER_SIZE);

			}
			// display system time delta
			Msg($"{(uint)Platform.MSTime - time} milliseconds \n");
			return;
		}

		if (stricmp(sound, "paint") == 0) {
			uint time;
			int count = 10000;
			int psav = g_paintedtime;

			Msg("Start profiling MIX_PaintChannels\n");

			pSfx = S_PrecacheSound("ambience/labdrone1.wav");

			StartSoundParams parms = new();
			parms.StaticSound = false;
			parms.SoundSource = S_Say_hash++;
			parms.EntChannel = SoundEntityChannel.Auto;
			parms.Sfx = pSfx;
			parms.Origin = listener_origin;
			parms.Volume = 1.0f;
			parms.SoundLevel = SoundLevel.LvlNone;
			parms.Flags = 0;
			parms.Pitch = PITCH_NORM;

			S_StartDynamicSound(ref parms);

			// get system time
			time = (uint)Platform.MSTime;

			// paint a boatload of sound

			MIX_PaintChannels(g_paintedtime + 512 * count, s_bIsListenerUnderwater);

			// display system time delta
			Msg($"{(uint)Platform.MSTime - time} milliseconds \n");
			g_paintedtime = psav;
			return;
		}

		// DEBUG
		if (!TestSoundChar(sound, SoundChars.Sentence)) {
			// build a fake sentence name, then play the sentence text

			sound = "xxtestxx " + new string(args[1]);

			// insert null terminator after sentence name
			VOX_AddTempSentence(sound[..8], sound[9..]);

			pSfx = S_PrecacheSound("!xxtestxx");
			if (pSfx == null) {
				Msg($"S_Say: can't cache {sound}\n");
				return;
			}

			StartSoundParams parms = new();
			parms.StaticSound = false;
			parms.SoundSource = soundServices.GetViewEntity();
			parms.EntChannel = SoundEntityChannel.Replace;
			parms.Sfx = pSfx;
			parms.Origin = vec3_origin;
			parms.Volume = 1.0f;
			parms.SoundLevel = SoundLevel.LvlNone;
			parms.Flags = 0;
			parms.Pitch = PITCH_NORM;

			S_StartDynamicSound(ref parms);

			// remove last
			VOX_RemoveLastSentence();
		}
		else {
			pSfx = S_FindName(sound, null);
			if (pSfx == null) {
				Msg($"S_Say: can't find sentence name {sound}\n");
				return;
			}

			StartSoundParams parms = new();
			parms.StaticSound = false;
			parms.SoundSource = soundServices.GetViewEntity();
			parms.EntChannel = SoundEntityChannel.Replace;
			parms.Sfx = pSfx;
			parms.Origin = vec3_origin;
			parms.Volume = 1.0f;
			parms.SoundLevel = SoundLevel.LvlNone;
			parms.Flags = 0;
			parms.Pitch = PITCH_NORM;

			S_StartDynamicSound(ref parms);
		}
	}

	public static float S_GetMono16Samples(ReadOnlySpan<char> pszName, List<short> sampleList) {
		SfxTable? pSfx = S_PrecacheSound(SkipSoundChars(pszName));
		if (pSfx == null)
			return 0.0f;

		AudioSourceBase? pWave = pSfx.GetSource();
		if (pWave == null)
			return 0.0f;

		AudioSourceType nType = pWave.GetAudioSourceType();
		if (nType != AudioSourceType.AUDIO_SOURCE_WAV)
			return 0.0f;

		AudioMixer? pMixer = pWave.CreateMixer();
		if (pMixer == null)
			return 0.0f;

		float duration = AudioSource_GetSoundDuration(pSfx);

		// Determine start/stop positions
		int totalsamples = (int)(duration * pWave.SampleRate());
		if (totalsamples <= 0)
			return 0;

		bool bStereo = pWave.IsStereoWav();
		int mix_sample_size = pMixer.GetMixSampleSize();
		int nNumChannels = bStereo ? 2 : 1;

		int pos = 0;
		int remaining = totalsamples;
		Span<byte> copyBuf = stackalloc byte[AudioSource.AUDIOSOURCE_COPYBUF_SIZE];
		while (remaining > 0) {
			int blockSize = Math.Min(remaining, 1000);

			int copied = pWave.GetOutputData(out ReadOnlySpan<byte> pData, pos, blockSize, copyBuf);
			if (copied == 0)
				break;

			remaining -= copied;
			pos += copied;

			// Now get samples out of output data
			switch (nNumChannels) {
				default:
				case 1: {
						for (int i = 0; i < copied; ++i) {
							int offset = i * mix_sample_size;

							short sample = 0;
							if (mix_sample_size == 1) {
								sbyte s = (sbyte)pData[offset];
								// Upscale it to fit into a short
								sample = (short)(s << 8);
							}
							else if (mix_sample_size == 2)
								sample = BinaryPrimitives.ReadInt16LittleEndian(pData[offset..]);
							else if (mix_sample_size == 4) {
								// Not likely to have 4 bytes mono!!!
								Assert(false);

								int s = BinaryPrimitives.ReadInt32LittleEndian(pData[offset..]);
								sample = (short)(s >> 16);
							}
							else
								Assert(false);

							sampleList.Add(sample);
						}
					}
					break;

				case 2: {
						for (int i = 0; i < copied; ++i) {
							int offset = i * mix_sample_size;

							short left = 0;
							short right = 0;

							if (mix_sample_size == 1) {
								// Not possible!!!, must be at least 2 bytes!!!
								Assert(false);

								sbyte v = (sbyte)pData[offset];
								left = right = (short)(v << 8);
							}
							else if (mix_sample_size == 2) {
								// One byte per channel
								left = (short)(((sbyte)pData[offset]) << 8);
								right = (short)(((sbyte)pData[offset + 1]) << 8);
							}
							else if (mix_sample_size == 4) {
								// 2 bytes per channel
								left = BinaryPrimitives.ReadInt16LittleEndian(pData[offset..]);
								right = BinaryPrimitives.ReadInt16LittleEndian(pData[(offset + 2)..]);
							}
							else
								Assert(false);

							short sample = (short)((left + right) >> 1);
							sampleList.Add(sample);
						}
					}
					break;
			}
		}

		pMixer.Dispose();

		return duration;
	}
}
