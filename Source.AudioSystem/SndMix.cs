global using static Source.AudioSystem.SndMix;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.AudioSystem;

public static class SndMix
{
	// NOTE: !!!!!! YOU MUST UPDATE SND_MIXA.S IF THIS VALUE IS CHANGED !!!!!
	const int SND_SCALE_BITS = 7;
	const int SND_SCALE_SHIFT = 8 - SND_SCALE_BITS;
	const int SND_SCALE_LEVELS = 1 << SND_SCALE_BITS;

	const int SND_SCALE_BITS16 = 8;
	const int SND_SCALE_SHIFT16 = 8 - SND_SCALE_BITS16;
	const int SND_SCALE_LEVELS16 = 1 << SND_SCALE_BITS16;

	public static PortableSamplePair[] g_paintbuffer = null!;

	// temp paintbuffer - not included in main list of paintbuffers
	// NOTE: this paintbuffer is also used as a copy buffer by interpolating pitch
	// shift routines.  Decreasing TEMP_COPY_BUFFER_SIZE (or PAINTBUFFER_MEM_SIZE)
	// will decrease the maximum pitch level (current 4.0)!
	public static PortableSamplePair[] g_temppaintbuffer = null!;

	public static readonly List<PaintBuffer> g_paintBuffers = [];

	// pointer to current paintbuffer (front and reare), used by all mixing, upsampling and dsp routines
	public static PortableSamplePair[]? g_curpaintbuffer = null;
	public static PortableSamplePair[]? g_currearpaintbuffer = null;
	public static PortableSamplePair[]? g_curcenterpaintbuffer = null;

	public static bool g_bdirectionalfx;
	public static bool g_bDspOff;
	public static float g_dsp_volume;

	// dsp performance timing
	public static uint g_snd_call_time_debug = 0;
	public static uint g_snd_time_debug = 0;
	public static uint g_snd_count_debug = 0;
	public static uint g_snd_samplecount = 0;
	public static uint g_snd_frametime = 0;
	public static uint g_snd_frametime_total = 0;
	public static int g_snd_profile_type = 0;       // type 1 dsp, type 2 mixer, type 3 load sound, type 4 all sound

	public const int FILTERTYPE_NONE = 0;
	public const int FILTERTYPE_LINEAR = 1;
	public const int FILTERTYPE_CUBIC = 2;

	static readonly int[] snd_scaletable = new int[SND_SCALE_LEVELS * 256];   // 32k*4 = 128K

	static int snd_linear_count;
	static int snd_vol;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static ReadOnlySpan<int> SndScaleTable(int level) => new(snd_scaletable, level * 256, 256);

	public static bool IsReplayRendering() {
		return false;
	}

	//-----------------------------------------------------------------------------
	// Free allocated memory buffers
	//-----------------------------------------------------------------------------
	public static void MIX_FreeAllPaintbuffers() {
		if (g_paintBuffers.Count != 0) {
			g_temppaintbuffer = null!;

			g_paintBuffers.Clear();
		}
	}

	static PortableSamplePair[] AllocPaintbuffer(int count) {
		return new PortableSamplePair[count];
	}

	public static void MIX_InitializePaintbuffer(PaintBuffer paintBuffer, bool surround, bool surroundCenter) {
		paintBuffer.Active = false;
		paintBuffer.Surround = false;
		paintBuffer.SurroundCenter = false;
		paintBuffer.IdspSpecialDsp = 0;
		paintBuffer.PrevSpecialDSP = 0;
		paintBuffer.SpecialDSP = 0;
		paintBuffer.Flags = 0;
		paintBuffer.BufRear = null;
		paintBuffer.BufCenter = null;
		paintBuffer.IFilter = 0;

		paintBuffer.Buf = AllocPaintbuffer(PAINTBUFFER_MEM_SIZE);

		if (surround)
			paintBuffer.BufRear = AllocPaintbuffer(PAINTBUFFER_MEM_SIZE);
		if (surroundCenter)
			paintBuffer.BufCenter = AllocPaintbuffer(PAINTBUFFER_MEM_SIZE);
	}

	//-----------------------------------------------------------------------------
	// Allocate memory buffers
	// Initialize paintbuffers array, set current paint buffer to main output buffer SOUND_BUFFER_PAINT
	//-----------------------------------------------------------------------------
	public static bool MIX_InitAllPaintbuffers() {
		bool surround;
		bool surroundCenter;

		surroundCenter = g_AudioDevice!.IsSurroundCenter();
		surround = g_AudioDevice.IsSurround() || surroundCenter;

		g_temppaintbuffer = AllocPaintbuffer(TEMP_COPY_BUFFER_SIZE);

		while (g_paintBuffers.Count < SOUND_BUFFER_BASETOTAL) {
			PaintBuffer paintBuffer = new();
			g_paintBuffers.Add(paintBuffer);
			MIX_InitializePaintbuffer(paintBuffer, surround, surroundCenter);
		}

		g_paintbuffer = g_paintBuffers[SOUND_BUFFER_PAINT].Buf;

		// buffer flags
		g_paintBuffers[SOUND_BUFFER_ROOM].Flags = SOUND_BUSS_ROOM;
		g_paintBuffers[SOUND_BUFFER_FACING].Flags = SOUND_BUSS_FACING;
		g_paintBuffers[SOUND_BUFFER_FACINGAWAY].Flags = SOUND_BUSS_FACINGAWAY;
		g_paintBuffers[SOUND_BUFFER_SPEAKER].Flags = SOUND_BUSS_SPEAKER;
		g_paintBuffers[SOUND_BUFFER_DRY].Flags = SOUND_BUSS_DRY;

		// buffer surround sound flag
		g_paintBuffers[SOUND_BUFFER_PAINT].Surround = surround;
		g_paintBuffers[SOUND_BUFFER_FACING].Surround = surround;
		g_paintBuffers[SOUND_BUFFER_FACINGAWAY].Surround = surround;
		g_paintBuffers[SOUND_BUFFER_DRY].Surround = surround;

		// buffer 5 channel surround sound flag
		g_paintBuffers[SOUND_BUFFER_PAINT].SurroundCenter = surroundCenter;
		g_paintBuffers[SOUND_BUFFER_FACING].SurroundCenter = surroundCenter;
		g_paintBuffers[SOUND_BUFFER_FACINGAWAY].SurroundCenter = surroundCenter;
		g_paintBuffers[SOUND_BUFFER_DRY].SurroundCenter = surroundCenter;

		// room buffer mixes down to mono or stereo, never to 4 or 5 ch
		g_paintBuffers[SOUND_BUFFER_ROOM].Surround = false;
		g_paintBuffers[SOUND_BUFFER_ROOM].SurroundCenter = false;

		// speaker buffer mixes to mono
		g_paintBuffers[SOUND_BUFFER_SPEAKER].Surround = false;
		g_paintBuffers[SOUND_BUFFER_SPEAKER].SurroundCenter = false;

		MIX_SetCurrentPaintbuffer(SOUND_BUFFER_PAINT);

		return true;
	}

	// called before loading samples to mix  - cap the mix rate (ie: pitch) so that
	// we never overflow the mix copy buffer.

	public static double MIX_GetMaxRate(double rate, int sampleCount) {
		if (rate <= 2.0)
			return rate;

		// copybuf_bytes = rate_max * samples_max * samplesize_max
		// so:
		// rate_max = copybuf_bytes /  (samples_max * samplesize_max )

		double samplesize_max = 4.0; // stereo 16bit samples
		double copybuf_bytes = (double)(TEMP_COPY_BUFFER_SIZE * PortableSamplePair.SIZE);
		double samples_max = (double)PAINTBUFFER_SIZE;

		double rate_max = copybuf_bytes / (samples_max * samplesize_max);

		// make sure sampleCount is never greater than paintbuffer samples
		// (this should have been set up in MIX_PaintChannels)

		Assert(sampleCount <= PAINTBUFFER_SIZE);

		return Math.Min(rate, rate_max);
	}

	// Transfer (endtime - lpaintedtime) stereo samples in pfront out to hardware
	// pfront - pointer to stereo paintbuffer - 32 bit samples, interleaved stereo
	// lpaintedtime - total number of 32 bit stereo samples previously output to hardware
	// endtime - total number of 32 bit stereo samples currently mixed in paintbuffer
	public static void S_TransferStereo16(Span<short> output, PortableSamplePair[] front, int lpaintedtime, int endtime) {
		int lpos;

		Assert(!output.IsEmpty);

		snd_vol = IsOutputMuted ? 0 : (int)(S_GetMasterVolume() * 256);
		ReadOnlySpan<int> snd_p = MemoryMarshal.Cast<PortableSamplePair, int>(front.AsSpan());

		// get size of output buffer in full samples (LR pairs)
		int samplePairCount = g_AudioDevice!.DeviceSampleCount() >> 1;
		int sampleMask = samplePairCount - 1;

		bool shouldPlaySound = !soundServices.IsMovieRecording() && !IsReplayRendering();

		while (lpaintedtime < endtime) {
			// pbuf can hold 16384, 16 bit L/R samplepairs.
			// lpaintedtime - where to start painting into dma buffer.
			// (modulo size of dma buffer for current position).
			// handle recirculating buffer issues
			// lpos - samplepair index into dma buffer. First samplepair from paintbuffer to be xfered here.
			lpos = lpaintedtime & sampleMask;

			// snd_out is L/R sample index into dma buffer.  First L sample from paintbuffer goes here.
			Span<short> snd_out = output[(lpos << 1)..];

			// snd_linear_count is number of samplepairs between end of dma buffer and xfer start index.
			snd_linear_count = samplePairCount - lpos;

			// clamp snd_linear_count to be only as many samplepairs premixed
			if (snd_linear_count > endtime - lpaintedtime) {
				// endtime - lpaintedtime = number of premixed sample pairs ready for xfer.
				snd_linear_count = endtime - lpaintedtime;
			}

			// snd_linear_count is now number of mono 16 bit samples (L and R) to xfer.
			snd_linear_count <<= 1;

			// write a linear blast of samples
			SND_RecordBuffer(snd_p);
			if (shouldPlaySound) {
				// transfer 16bit samples from snd_p into snd_out, multiplying each sample by volume.
				Snd_WriteLinearBlastStereo16(snd_p, snd_out);
			}

			// advance paintbuffer pointer
			snd_p = snd_p[snd_linear_count..];

			// advance lpaintedtime by number of samplepairs just xfered.
			lpaintedtime += snd_linear_count >> 1;
		}
	}

	// Transfer contents of main paintbuffer pfront out to
	// device.  Perform volume multiply on each sample.
	public static void S_TransferPaintBuffer(Span<byte> output, PortableSamplePair[] front, int lpaintedtime, int endtime) {
		int out_idx;        // mono sample index
		int count;          // number of mono samples to output
		int out_mask;
		int step;
		int val;
		int soundVol;
		int p;

		Assert(!output.IsEmpty);

		ReadOnlySpan<int> pfront = MemoryMarshal.Cast<PortableSamplePair, int>(front.AsSpan());
		p = 0;

		count = (endtime - lpaintedtime) * g_AudioDevice!.DeviceChannels();

		out_mask = g_AudioDevice.DeviceSampleCount() - 1;

		// 44k: remove old 22k sound support << HISPEED_DMA
		// out_idx = ((paintedtime << HISPEED_DMA) * g_AudioDevice->DeviceChannels()) & out_mask;

		out_idx = (lpaintedtime * g_AudioDevice.DeviceChannels()) & out_mask;

		step = 3 - g_AudioDevice.DeviceChannels();  // mono output buffer - step 2, stereo - step 1
		soundVol = IsOutputMuted ? 0 : (int)(S_GetMasterVolume() * 256);

		if (g_AudioDevice.DeviceSampleBits() == 16) {
			Span<short> @out = MemoryMarshal.Cast<byte, short>(output);
			while (count-- != 0) {
				val = (pfront[p] * soundVol) >> 8;
				p += step;
				val = CLIP(val);

				@out[out_idx] = (short)val;
				out_idx = (out_idx + 1) & out_mask;
			}
		}
		else if (g_AudioDevice.DeviceSampleBits() == 8) {
			Span<byte> @out = output;
			while (count-- != 0) {
				val = (pfront[p] * soundVol) >> 8;
				p += step;
				val = CLIP(val);

				@out[out_idx] = (byte)((val >> 8) + 128);
				out_idx = (out_idx + 1) & out_mask;
			}
		}
	}

	/*
	===============================================================================

	CHANNEL MIXING

	===============================================================================
	*/


	// free channel so that it may be allocated by the
	// next request to play a sound.  If sound is a
	// word in a sentence, release the sentence.
	// Works for static, dynamic, sentence and stream sounds

	public static void S_FreeChannel(Channel ch) {
		// Don't reenter in here (can happen inside voice code).
		if (ch.Flags.IsFreeingChannel)
			return;
		ch.Flags.IsFreeingChannel = true;

		SND_CloseMouth(ch);

		soundServices.OnSoundStopped(ch.Guid, ch.SoundSource, (SoundEntityChannel)ch.EntChannel, ch.Sfx!.GetName());

		ch.Flags.IsSentence = false;

		ch.Mixer?.Dispose();
		ch.Mixer = null;
		ch.Sfx = null;

		// zero all data in channel
		g_ActiveChannels.Remove(ch);
		ch.Clear();
	}

	// Mix all channels into active paintbuffers until paintbuffer is full or 'endtime' is reached.
	// endtime: time in 44khz samples to mix
	// rate: ignore samples which are not natively at this rate (for multipass mixing/filtering)
	//		 if rate == SOUND_ALL_RATES then mix all samples this pass
	// flags: if SOUND_MIX_DRY, then mix only samples with channel flagged as 'dry'
	// outputRate: target mix rate for all samples.  Note, if outputRate = SOUND_DMA_SPEED, then
	//		 this routine will fill the paintbuffer to endtime.  Otherwise, fewer samples are mixed.
	//		 if (endtime - paintedtime) is not aligned on boundaries of 4,
	//		 we'll miss data if outputRate < SOUND_DMA_SPEED!
	public static void MIX_MixChannelsToPaintbuffer(ChannelList list, int endtime, int flags, int rate, int outputRate) {
		int i;
		int sampleCount;

		// mix each channel into paintbuffer
		// validate parameters
		Assert(outputRate <= SOUND_DMA_SPEED);
		Assert(((endtime - g_paintedtime) & 0x3) == 0 || (outputRate == SOUND_DMA_SPEED)); // make sure we're not discarding data

		// 44k: try to mix this many samples at outputRate
		sampleCount = (endtime - g_paintedtime) / (SOUND_DMA_SPEED / outputRate);
		if (sampleCount <= 0)
			return;

		// Apply a global pitch shift if we're playing back a time-scaled replay
		float globalPitchScale = 1.0f;

		for (i = list.Count; --i >= 0;) {
			Channel ch = list.GetChannel(i);
			Assert(ch.Sfx);
			// must never have a 'dry' and 'speaker' set - causes double mixing & double data reading
			Assert(!((ch.Flags.Dry && ch.Flags.Speaker) || (ch.Flags.Dry && ch.SpecialDsp != 0)));

			// if mixing with SOUND_MIX_DRY flag, ignore (don't even load) all channels not flagged as 'dry'
			if (flags == SOUND_MIX_DRY) {
				if (!ch.Flags.Dry)
					continue;
			}

			// if mixing with SOUND_MIX_WET flag, ignore (don't even load) all channels flagged as 'dry' or 'speaker'
			if (flags == SOUND_MIX_WET) {
				if (ch.Flags.Dry || ch.Flags.Speaker || ch.SpecialDsp != 0)
					continue;
			}

			// if mixing with SOUND_MIX_SPEAKER flag, ignore (don't even load) all channels not flagged as 'speaker'
			if (flags == SOUND_MIX_SPEAKER) {
				if (!ch.Flags.Speaker)
					continue;
			}

			// if mixing with SOUND_MIX_SPEAKER flag, ignore (don't even load) all channels not flagged as 'speaker'
			if (flags == SOUND_MIX_SPECIAL_DSP) {
				if (ch.SpecialDsp == 0)
					continue;
			}

			// multipass mixing - only mix samples of specified sample rate
			switch (rate) {
				case SOUND_11k:
				case SOUND_22k:
				case SOUND_44k:
					if (rate != ch.Sfx!.GetSource()!.SampleRate())
						continue;
					break;
				default:
				case SOUND_ALL_RATES:
					break;
			}

			// Tracker 20771, if breen is speaking through the monitor, the client doesn't have an entity
			//  for the "soundsource" but we still need the lipsync to pause if the game is paused.  Therefore
			//  I changed SND_IsMouth to look for any .wav on any channels which has sentence data
			bool isMouth = SND_IsMouth(ch);
			bool shouldPause = isMouth;

			// Tracker 14637:  Pausing the game pauses voice sounds, but not other sounds...
			if (shouldPause && soundServices.IsGamePaused())
				continue;

			if (isMouth) {
				IClientEntityList? entitylist = EntityList;
				if ((ch.SoundSource == SOUND_FROM_UI_PANEL) || entitylist?.GetClientEntity(ch.SoundSource) != null ||
					(ch.Flags.Speaker && entitylist?.GetClientEntity(ch.SpeakerEntity) != null)) {
					// UNDONE: recode this as a member function of CAudioMixer
					SND_MoveMouth8(ch, ch.Sfx!.GetSource()!, sampleCount);
				}
			}

			// mix channel to all active paintbuffers:
			// mix 'dry' sounds only to dry paintbuffer.
			// mix 'speaker' sounds only to speaker paintbuffer.
			// mix all other sounds between room, facing & facingaway paintbuffers
			// NOTE: must be called once per channel only - consecutive calls retrieve additional data.
			float pitch = ch.Pitch;
			ch.Pitch *= globalPitchScale;

			if (list.IsQuashed(i)) {
				// If the sound has been silenced as a performance heuristic, quash it.
				ch.Mixer!.SkipSamples(ch, sampleCount, outputRate, 0);
			}
			else
				ch.Mixer!.MixDataToDevice(g_AudioDevice!, ch, sampleCount, outputRate, 0);

			// restore to original pitch settings
			ch.Pitch = pitch;

			if (!ch.Mixer.ShouldContinueMixing()) {
				S_FreeChannel(ch);
				list.RemoveChannelFromList(i);
			}
			if (ch.FreeChannelAtSampleTime > 0 && (int)ch.FreeChannelAtSampleTime <= endtime) {
				S_FreeChannel(ch);
				list.RemoveChannelFromList(i);
			}
		}
	}

	// pass in index -1...count+2, return pointer to source sample in either paintbuffer or delay buffer
	static PortableSamplePair S_GetNextpFilter(int i, ReadOnlySpan<PortableSamplePair> buffer, ReadOnlySpan<PortableSamplePair> filtermem) {
		// The delay buffer is assumed to precede the paintbuffer by 6 duplicated samples
		if (i == -1)
			return filtermem[0];
		if (i == 0)
			return filtermem[1];
		if (i == 1)
			return filtermem[2];

		// return from paintbuffer, where samples are doubled.
		// even samples are to be replaced with interpolated value.

		return buffer[(i - 2) * 2 + 1];
	}

	// pass forward over passed in buffer and cubic interpolate all odd samples
	// pbuffer: buffer to filter (in place)
	// prevfilter:  filter memory. NOTE: this must match the filtertype ie: filtercubic[] for FILTERTYPE_CUBIC
	//				if NULL then perform no filtering. UNDONE: should have a filter memory array type
	// count: how many samples to upsample. will become count*2 samples in buffer, in place.

	public static void S_Interpolate2xCubic(Span<PortableSamplePair> buffer, Span<PortableSamplePair> filtermem, int cfltmem, int count) {

		// implement cubic interpolation on 2x upsampled buffer.   Effectively delays buffer contents by 2 samples.
		// pbuffer: contains samples at 0, 2, 4, 6...
		// temppaintbuffer is temp buffer, of same or larger size than a paintbuffer, used to store processed values
		// count: number of samples to process in buffer ie: how many samples at 0, 2, 4, 6...

		// finpos is the fractional, inpos the integer part.
		//		finpos = 0.5 for upsampling by 2x
		//		inpos is the position of the sample

		//		xm1 = x [inpos - 1];
		//		x0 = x [inpos + 0];
		//		x1 = x [inpos + 1];
		//		x2 = x [inpos + 2];
		//		a = (3 * (x0-x1) - xm1 + x2) / 2;
		//		b = 2*x1 + xm1 - (5*x0 + x2) / 2;
		//		c = (x1 - xm1) / 2;
		//		y [outpos] = (((a * finpos) + b) * finpos + c) * finpos + x0;

		int i, upCount = count << 1;
		int a, b, c;
		int xm1, x0, x1, x2;
		PortableSamplePair psamp0;
		PortableSamplePair psamp1;
		PortableSamplePair psamp2;
		PortableSamplePair psamp3;
		int outpos = 0;

		Assert(upCount <= PAINTBUFFER_SIZE);

		// pfiltermem holds 6 samples from previous buffer pass

		// process 'count' samples

		for (i = 0; i < count; i++) {

			// get source sample pointer

			psamp0 = S_GetNextpFilter(i - 1, buffer, filtermem);
			psamp1 = S_GetNextpFilter(i, buffer, filtermem);
			psamp2 = S_GetNextpFilter(i + 1, buffer, filtermem);
			psamp3 = S_GetNextpFilter(i + 2, buffer, filtermem);

			// write out original sample to interpolation buffer

			g_temppaintbuffer[outpos++] = psamp1;

			// get all left samples for interpolation window

			xm1 = psamp0.Left;
			x0 = psamp1.Left;
			x1 = psamp2.Left;
			x2 = psamp3.Left;

			// interpolate

			a = (3 * (x0 - x1) - xm1 + x2) / 2;
			b = 2 * x1 + xm1 - (5 * x0 + x2) / 2;
			c = (x1 - xm1) / 2;

			// write out interpolated sample

			g_temppaintbuffer[outpos].Left = a / 8 + b / 4 + c / 2 + x0;

			// get all right samples for window

			xm1 = psamp0.Right;
			x0 = psamp1.Right;
			x1 = psamp2.Right;
			x2 = psamp3.Right;

			// interpolate

			a = (3 * (x0 - x1) - xm1 + x2) / 2;
			b = 2 * x1 + xm1 - (5 * x0 + x2) / 2;
			c = (x1 - xm1) / 2;

			// write out interpolated sample, increment output counter
			g_temppaintbuffer[outpos++].Right = a / 8 + b / 4 + c / 2 + x0;

			Assert(outpos <= TEMP_COPY_BUFFER_SIZE);
		}

		Assert(cfltmem >= 3);

		// save last 3 samples from paintbuffer

		filtermem[0] = buffer[upCount - 5];
		filtermem[1] = buffer[upCount - 3];
		filtermem[2] = buffer[upCount - 1];

		// copy temppaintbuffer back into paintbuffer

		for (i = 0; i < upCount; i++)
			buffer[i] = g_temppaintbuffer[i];
	}

	// pass forward over passed in buffer and linearly interpolate all odd samples
	// pbuffer: buffer to filter (in place)
	// prevfilter:  filter memory. NOTE: this must match the filtertype ie: filterlinear[] for FILTERTYPE_LINEAR
	//				if NULL then perform no filtering.
	// count: how many samples to upsample. will become count*2 samples in buffer, in place.

	public static void S_Interpolate2xLinear(Span<PortableSamplePair> buffer, Span<PortableSamplePair> filtermem, int cfltmem, int count) {
		int i, upCount = count << 1;

		Assert(upCount <= PAINTBUFFER_SIZE);
		Assert(cfltmem >= 1);

		// use interpolation value from previous mix

		buffer[0].Left = (filtermem[0].Left + buffer[0].Left) >> 1;
		buffer[0].Right = (filtermem[0].Right + buffer[0].Right) >> 1;

		for (i = 2; i < upCount; i += 2) {
			// use linear interpolation for upsampling

			buffer[i].Left = (buffer[i].Left + buffer[i - 1].Left) >> 1;
			buffer[i].Right = (buffer[i].Right + buffer[i - 1].Right) >> 1;
		}

		// save last value to be played out in buffer

		filtermem[0] = buffer[upCount - 1];
	}

	// Optimized routine.  2.27X faster than the above routine
	public static void S_Interpolate2xLinear_2(int count, Span<PortableSamplePair> buffer, Span<PortableSamplePair> filtermem, int cfltmem) {
		Assert(cfltmem >= 1);

		int sample = count - 1;
		int end = (count * 2) - 1;
		int write = end;
		int read = sample;
		PortableSamplePair last = buffer[read];
		read--;

		// PERFORMANCE: Unroll the loop 8 times.  This improves speed quite a bit
		for (; sample >= 8; sample -= 8) {
			buffer[write] = last;
			buffer[write - 1].Left = (buffer[read].Left + last.Left) >> 1;
			buffer[write - 1].Right = (buffer[read].Right + last.Right) >> 1;
			last = buffer[read];

			buffer[write - 2] = last;
			buffer[write - 3].Left = (buffer[read - 1].Left + last.Left) >> 1;
			buffer[write - 3].Right = (buffer[read - 1].Right + last.Right) >> 1;
			last = buffer[read - 1];

			buffer[write - 4] = last;
			buffer[write - 5].Left = (buffer[read - 2].Left + last.Left) >> 1;
			buffer[write - 5].Right = (buffer[read - 2].Right + last.Right) >> 1;
			last = buffer[read - 2];

			buffer[write - 6] = last;
			buffer[write - 7].Left = (buffer[read - 3].Left + last.Left) >> 1;
			buffer[write - 7].Right = (buffer[read - 3].Right + last.Right) >> 1;
			last = buffer[read - 3];

			buffer[write - 8] = last;
			buffer[write - 9].Left = (buffer[read - 4].Left + last.Left) >> 1;
			buffer[write - 9].Right = (buffer[read - 4].Right + last.Right) >> 1;
			last = buffer[read - 4];

			buffer[write - 10] = last;
			buffer[write - 11].Left = (buffer[read - 5].Left + last.Left) >> 1;
			buffer[write - 11].Right = (buffer[read - 5].Right + last.Right) >> 1;
			last = buffer[read - 5];

			buffer[write - 12] = last;
			buffer[write - 13].Left = (buffer[read - 6].Left + last.Left) >> 1;
			buffer[write - 13].Right = (buffer[read - 6].Right + last.Right) >> 1;
			last = buffer[read - 6];

			buffer[write - 14] = last;
			buffer[write - 15].Left = (buffer[read - 7].Left + last.Left) >> 1;
			buffer[write - 15].Right = (buffer[read - 7].Right + last.Right) >> 1;
			last = buffer[read - 7];

			read -= 8;
			write -= 16;
		}
		while (read >= 0) {
			buffer[write] = last;
			buffer[write - 1].Left = (buffer[read].Left + last.Left) >> 1;
			buffer[write - 1].Right = (buffer[read].Right + last.Right) >> 1;
			last = buffer[read];
			read--;
			write -= 2;
		}
		buffer[1] = last;
		buffer[0].Left = (filtermem[0].Left + last.Left) >> 1;
		buffer[0].Right = (filtermem[0].Right + last.Right) >> 1;
		filtermem[0] = buffer[end];
	}

	// upsample by 2x, optionally using interpolation
	// count: how many samples to upsample. will become count*2 samples in buffer, in place.
	// pbuffer: buffer to upsample into (in place)
	// pfiltermem:  filter memory. NOTE: this must match the filtertype ie: filterlinear[] for FILTERTYPE_LINEAR
	//				if NULL then perform no filtering.
	// cfltmem: max number of sample pairs filter can use
	// filtertype: FILTERTYPE_NONE, _LINEAR, _CUBIC etc.  Must match prevfilter.
	public static void S_MixBufferUpsample2x(int count, Span<PortableSamplePair> buffer, Span<PortableSamplePair> filtermem, int cfltmem, int filtertype) {
		// JAY: Optimized this routine.  Test then remove old routine.
		// NOTE: Has been proven equivalent by comparing output.
		if (filtertype == FILTERTYPE_LINEAR) {
			S_Interpolate2xLinear_2(count, buffer, filtermem, cfltmem);
			return;
		}
		int i, j, upCount = count << 1;

		// reverse through buffer, duplicating contents for 'count' samples

		for (i = upCount - 1, j = count - 1; j >= 0; i -= 2, j--) {
			buffer[i] = buffer[j];
			buffer[i - 1] = buffer[j];
		}

		// pass forward through buffer, interpolate all even slots

		switch (filtertype) {
			default:
				break;
			case FILTERTYPE_LINEAR:
				S_Interpolate2xLinear(buffer, filtermem, cfltmem, count);
				break;
			case FILTERTYPE_CUBIC:
				S_Interpolate2xCubic(buffer, filtermem, cfltmem, count);
				break;
		}
	}

	//===============================================================================
	// PAINTBUFFER ROUTINES
	//===============================================================================


	// Set current paintbuffer to pbuf.
	// The set paintbuffer is used by all subsequent mixing, upsampling and dsp routines.
	// Also sets the rear paintbuffer if paintbuffer has fsurround true.
	// (otherwise, rearpaintbuffer is NULL)

	public static void MIX_SetCurrentPaintbuffer(int ipaintbuffer) {
		// set front and rear paintbuffer

		Assert(ipaintbuffer < g_paintBuffers.Count);

		g_curpaintbuffer = g_paintBuffers[ipaintbuffer].Buf;

		if (g_paintBuffers[ipaintbuffer].Surround) {
			g_currearpaintbuffer = g_paintBuffers[ipaintbuffer].BufRear;

			g_curcenterpaintbuffer = null;

			if (g_paintBuffers[ipaintbuffer].SurroundCenter)
				g_curcenterpaintbuffer = g_paintBuffers[ipaintbuffer].BufCenter;
		}
		else {
			g_currearpaintbuffer = null;
			g_curcenterpaintbuffer = null;
		}

		Assert(g_curpaintbuffer != null);
	}

	// return index to current paintbuffer

	public static int MIX_GetCurrentPaintbufferIndex() {
		int i;

		for (i = 0; i < g_paintBuffers.Count; i++) {
			if (g_curpaintbuffer == g_paintBuffers[i].Buf)
				return i;
		}

		return 0;
	}

	// return pointer to current paintbuffer struct

	public static PaintBuffer MIX_GetCurrentPaintbufferPtr() {
		int ipaint = MIX_GetCurrentPaintbufferIndex();

		Assert(ipaint < g_paintBuffers.Count);

		return g_paintBuffers[ipaint];
	}

	// return pointer to front paintbuffer pbuf, given index

	public static PortableSamplePair[] MIX_GetPFrontFromIPaint(int ipaintbuffer) {
		return g_paintBuffers[ipaintbuffer].Buf;
	}

	public static PaintBuffer MIX_GetPPaintFromIPaint(int ipaintbuffer) {
		Assert(ipaintbuffer < g_paintBuffers.Count);

		return g_paintBuffers[ipaintbuffer];
	}

	// return pointer to rear buffer, given index.
	// returns null if fsurround is false;

	public static PortableSamplePair[]? MIX_GetPRearFromIPaint(int ipaintbuffer) {
		if (g_paintBuffers[ipaintbuffer].Surround)
			return g_paintBuffers[ipaintbuffer].BufRear;

		return null;
	}

	// return pointer to center buffer, given index.
	// returns null if fsurround_center is false;

	public static PortableSamplePair[]? MIX_GetPCenterFromIPaint(int ipaintbuffer) {
		if (g_paintBuffers[ipaintbuffer].SurroundCenter)
			return g_paintBuffers[ipaintbuffer].BufCenter;

		return null;
	}

	// return index to paintbuffer, given buffer pointer

	public static int MIX_GetIPaintFromPFront(PortableSamplePair[] buf) {
		int i;

		for (i = 0; i < g_paintBuffers.Count; i++) {
			if (buf == g_paintBuffers[i].Buf)
				return i;
		}

		return 0;
	}

	// return pointer to paintbuffer struct, given ptr to buffer data

	public static PaintBuffer MIX_GetPPaintFromPFront(PortableSamplePair[] buf) {
		int i;
		i = MIX_GetIPaintFromPFront(buf);

		return g_paintBuffers[i];
	}

	// up convert mono buffer to full surround

	static void MIX_ConvertBufferToSurround(int ipaintbuffer) {
		PaintBuffer ppaint = g_paintBuffers[ipaintbuffer];

		// duplicate channel data as needed

		if (g_AudioDevice!.IsSurround()) {
			// set buffer flags

			ppaint.Surround = g_AudioDevice.IsSurround();
			ppaint.SurroundCenter = g_AudioDevice.IsSurroundCenter();

			PortableSamplePair[] front = MIX_GetPFrontFromIPaint(ipaintbuffer);
			PortableSamplePair[]? rear = MIX_GetPRearFromIPaint(ipaintbuffer);
			PortableSamplePair[]? center = MIX_GetPCenterFromIPaint(ipaintbuffer);

			// copy front to rear
			front.AsSpan(0, PAINTBUFFER_SIZE).CopyTo(rear);

			// copy front to center
			if (g_AudioDevice.IsSurroundCenter())
				front.AsSpan(0, PAINTBUFFER_SIZE).CopyTo(center);
		}
	}

	// Activate a paintbuffer.  All active paintbuffers are mixed in parallel within
	// MIX_MixChannelsToPaintbuffer, according to flags

	static void MIX_ActivatePaintbuffer(int ipaintbuffer) {
		Assert(ipaintbuffer < g_paintBuffers.Count);
		g_paintBuffers[ipaintbuffer].Active = true;
	}

	// Don't mix into this paintbuffer

	static void MIX_DeactivatePaintbuffer(int ipaintbuffer) {
		Assert(ipaintbuffer < g_paintBuffers.Count);
		g_paintBuffers[ipaintbuffer].Active = false;
	}

	// Don't mix into any paintbuffers

	static void MIX_DeactivateAllPaintbuffers() {
		int i;
		for (i = 0; i < g_paintBuffers.Count; i++)
			g_paintBuffers[i].Active = false;
	}

	// set upsampling filter indexes back to 0

	static void MIX_ResetPaintbufferFilterCounters() {
		int i;
		for (i = 0; i < g_paintBuffers.Count; i++)
			g_paintBuffers[i].IFilter = 0;
	}

	static void MIX_ResetPaintbufferFilterCounter(int ipaintbuffer) {
		Assert(ipaintbuffer < g_paintBuffers.Count);
		g_paintBuffers[ipaintbuffer].IFilter = 0;
	}

	// Change paintbuffer's flags

	static void MIX_SetPaintbufferFlags(int ipaintbuffer, int flags) {
		Assert(ipaintbuffer < g_paintBuffers.Count);
		g_paintBuffers[ipaintbuffer].Flags = flags;
	}

	// zero out all paintbuffers

	public static void MIX_ClearAllPaintBuffers(int sampleCount, bool clearFilters) {
		// g_paintBuffers can be NULL with -nosound
		if (g_paintBuffers.Count <= 0)
			return;

		int i;
		int count = Math.Min(sampleCount, PAINTBUFFER_SIZE);

		// zero out all paintbuffer data (ignore sampleCount)

		for (i = 0; i < g_paintBuffers.Count; i++) {
			g_paintBuffers[i].Buf?.AsSpan(0, count + 1).Clear();

			g_paintBuffers[i].BufRear?.AsSpan(0, count + 1).Clear();

			g_paintBuffers[i].BufCenter?.AsSpan(0, count + 1).Clear();

			if (clearFilters) {
				g_paintBuffers[i].FltMem.AsSpan().Clear();
				g_paintBuffers[i].FltMemRear.AsSpan().Clear();
				g_paintBuffers[i].FltMemCenter.AsSpan().Clear();
			}
		}

		if (clearFilters)
			MIX_ResetPaintbufferFilterCounters();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] static int AVG(int a, int b) => (a + b) >> 1;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] static int AVG4(int a, int b, int c, int d) => (a + b + c + d) >> 2;

	// Synthesize center channel from left/right values (average).
	// Currently just averages, but could actually remove
	// the center signal from the l/r channels...

	static void MIX_CenterFromLeftRight(ref int pl, ref int pr, out int pc) {
		int l = pl;
		int r = pr;
		int c = 0;


		c = (l + r) / 2;

		/*
			l = l - c/2;
			r = r - c/2;

			if (l < 0)
			{
				l = 0;
				r += (-l);
				c += (-l);
			}
			else if (r < 0)
			{
				r = 0;
				l += (-r);
				c += (-r);
			}
		*/
		pc = c;
		//	*pl = l;
		//	*pr = r;
	}

	// mixes pbuf1 + pbuf2 into pbuf3, count samples
	// fgain is output gain 0-1.0
	// NOTE: pbuf3 may equal pbuf1 or pbuf2!

	// mixing algorithms:

	// destination 2ch:
	// pb1 2ch		  + pb2 2ch			-> pb3 2ch
	// pb1 (4ch->2ch) + pb2 2ch			-> pb3 2ch
	// pb1 2ch		  + pb2 (4ch->2ch)	-> pb3 2ch
	// pb1 (4ch->2ch) + pb2 (4ch->2ch)	-> pb3 2ch

	// destination 4ch:
	// pb1 4ch		  + pb2 4ch			-> pb3 4ch
	// pb1 (2ch->4ch) + pb2 4ch			-> pb3 4ch
	// pb1 4ch		  + pb2 (2ch->4ch)	-> pb3 4ch
	// pb1 (2ch->4ch) + pb2 (2ch->4ch)	-> pb3 4ch

	// if all buffers are 4 or 5 ch surround, mix rear & center channels into ibuf3 as well.

	// NOTE: for performance, conversion and mixing are done in a single pass instead of
	// a two pass channel convert + mix scheme.

	public static void MIX_MixPaintbuffers(int ibuf1, int ibuf2, int ibuf3, int count, float fgain_out) {
		int i;
		PortableSamplePair[] pbuf1, pbuf2, pbuf3;
		PortableSamplePair[] pbufrear1, pbufrear2, pbufrear3;
		PortableSamplePair[] pbufcenter1, pbufcenter2, pbufcenter3;
		int cchan1, cchan2, cchan3;
		int xl, xr;
		int l, r, l2, r2, c, c2;
		int gain_out;

		gain_out = (int)(256 * fgain_out);

		Assert(count <= PAINTBUFFER_SIZE);
		Assert(ibuf1 < g_paintBuffers.Count);
		Assert(ibuf2 < g_paintBuffers.Count);
		Assert(ibuf3 < g_paintBuffers.Count);

		pbuf1 = g_paintBuffers[ibuf1].Buf;
		pbuf2 = g_paintBuffers[ibuf2].Buf;
		pbuf3 = g_paintBuffers[ibuf3].Buf;

		pbufrear1 = g_paintBuffers[ibuf1].BufRear!;
		pbufrear2 = g_paintBuffers[ibuf2].BufRear!;
		pbufrear3 = g_paintBuffers[ibuf3].BufRear!;

		pbufcenter1 = g_paintBuffers[ibuf1].BufCenter!;
		pbufcenter2 = g_paintBuffers[ibuf2].BufCenter!;
		pbufcenter3 = g_paintBuffers[ibuf3].BufCenter!;

		cchan1 = 2 + (g_paintBuffers[ibuf1].Surround ? 2 : 0) + (g_paintBuffers[ibuf1].SurroundCenter ? 1 : 0);
		cchan2 = 2 + (g_paintBuffers[ibuf2].Surround ? 2 : 0) + (g_paintBuffers[ibuf2].SurroundCenter ? 1 : 0);
		cchan3 = 2 + (g_paintBuffers[ibuf3].Surround ? 2 : 0) + (g_paintBuffers[ibuf3].SurroundCenter ? 1 : 0);

		// make sure pbuf1 always has fewer or equal channels than pbuf2
		// NOTE: pbuf3 may equal pbuf1 or pbuf2!

		if (cchan2 < cchan1) {
			(cchan1, cchan2) = (cchan2, cchan1);
			PortableSamplePair[] pbuftemp = pbuf1;
			pbuf1 = pbuf2;
			pbuf2 = pbuftemp;
			pbuftemp = pbufrear1;
			pbufrear1 = pbufrear2;
			pbufrear2 = pbuftemp;
			pbuftemp = pbufcenter1;
			pbufcenter1 = pbufcenter2;
			pbufcenter2 = pbuftemp;
		}


		// UNDONE: implement fast mixing routines for each of the following sections

		// destination buffer stereo - average n chans down to stereo

		if (cchan3 == 2) {
			// destination 2ch:
			// pb1 2ch		  + pb2 2ch			-> pb3 2ch
			// pb1 2ch		  + pb2 (4ch->2ch)	-> pb3 2ch
			// pb1 (4ch->2ch) + pb2 (4ch->2ch)	-> pb3 2ch

			if (cchan1 == 2 && cchan2 == 2) {
				// mix front channels

				for (i = 0; i < count; i++) {
					pbuf3[i].Left = pbuf1[i].Left + pbuf2[i].Left;
					pbuf3[i].Right = pbuf1[i].Right + pbuf2[i].Right;
				}
				goto gain2ch;
			}

			if (cchan1 == 2 && cchan2 == 4) {
				// avg rear chan l/r

				for (i = 0; i < count; i++) {
					pbuf3[i].Left = pbuf1[i].Left + AVG(pbuf2[i].Left, pbufrear2[i].Left);
					pbuf3[i].Right = pbuf1[i].Right + AVG(pbuf2[i].Right, pbufrear2[i].Right);
				}
				goto gain2ch;
			}

			if (cchan1 == 4 && cchan2 == 4) {
				// avg rear chan l/r

				for (i = 0; i < count; i++) {
					pbuf3[i].Left = AVG(pbuf1[i].Left, pbufrear1[i].Left) + AVG(pbuf2[i].Left, pbufrear2[i].Left);
					pbuf3[i].Right = AVG(pbuf1[i].Right, pbufrear1[i].Right) + AVG(pbuf2[i].Right, pbufrear2[i].Right);
				}
				goto gain2ch;
			}

			if (cchan1 == 2 && cchan2 == 5) {
				// avg rear chan l/r + center split into left/right

				for (i = 0; i < count; i++) {
					l = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					pbuf3[i].Left = pbuf1[i].Left + AVG(l, pbufrear2[i].Left);
					pbuf3[i].Right = pbuf1[i].Right + AVG(r, pbufrear2[i].Right);
				}
				goto gain2ch;
			}

			if (cchan1 == 4 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					l = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					pbuf3[i].Left = AVG(pbuf1[i].Left, pbufrear1[i].Left) + AVG(l, pbufrear2[i].Left);
					pbuf3[i].Right = AVG(pbuf1[i].Right, pbufrear1[i].Right) + AVG(r, pbufrear2[i].Right);
				}
				goto gain2ch;
			}

			if (cchan1 == 5 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left + ((pbufcenter1[i].Left) >> 1);
					r = pbuf1[i].Right + ((pbufcenter1[i].Left) >> 1);

					l2 = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r2 = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					pbuf3[i].Left = AVG(l, pbufrear1[i].Left) + AVG(l2, pbufrear2[i].Left);
					pbuf3[i].Right = AVG(r, pbufrear1[i].Right) + AVG(r2, pbufrear2[i].Right);
				}
				goto gain2ch;
			}

		}

		// destination buffer quad - duplicate n chans up to quad

		if (cchan3 == 4) {

			// pb1 4ch		  + pb2 4ch			-> pb3 4ch
			// pb1 (2ch->4ch) + pb2 4ch			-> pb3 4ch
			// pb1 (2ch->4ch) + pb2 (2ch->4ch)	-> pb3 4ch

			if (cchan1 == 4 && cchan2 == 4) {
				// mix front -> front, rear -> rear

				for (i = 0; i < count; i++) {
					pbuf3[i].Left = pbuf1[i].Left + pbuf2[i].Left;
					pbuf3[i].Right = pbuf1[i].Right + pbuf2[i].Right;

					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;
				}
				goto gain4ch;
			}

			if (cchan1 == 2 && cchan2 == 4) {

				for (i = 0; i < count; i++) {
					// split 2 ch left ->  front left, rear left
					// split 2 ch right -> front right, rear right

					xl = pbuf1[i].Left;
					xr = pbuf1[i].Right;

					pbuf3[i].Left = xl + pbuf2[i].Left;
					pbuf3[i].Right = xr + pbuf2[i].Right;

					pbufrear3[i].Left = xl + pbufrear2[i].Left;
					pbufrear3[i].Right = xr + pbufrear2[i].Right;
				}
				goto gain4ch;
			}

			if (cchan1 == 2 && cchan2 == 2) {
				// mix l,r, split into front l, front r

				for (i = 0; i < count; i++) {
					xl = pbuf1[i].Left + pbuf2[i].Left;
					xr = pbuf1[i].Right + pbuf2[i].Right;

					pbufrear3[i].Left = pbuf3[i].Left = xl;
					pbufrear3[i].Right = pbuf3[i].Right = xr;
				}
				goto gain4ch;
			}


			if (cchan1 == 2 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					// split center of chan2 into left/right

					l2 = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r2 = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					xl = pbuf1[i].Left;
					xr = pbuf1[i].Right;

					pbuf3[i].Left = xl + l2;
					pbuf3[i].Right = xr + r2;

					pbufrear3[i].Left = xl + pbufrear2[i].Left;
					pbufrear3[i].Right = xr + pbufrear2[i].Right;
				}
				goto gain4ch;
			}

			if (cchan1 == 4 && cchan2 == 5) {

				for (i = 0; i < count; i++) {
					l2 = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r2 = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					pbuf3[i].Left = pbuf1[i].Left + l2;
					pbuf3[i].Right = pbuf1[i].Right + r2;

					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;
				}
				goto gain4ch;
			}

			if (cchan1 == 5 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left + ((pbufcenter1[i].Left) >> 1);
					r = pbuf1[i].Right + ((pbufcenter1[i].Left) >> 1);

					l2 = pbuf2[i].Left + ((pbufcenter2[i].Left) >> 1);
					r2 = pbuf2[i].Right + ((pbufcenter2[i].Left) >> 1);

					pbuf3[i].Left = l + l2;
					pbuf3[i].Right = r + r2;

					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;
				}
				goto gain4ch;
			}
		}

		// 5 channel destination

		if (cchan3 == 5) {
			// up convert from 2 or 4 ch buffer to 5 ch buffer:
			// center channel is synthesized from front left, front right

			if (cchan1 == 2 && cchan2 == 2) {
				for (i = 0; i < count; i++) {
					// split 2 ch left ->  front left, center, rear left
					// split 2 ch right -> front right, center, rear right

					l = pbuf1[i].Left;
					r = pbuf1[i].Right;

					MIX_CenterFromLeftRight(ref l, ref r, out c);

					l2 = pbuf2[i].Left;
					r2 = pbuf2[i].Right;

					MIX_CenterFromLeftRight(ref l2, ref r2, out c2);

					pbuf3[i].Left = l + l2;
					pbuf3[i].Right = r + r2;

					pbufrear3[i].Left = pbuf1[i].Left + pbuf2[i].Left;
					pbufrear3[i].Right = pbuf1[i].Right + pbuf2[i].Right;

					pbufcenter3[i].Left = c + c2;
				}
				goto gain5ch;
			}

			if (cchan1 == 2 && cchan2 == 4) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left;
					r = pbuf1[i].Right;

					MIX_CenterFromLeftRight(ref l, ref r, out c);

					l2 = pbuf2[i].Left;
					r2 = pbuf2[i].Right;

					MIX_CenterFromLeftRight(ref l2, ref r2, out c2);

					pbuf3[i].Left = l + l2;
					pbuf3[i].Right = r + r2;

					pbufrear3[i].Left = pbuf1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbuf1[i].Right + pbufrear2[i].Right;

					pbufcenter3[i].Left = c + c2;
				}
				goto gain5ch;
			}

			if (cchan1 == 2 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left;
					r = pbuf1[i].Right;

					MIX_CenterFromLeftRight(ref l, ref r, out c);

					pbuf3[i].Left = l + pbuf2[i].Left;
					pbuf3[i].Right = r + pbuf2[i].Right;

					pbufrear3[i].Left = pbuf1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbuf1[i].Right + pbufrear2[i].Right;

					pbufcenter3[i].Left = c + pbufcenter2[i].Left;
				}
				goto gain5ch;
			}

			if (cchan1 == 4 && cchan2 == 4) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left;
					r = pbuf1[i].Right;

					MIX_CenterFromLeftRight(ref l, ref r, out c);

					l2 = pbuf2[i].Left;
					r2 = pbuf2[i].Right;

					MIX_CenterFromLeftRight(ref l2, ref r2, out c2);

					pbuf3[i].Left = l + l2;
					pbuf3[i].Right = r + r2;

					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;

					pbufcenter3[i].Left = c + c2;
				}
				goto gain5ch;
			}


			if (cchan1 == 4 && cchan2 == 5) {
				for (i = 0; i < count; i++) {
					l = pbuf1[i].Left;
					r = pbuf1[i].Right;

					MIX_CenterFromLeftRight(ref l, ref r, out c);

					pbuf3[i].Left = l + pbuf2[i].Left;
					pbuf3[i].Right = r + pbuf2[i].Right;

					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;

					pbufcenter3[i].Left = c + pbufcenter2[i].Left;
				}
				goto gain5ch;
			}

			if (cchan2 == 5 && cchan1 == 5) {
				for (i = 0; i < count; i++) {
					pbuf3[i].Left = pbuf1[i].Left + pbuf2[i].Left;
					pbuf3[i].Right = pbuf1[i].Right + pbuf2[i].Right;
					pbufrear3[i].Left = pbufrear1[i].Left + pbufrear2[i].Left;
					pbufrear3[i].Right = pbufrear1[i].Right + pbufrear2[i].Right;
					pbufcenter3[i].Left = pbufcenter1[i].Left + pbufcenter2[i].Left;
				}
				goto gain5ch;
			}
		}

	gain2ch:
		if (gain_out == 256)        // KDB: perf
			return;

		for (i = 0; i < count; i++) {
			pbuf3[i].Left = (pbuf3[i].Left * gain_out) >> 8;
			pbuf3[i].Right = (pbuf3[i].Right * gain_out) >> 8;
		}
		return;

	gain4ch:
		if (gain_out == 256)        // KDB: perf
			return;

		for (i = 0; i < count; i++) {
			pbuf3[i].Left = (pbuf3[i].Left * gain_out) >> 8;
			pbuf3[i].Right = (pbuf3[i].Right * gain_out) >> 8;
			pbufrear3[i].Left = (pbufrear3[i].Left * gain_out) >> 8;
			pbufrear3[i].Right = (pbufrear3[i].Right * gain_out) >> 8;
		}
		return;

	gain5ch:
		if (gain_out == 256)        // KDB: perf
			return;

		for (i = 0; i < count; i++) {
			pbuf3[i].Left = (pbuf3[i].Left * gain_out) >> 8;
			pbuf3[i].Right = (pbuf3[i].Right * gain_out) >> 8;
			pbufrear3[i].Left = (pbufrear3[i].Left * gain_out) >> 8;
			pbufrear3[i].Right = (pbufrear3[i].Right * gain_out) >> 8;
			pbufcenter3[i].Left = (pbufcenter3[i].Left * gain_out) >> 8;
		}
		return;
	}

	// multiply all values in paintbuffer by fgain

	public static void MIX_ScalePaintBuffer(int bufferIndex, int count, float fgain) {
		PortableSamplePair[] pbuf = g_paintBuffers[bufferIndex].Buf;
		PortableSamplePair[] pbufrear = g_paintBuffers[bufferIndex].BufRear!;
		PortableSamplePair[] pbufcenter = g_paintBuffers[bufferIndex].BufCenter!;

		int gain = (int)(256 * fgain);
		int i;

		if (gain == 256)
			return;

		if (!g_paintBuffers[bufferIndex].Surround) {
			for (i = 0; i < count; i++) {
				pbuf[i].Left = (pbuf[i].Left * gain) >> 8;
				pbuf[i].Right = (pbuf[i].Right * gain) >> 8;
			}
		}
		else {
			for (i = 0; i < count; i++) {
				pbuf[i].Left = (pbuf[i].Left * gain) >> 8;
				pbuf[i].Right = (pbuf[i].Right * gain) >> 8;
				pbufrear[i].Left = (pbufrear[i].Left * gain) >> 8;
				pbufrear[i].Right = (pbufrear[i].Right * gain) >> 8;
			}

			if (g_paintBuffers[bufferIndex].SurroundCenter) {
				for (i = 0; i < count; i++) {
					pbufcenter[i].Left = (pbufcenter[i].Left * gain) >> 8;
					// pbufcenter[i].right = (pbufcenter[i].right * gain) >> 8; mono center channel
				}
			}
		}
	}

	// DEBUG peak detection values
	static float sdebug_avg_in = 0.0f;
	static float sdebug_in_count = 0.0f;
	static float sdebug_avg_out = 0.0f;
	static float sdebug_out_count = 0.0f;
	const float SDEBUG_TOTAL_COUNT = 3 * 44100;

	// DEBUG code - get and show peak value of specified paintbuffer
	// DEBUG code - ibuf is buffer index, count is # samples to test, pppeakprev stores peak


	static void SDEBUG_GetAvgValue(int ibuf, int count, ref float pav) {
		if (snd_showstart.GetInt() != 4)
			return;

		float av = 0.0F;

		for (int i = 0; i < count; i++)
			av += (Math.Abs(g_paintBuffers[ibuf].Buf[0].Left) + Math.Abs(g_paintBuffers[ibuf].Buf[0].Right)) / 2.0F;

		pav = av / count;
	}


	static void SDEBUG_GetAvgIn(int ibuf, int count) {
		float av = 0.0f;
		SDEBUG_GetAvgValue(ibuf, count, ref av);

		sdebug_avg_in = ((av * count) + (sdebug_avg_in * sdebug_in_count)) / (count + sdebug_in_count);
		sdebug_in_count += count;
	}

	static void SDEBUG_GetAvgOut(int ibuf, int count) {
		float av = 0.0f;
		SDEBUG_GetAvgValue(ibuf, count, ref av);

		sdebug_avg_out = ((av * count) + (sdebug_avg_out * sdebug_out_count)) / (count + sdebug_out_count);
		sdebug_out_count += count;
	}


	static void SDEBUG_ShowAvgValue() {
		if (sdebug_in_count > SDEBUG_TOTAL_COUNT) {
			if ((int)sdebug_avg_in > 20.0 && (int)sdebug_avg_out > 20.0)
				DevMsg($"dsp avg gain:{sdebug_avg_out / sdebug_avg_in:F2} in:{sdebug_avg_in:F2} out:{sdebug_avg_out:F2} 1/gain:{sdebug_avg_in / sdebug_avg_out:F2}\n");

			sdebug_avg_in = 0.0f;
			sdebug_avg_out = 0.0f;
			sdebug_in_count = 0.0f;
			sdebug_out_count = 0.0f;
		}
	}

	// clip all values in paintbuffer to 16bit.
	// if fsurround is set for paintbuffer, also process rear buffer samples

	public static void MIX_CompressPaintbuffer(int ipaint, int count) {
		int i;
		PaintBuffer ppaint = MIX_GetPPaintFromIPaint(ipaint);
		Span<PortableSamplePair> pbf;
		Span<PortableSamplePair> pbr;
		Span<PortableSamplePair> pbc;

		pbf = ppaint.Buf.AsSpan(0, count);
		pbr = ppaint.BufRear;
		pbc = ppaint.BufCenter;

		for (i = 0; i < pbf.Length; i++) {
			pbf[i].Left = CLIP(pbf[i].Left);
			pbf[i].Right = CLIP(pbf[i].Right);
		}

		if (ppaint.Surround) {
			Assert(!pbr.IsEmpty);

			pbr = pbr[..count];
			for (i = 0; i < pbr.Length; i++) {
				pbr[i].Left = CLIP(pbr[i].Left);
				pbr[i].Right = CLIP(pbr[i].Right);
			}
		}

		if (ppaint.SurroundCenter) {
			Assert(!pbc.IsEmpty);

			pbc = pbc[..count];
			for (i = 0; i < pbc.Length; i++) {
				pbc[i].Left = CLIP(pbc[i].Left);
				//pbc->right = CLIP(pbc->right); mono center channel
			}
		}
	}


	// mix and upsample channels to 44khz 'ipaintbuffer'
	// mix channels matching 'flags' (SOUND_MIX_DRY, SOUND_MIX_WET, SOUND_MIX_SPEAKER) into specified paintbuffer
	// upsamples 11khz, 22khz channels to 44khz.

	// NOTE: only call this on channels that will be mixed into only 1 paintbuffer
	// and that will not be mixed until the next mix pass! otherwise, MIX_MixChannelsToPaintbuffer
	// will advance any internal pointers on mixed channels; subsequent calls will be at
	// incorrect offset.

	public static void MIX_MixUpsampleBuffer(ChannelList list, int ipaintbuffer, int end, int count, int flags) {
		int ipaintcur = MIX_GetCurrentPaintbufferIndex(); // save current paintbuffer

		// reset paintbuffer upsampling filter index
		MIX_ResetPaintbufferFilterCounter(ipaintbuffer);

		// prevent other paintbuffers from being mixed
		MIX_DeactivateAllPaintbuffers();

		MIX_ActivatePaintbuffer(ipaintbuffer);          // operates on MIX_MixChannelsToPaintbuffer
		MIX_SetCurrentPaintbuffer(ipaintbuffer);            // operates on MixUpSample

		// mix 11khz channels to buffer
		if (list.Has11kChannels) {
			MIX_MixChannelsToPaintbuffer(list, end, flags, SOUND_11k, SOUND_11k);

			// upsample 11khz buffer by 2x
			g_AudioDevice!.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_11k), FILTERTYPE_LINEAR);
		}

		if (list.Has22kChannels || list.Has11kChannels) {
			// mix 22khz channels to buffer
			MIX_MixChannelsToPaintbuffer(list, end, flags, SOUND_22k, SOUND_22k);

			// upsample 22khz buffer by 2x
			g_AudioDevice!.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_22k), FILTERTYPE_LINEAR);
		}

		// mix 44khz channels to buffer
		MIX_MixChannelsToPaintbuffer(list, end, flags, SOUND_44k, SOUND_DMA_SPEED);

		MIX_DeactivateAllPaintbuffers();

		// restore previous paintbuffer
		MIX_SetCurrentPaintbuffer(ipaintcur);
	}

	// upsample and mix sounds into final 44khz versions of the following paintbuffers:
	// SOUND_BUFFER_ROOM, SOUND_BUFFER_FACING, IFACINGAWAY, SOUND_BUFFER_DRY, SOUND_BUFFER_SPEAKER, SOUND_BUFFER_SPECIALs
	// dsp fx are then applied to these buffers by the caller.
	// caller also remixes all into final SOUND_BUFFER_PAINT output.

	public static void MIX_UpsampleAllPaintbuffers(ChannelList list, int end, int count) {
		// 'dry' and 'speaker' channel sounds mix 100% into their corresponding buffers

		// mix and upsample all 'dry' sounds (channels) to 44khz SOUND_BUFFER_DRY paintbuffer

		if (list.HasDryChannels)
			MIX_MixUpsampleBuffer(list, SOUND_BUFFER_DRY, end, count, SOUND_MIX_DRY);

		// mix and upsample all 'speaker' sounds (channels) to 44khz SOUND_BUFFER_SPEAKER paintbuffer

		if (list.HasSpeakerChannels)
			MIX_MixUpsampleBuffer(list, SOUND_BUFFER_SPEAKER, end, count, SOUND_MIX_SPEAKER);

		// mix and upsample all 'special dsp' sounds (channels) to 44khz SOUND_BUFFER_SPECIALs paintbuffer

		for (int iDSP = 0; iDSP < list.SpecialDSPs.Count; ++iDSP) {
			for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
				PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);
				if (specialBuffer.SpecialDSP == list.SpecialDSPs[iDSP] && specialBuffer.IdspSpecialDsp != -1) {
					MIX_MixUpsampleBuffer(list, i, end, count, SOUND_MIX_SPECIAL_DSP);
					break;
				}
			}
		}

		// 'room', 'facing' 'facingaway' sounds are mixed into up to 3 buffers:

		// 11khz sounds are mixed into 3 buffers based on distance from listener, and facing direction
		// These buffers are room, facing, facingaway
		// These 3 mixed buffers are then each upsampled to 22khz.

		// 22khz sounds are mixed into the 3 buffers based on distance from listener, and facing direction
		// These 3 mixed buffers are then each upsampled to 44khz.

		// 44khz sounds are mixed into the 3 buffers based on distance from listener, and facing direction
		MIX_DeactivateAllPaintbuffers();

		// set paintbuffer upsample filter indices to 0
		MIX_ResetPaintbufferFilterCounters();

		if (!g_bDspOff) {
			// only mix to roombuffer if dsp fx are on KDB: perf
			MIX_ActivatePaintbuffer(SOUND_BUFFER_ROOM);                 // operates on MIX_MixChannelsToPaintbuffer
		}

		MIX_ActivatePaintbuffer(SOUND_BUFFER_FACING);

		if (g_bdirectionalfx) {
			// mix to facing away buffer only if directional presets are set

			MIX_ActivatePaintbuffer(SOUND_BUFFER_FACINGAWAY);
		}

		// mix 11khz sounds:
		// pan sounds between 3 busses: facing, facingaway and room buffers

		MIX_MixChannelsToPaintbuffer(list, end, SOUND_MIX_WET, SOUND_11k, SOUND_11k);

		// upsample all 11khz buffers by 2x
		if (!g_bDspOff) {
			// only upsample roombuffer if dsp fx are on KDB: perf
			MIX_SetCurrentPaintbuffer(SOUND_BUFFER_ROOM);           // operates on MixUpSample
			g_AudioDevice!.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_11k), FILTERTYPE_LINEAR);
		}

		MIX_SetCurrentPaintbuffer(SOUND_BUFFER_FACING);
		g_AudioDevice!.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_11k), FILTERTYPE_LINEAR);

		if (g_bdirectionalfx) {
			MIX_SetCurrentPaintbuffer(SOUND_BUFFER_FACINGAWAY);
			g_AudioDevice.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_11k), FILTERTYPE_LINEAR);
		}

		// mix 22khz sounds:
		// pan sounds between 3 busses: facing, facingaway and room buffers
		MIX_MixChannelsToPaintbuffer(list, end, SOUND_MIX_WET, SOUND_22k, SOUND_22k);

		// upsample all 22khz buffers by 2x
		if (!g_bDspOff) {
			// only upsample roombuffer if dsp fx are on KDB: perf

			MIX_SetCurrentPaintbuffer(SOUND_BUFFER_ROOM);
			g_AudioDevice.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_22k), FILTERTYPE_LINEAR);
		}

		MIX_SetCurrentPaintbuffer(SOUND_BUFFER_FACING);
		g_AudioDevice.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_22k), FILTERTYPE_LINEAR);

		if (g_bdirectionalfx) {
			MIX_SetCurrentPaintbuffer(SOUND_BUFFER_FACINGAWAY);
			g_AudioDevice.MixUpsample(count / (SOUND_DMA_SPEED / SOUND_22k), FILTERTYPE_LINEAR);
		}

		// mix all 44khz sounds to all active paintbuffers
		MIX_MixChannelsToPaintbuffer(list, end, SOUND_MIX_WET, SOUND_44k, SOUND_DMA_SPEED);

		MIX_DeactivateAllPaintbuffers();

		MIX_SetCurrentPaintbuffer(SOUND_BUFFER_PAINT);
	}

	public static readonly ConVar snd_cull_duplicates = new("snd_cull_duplicates", "0", 0, "If nonzero, aggressively cull duplicate sounds during mixing. The number specifies the number of duplicates allowed to be played.");

	// Helper class for determining whether a given channel number should be culled from
	// mixing, if snd_cull_duplicates is enabled (psychoacoustic quashing).
	class ChannelCullList
	{
		// an array of sound names and their volumes
		// TODO: there may be a way to do this faster on 360 (eg, pad to 128bit, use SIMD)
		public struct ChannelVolData
		{
			public int ChannelNum;
			public int Vol; // max volume of sound. -1 means "do not cull, ever, do not even do the math"
			public object? NameHash; // a unique id for a sound file
		}

		static readonly Comparer<ChannelVolData> VolumeComparer = Comparer<ChannelVolData>.Create((a, b) => b.Vol - a.Vol);

		readonly ChannelVolData[] channelInfo = new ChannelVolData[MAX_CHANNELS];

		readonly bool[] shouldCull = new bool[MAX_CHANNELS]; // in ChannelList order, not sorted order
		int numChans;

		// default constructor
		public ChannelCullList() {
			numChans = 0;
		}

		// returns true if a given channel number has been marked for culling
		public bool ShouldCull(int channelNum) {
			return (numChans > channelNum) ? shouldCull[channelNum] : false;
		}

		// call if you plan on culling channels - and not otherwise, it's a little expensive
		// (that's why it's not in the constructor)
		public void Initialize(ChannelList list) {
			// First, build a sorted list of channels by decreasing volume, and by a hash of their wavname.
			numChans = list.Count;

			for (int i = numChans - 1; i >= 0; --i) {
				Channel ch = list.GetChannel(i);
				channelInfo[i].ChannelNum = i;
				if (ch != null && ch.Mixer!.IsReadyToMix()) {
					channelInfo[i].Vol = (int)ChannelLoudestCurVolume(ch);
					AssertMsg(channelInfo[i].Vol >= 0, "Sound channel has a negative volume?");
					channelInfo[i].NameHash = ch.Sfx;
				}
				else {
					channelInfo[i].Vol = -1;
					channelInfo[i].NameHash = null; // doesn't matter
				}
			}

			// set the unused channels to invalid data
			for (int i = numChans; i < MAX_CHANNELS; ++i) {
				channelInfo[i].ChannelNum = -1;
				channelInfo[i].Vol = -1;
			}

			// Sort the list.
			Array.Sort(channelInfo, VolumeComparer);

			// Then, determine if the given sound is less than the nth loudest of its hash. If so, mark its flag
			// for removal.
			// TODO: use an actual algorithm rather than this bogus quadratic technique.
			// (I'm using it for now because we don't have convenient/fast hash table
			// classes, which would be the linear-time way to deal with this).
			int cutoff = snd_cull_duplicates.GetInt();
			for (int i = 0; i < numChans; ++i) // i is index in original channel list
			{
				Channel ch = list.GetChannel(i);
				// for each sound, determine where it ranks in loudness
				int howManyLouder = 0;
				for (int j = 0;
					 j < MAX_CHANNELS && channelInfo[j].ChannelNum != i && channelInfo[j].Vol >= 0;
					 ++j) {
					// j steps through the sorted list until we find ourselves:
					if (channelInfo[j].NameHash == ch.Sfx) {
						// that's another channel playing this sound but louder than me
						++howManyLouder;
					}
				}
				if (howManyLouder >= cutoff) {
					// this sound should be culled
					shouldCull[i] = true;
				}
				else {
					// this sound should not be culled
					shouldCull[i] = false;
				}
			}
		}
	}

	public static readonly ConVar snd_mute_losefocus = new("snd_mute_losefocus", "1", FCvar.Archive);

	static readonly ChannelCullList s_CullList = new();
	static readonly ChannelList s_MixChannelList = new();
	static bool IsOutputMuted => !soundServices.IsGameActive() && snd_mute_losefocus.GetBool();

	// build a list of channels that will actually do mixing in this update
	// remove all active channels that won't mix for some reason
	public static void MIX_BuildChannelList(ChannelList list) {
		g_ActiveChannels.GetActiveChannels(list);
		list.SpecialDSPs.Clear();
		list.HasDryChannels = false;
		list.HasSpeakerChannels = false;
		list.Has11kChannels = false;
		list.Has22kChannels = false;
		list.Has44kChannels = false;
		bool delayStartServer = false;
		bool delayStartClient = false;
		bool paused = soundServices.IsGamePaused();

		ChannelCullList cullList = s_CullList;
		if (snd_cull_duplicates.GetInt() > 0)
			cullList.Initialize(list);

		for (int i = list.Count; --i >= 0;) {
			Channel ch = list.GetChannel(i);
			bool remove = false;
			// Certain async loaded sounds lazily load into memory in the background, use this to determine
			//  if the sound is ready for mixing
			AudioSourceBase? source = null;
			if (ch.Mixer!.IsReadyToMix()) {
				source = S_LoadSound(ch.Sfx!, ch);

				// Don't mix sound data for sounds with 'zero' volume. If it's a non-looping sound,
				// just remove the sound when its volume goes to zero. If it's a 'dry' channel sound (ie: music)
				// then assume bZeroVolume is fade in - don't restart

				// To be 'zero' volume, all target volume and current volume values must all be less than 5

				bool zeroVolume = BChannelLowVolume(ch, 1);

				if (source == null || (zeroVolume && !source.IsLooped() && !ch.Flags.Dry)) {
					// NOTE: Since we've loaded the sound, check to see if it's a sentence.  Play them at zero anyway
					// to keep the character's lips moving and the captions happening.
					if (source == null || source.GetSentence() == null) {
						S_FreeChannel(ch);
						remove = true;
					}
				}
				else if (zeroVolume)
					remove = true;
				// If the sound wants to stop when the game pauses, do so
				if (paused && SND_ShouldPause(ch))
					remove = true;
				// On lowend, aggressively cull duplicate sounds.
				if (!remove && snd_cull_duplicates.GetInt() > 0) {
					// We can't simply remove them, because then sounds will pile up waiting to finish later.
					// We need to flag them for not mixing.
					list.Quashed[i] = cullList.ShouldCull(i);
				}
				else
					list.Quashed[i] = false;
			}
			else
				remove = true;

			if (remove) {
				list.RemoveChannelFromList(i);
				continue;
			}
			if (ch.Flags.Speaker)
				list.HasSpeakerChannels = true;
			if (ch.SpecialDsp != 0) {
				if (!list.SpecialDSPs.Contains(ch.SpecialDsp))
					list.SpecialDSPs.Add(ch.SpecialDsp);
			}
			if (ch.Flags.Dry)
				list.HasDryChannels = true;
			int rate = source!.SampleRate();
			if (rate == SOUND_11k)
				list.Has11kChannels = true;
			else if (rate == SOUND_22k)
				list.Has22kChannels = true;
			else if (rate == SOUND_44k)
				list.Has44kChannels = true;
			if (ch.Flags.DelayedStart && !SND_IsMouth(ch)) {
				if (ch.Flags.FromServer)
					delayStartServer = true;
				else
					delayStartClient = true;
			}

			// get playback pitch
			ch.Pitch = ch.Mixer.ModifyPitch(ch.BasePitch * 0.01f);
		}

		// This code will resync the delay calculation clock really often
		// any time there are no scheduled waves or the game is paused
		// we go ahead and reset the clock
		// That way the clock is only used for short periods of time
		// and we need no solution for drift
		if (paused || (soundServices.GetHostFrametimeUnbounded() > soundServices.GetHostFrametime())) {
			delayStartClient = false;
			delayStartServer = false;
		}
		if (!delayStartServer)
			S_SyncClockAdjust(ClockSyncIndex.Server);
		if (!delayStartClient)
			S_SyncClockAdjust(ClockSyncIndex.Client);
	}

	// main mixing rountine - mix up to 'endtime' samples.
	// All channels are mixed in a paintbuffer and then sent to
	// hardware.

	// A mix pass is performed, resulting in mixed sounds in SOUND_BUFFER_ROOM, SOUND_BUFFER_FACING, SOUND_BUFFER_FACINGAWAY, SOUND_BUFFER_DRY, SOUND_BUFFER_SPEAKER, SOUND_BUFFER_SPECIALs

	// directional sounds are panned and mixed between SOUND_BUFFER_FACING and SOUND_BUFFER_FACINGAWAY
	// omnidirectional sounds are panned 100% into SOUND_BUFFER_FACING
	// sound sources far from player (ie: near back of room ) are mixed in proportion to this distance
	// into SOUND_BUFFER_ROOM
	// sounds with ch->bSpeaker set are mixed in mono into SOUND_BUFFER_SPEAKER
	// sounds with ch->bSpecialDSP set are mixed in mono into SOUND_BUFFER_SPECIALs

	// dsp_facingaway fx (2 or 4ch filtering) are then applied to the SOUND_BUFFER_FACINGAWAY
	// dsp_speaker fx (1ch) are then applied to the SOUND_BUFFER_SPEAKER
	// dsp_specialdsp fx (1ch) are then applied to the SOUND_BUFFER_SPECIALs
	// dsp_room fx (1ch reverb) are then applied to the SOUND_BUFFER_ROOM

	// All buffers are recombined into the SOUND_BUFFER_PAINT

	// The dsp_water and dsp_player fx are applied in series to the SOUND_BUFFER_PAINT

	// Finally, the SOUND_BUFFER_DRY buffer is mixed into the SOUND_BUFFER_PAINT

	public static void MIX_PaintChannels(int endtime, bool isUnderwater) {
		int end;
		int count;
		bool b_spatial_delays = dsp_enhance_stereo.GetInt() != 0;
		bool room_fsurround_sav;
		bool room_fsurround_center_sav;
		PaintBuffer proom = MIX_GetPPaintFromIPaint(SOUND_BUFFER_ROOM);

		CheckNewDspPresets();

		MXR_SetCurrentSoundMixer(snd_soundmixer.GetString());

		// dsp performance tuning

		g_snd_profile_type = snd_profile.GetInt();

		// dsp_off is true if no dsp processing is to run
		// directional dsp processing is enabled if dsp_facingaway is non-zero

		g_bDspOff = dsp_off.GetInt() != 0;
		ChannelList list = s_MixChannelList;

		MIX_BuildChannelList(list);

		// get master dsp volume
		g_dsp_volume = dsp_volume.GetFloat();

		// attenuate master dsp volume by 2,4 or 5 ch settings
		if (g_AudioDevice!.IsSurround())
			g_dsp_volume *= g_AudioDevice.IsSurroundCenter() ? dsp_vol_5ch.GetFloat() : dsp_vol_4ch.GetFloat();
		else
			g_dsp_volume *= dsp_vol_2ch.GetFloat();

		if (!g_bDspOff)
			g_bdirectionalfx = dsp_facingaway.GetInt() != 0;
		else
			g_bdirectionalfx = false;

		// get dsp preset gain values, update gain crossfaders, used when mixing dsp processed buffers into paintbuffer
		SDEBUG_ShowAvgValue();

		// the cache needs to hold the audio in memory during mixing, so tell it that mixing is starting
		wavedatacache.OnMixBegin();

		while (g_paintedtime < endtime) {
			// mix a full 'paintbuffer' of sound

			// clamp at paintbuffer size
			end = endtime;
			if (endtime - g_paintedtime > PAINTBUFFER_SIZE)
				end = g_paintedtime + PAINTBUFFER_SIZE;

			// number of 44khz samples to mix into paintbuffer, up to paintbuffer size
			count = end - g_paintedtime;

			// clear all mix buffers
			g_AudioDevice.MixBegin(count);

			// upsample all mix buffers.
			// results in 44khz versions of:
			// SOUND_BUFFER_ROOM, SOUND_BUFFER_FACING, SOUND_BUFFER_FACINGAWAY, SOUND_BUFFER_DRY, SOUND_BUFFER_SPEAKER, SOUND_BUFFER_SPECIALs
			MIX_UpsampleAllPaintbuffers(list, end, count);

			// apply appropriate dsp fx to each buffer, remix buffers into single quad output buffer
			// apply 2 or 4ch filtering to IFACINGAWAY buffer
			if (g_bdirectionalfx)
				g_AudioDevice.ApplyDSPEffects(idsp_facingaway, MIX_GetPFrontFromIPaint(SOUND_BUFFER_FACINGAWAY), MIX_GetPRearFromIPaint(SOUND_BUFFER_FACINGAWAY), MIX_GetPCenterFromIPaint(SOUND_BUFFER_FACINGAWAY), count);

			if (!g_bDspOff && list.HasSpeakerChannels) {
				// apply 1ch filtering to SOUND_BUFFER_SPEAKER
				g_AudioDevice.ApplyDSPEffects(idsp_speaker, MIX_GetPFrontFromIPaint(SOUND_BUFFER_SPEAKER), MIX_GetPRearFromIPaint(SOUND_BUFFER_SPEAKER), MIX_GetPCenterFromIPaint(SOUND_BUFFER_SPEAKER), count);

				// mix SOUND_BUFFER_SPEAKER with SOUND_BUFFER_ROOM and SOUND_BUFFER_FACING
				MIX_ScalePaintBuffer(SOUND_BUFFER_SPEAKER, count, 0.7f);

				MIX_MixPaintbuffers(SOUND_BUFFER_SPEAKER, SOUND_BUFFER_FACING, SOUND_BUFFER_FACING, count, 1.0f);   // +70% dry speaker

				MIX_ScalePaintBuffer(SOUND_BUFFER_SPEAKER, count, 0.43f);

				MIX_MixPaintbuffers(SOUND_BUFFER_SPEAKER, SOUND_BUFFER_ROOM, SOUND_BUFFER_ROOM, count, 1.0f);       // +30% wet speaker
			}

			if (!g_bDspOff) {
				// apply 1ch filtering to SOUND_BUFFER_SPECIALs
				for (int iDSP = 0; iDSP < list.SpecialDSPs.Count; ++iDSP) {
					bool foundMixer = false;

					for (int i = SOUND_BUFFER_SPECIAL_START; i < g_paintBuffers.Count; ++i) {
						PaintBuffer specialBuffer = MIX_GetPPaintFromIPaint(i);
						if (specialBuffer.SpecialDSP == list.SpecialDSPs[iDSP] && specialBuffer.IdspSpecialDsp != -1) {
							g_AudioDevice.ApplyDSPEffects(specialBuffer.IdspSpecialDsp, MIX_GetPFrontFromIPaint(i), MIX_GetPRearFromIPaint(i), MIX_GetPCenterFromIPaint(i), count);

							// mix SOUND_BUFFER_SPECIALs with SOUND_BUFFER_ROOM and SOUND_BUFFER_FACING
							MIX_ScalePaintBuffer(i, count, 0.7f);

							MIX_MixPaintbuffers(i, SOUND_BUFFER_FACING, SOUND_BUFFER_FACING, count, 1.0f);  // +70% dry speaker

							MIX_ScalePaintBuffer(i, count, 0.43f);

							MIX_MixPaintbuffers(i, SOUND_BUFFER_ROOM, SOUND_BUFFER_ROOM, count, 1.0f);      // +30% wet speaker

							foundMixer = true;

							break;
						}
					}

					// Couldn't find a mixer with the correct DSP, so make a new one!
					if (!foundMixer) {
						bool surroundCenter = g_AudioDevice.IsSurroundCenter();
						bool surround = g_AudioDevice.IsSurround() || surroundCenter;

						PaintBuffer newBuffer = new();
						g_paintBuffers.Add(newBuffer);
						MIX_InitializePaintbuffer(newBuffer, surround, surroundCenter);

						newBuffer.Flags = SOUND_BUSS_SPECIAL_DSP;

						// special dsp buffer mixes to mono
						newBuffer.Surround = false;
						newBuffer.SurroundCenter = false;

						newBuffer.IdspSpecialDsp = -1;
						newBuffer.SpecialDSP = list.SpecialDSPs[iDSP];

						newBuffer.PrevSpecialDSP = newBuffer.SpecialDSP;
						newBuffer.IdspSpecialDsp = DSP_Alloc(newBuffer.SpecialDSP, 300, 1);
					}
				}
			}

			// apply dsp_room effects to room buffer
			g_AudioDevice.ApplyDSPEffects(Get_idsp_room(), MIX_GetPFrontFromIPaint(SOUND_BUFFER_ROOM), MIX_GetPRearFromIPaint(SOUND_BUFFER_ROOM), MIX_GetPCenterFromIPaint(SOUND_BUFFER_ROOM), count);

			// save room buffer surround status, in case we upconvert it
			room_fsurround_sav = proom.Surround;
			room_fsurround_center_sav = proom.SurroundCenter;

			// apply left/center/right/lrear/rrear spatial delays to room buffer
			if (b_spatial_delays && !g_bDspOff && !DSP_RoomDSPIsOff()) {
				// upgrade mono room buffer to surround status so we can apply spatial delays to all channels
				MIX_ConvertBufferToSurround(SOUND_BUFFER_ROOM);
				g_AudioDevice.ApplyDSPEffects(idsp_spatial, MIX_GetPFrontFromIPaint(SOUND_BUFFER_ROOM), MIX_GetPRearFromIPaint(SOUND_BUFFER_ROOM), MIX_GetPCenterFromIPaint(SOUND_BUFFER_ROOM), count);
			}

			if (g_bdirectionalfx)       // KDB: perf
			{
				// Recombine IFACING and IFACINGAWAY buffers into SOUND_BUFFER_PAINT
				MIX_MixPaintbuffers(SOUND_BUFFER_FACING, SOUND_BUFFER_FACINGAWAY, SOUND_BUFFER_PAINT, count, DSP_NOROOM_MIX);

				// Add in dsp room fx to paintbuffer, mix at 75%
				MIX_MixPaintbuffers(SOUND_BUFFER_ROOM, SOUND_BUFFER_PAINT, SOUND_BUFFER_PAINT, count, DSP_ROOM_MIX);
			}
			else {
				// Mix IFACING buffer with SOUND_BUFFER_ROOM
				// (SOUND_BUFFER_FACINGAWAY contains no data, IFACINGBBUFFER has full dry mix based on distance from listener)
				// if dsp disabled, mix 100% facingbuffer, otherwise, mix 75% facingbuffer + roombuffer
				float mix = g_bDspOff ? 1.0f : DSP_ROOM_MIX;
				MIX_MixPaintbuffers(SOUND_BUFFER_ROOM, SOUND_BUFFER_FACING, SOUND_BUFFER_PAINT, count, mix);
			}

			// restore room buffer surround status, in case we upconverted it
			proom.Surround = room_fsurround_sav;
			proom.SurroundCenter = room_fsurround_center_sav;

			// Apply underwater fx dsp_water (serial in-line)
			if (isUnderwater) {
				// BUG: if out of water, previous delays will be heard. must clear dly buffers.
				g_AudioDevice.ApplyDSPEffects(idsp_water, MIX_GetPFrontFromIPaint(SOUND_BUFFER_PAINT), MIX_GetPRearFromIPaint(SOUND_BUFFER_PAINT), MIX_GetPCenterFromIPaint(SOUND_BUFFER_PAINT), count);
			}

			// find dsp gain
			SDEBUG_GetAvgIn(SOUND_BUFFER_PAINT, count);

			// Apply player fx dsp_player (serial in-line) - does nothing if dsp fx are disabled
			g_AudioDevice.ApplyDSPEffects(idsp_player, MIX_GetPFrontFromIPaint(SOUND_BUFFER_PAINT), MIX_GetPRearFromIPaint(SOUND_BUFFER_PAINT), MIX_GetPCenterFromIPaint(SOUND_BUFFER_PAINT), count);

			// display dsp gain
			SDEBUG_GetAvgOut(SOUND_BUFFER_PAINT, count);

			/*
					// apply left/center/right/lrear/rrear spatial delays to paint buffer

					if ( b_spatial_delays )
						g_AudioDevice->ApplyDSPEffects( idsp_spatial, MIX_GetPFrontFromIPaint(SOUND_BUFFER_PAINT),  MIX_GetPRearFromIPaint(SOUND_BUFFER_PAINT), MIX_GetPCenterFromIPaint(SOUND_BUFFER_PAINT), count );
			*/
			// Add dry buffer, set output gain to water * player dsp gain (both 1.0 if not active)

			MIX_MixPaintbuffers(SOUND_BUFFER_PAINT, SOUND_BUFFER_DRY, SOUND_BUFFER_PAINT, count, 1.0f);

			// clip all values > 16 bit down to 16 bit
			// NOTE: This is required - the hardware buffer transfer routines no longer perform clipping.
			MIX_CompressPaintbuffer(SOUND_BUFFER_PAINT, count);

			// transfer SOUND_BUFFER_PAINT paintbuffer out to DMA buffer
			MIX_SetCurrentPaintbuffer(SOUND_BUFFER_PAINT);

			g_AudioDevice.TransferSamples(end);

			g_paintedtime = end;
		}

		// the cache needs to hold the audio in memory during mixing, so tell it that mixing is complete
		wavedatacache.OnMixEnd();
	}

	// Applies volume scaling (evenly) to all fl,fr,rl,rr volumes
	// used for voice ducking and panning between various mix busses
	// Ensures if mixing to speaker buffer, only speaker sounds pass through

	// Called just before mixing wav data to current paintbuffer.
	// a) if another player in a multiplayer game is speaking, scale all volumes down.
	// b) if mixing to SOUND_BUFFER_ROOM, scale all volumes by ch.dspmix and dsp_room gain
	// c) if mixing to SOUND_BUFFER_FACINGAWAY, scale all volumes by ch.dspface and dsp_facingaway gain
	// d) If SURROUND_ON, but buffer is not surround, recombined front/rear volumes

	// returns false if channel is to be entirely skipped.

	public static bool MIX_ScaleChannelVolume(PaintBuffer ppaint, Channel channel, Span<int> volume, int mixchans) {
		int i;
		int mixflag = ppaint.Flags;
		float scale;
		char wavtype = channel.WavType;
		float dspmix;

		// copy current channel volumes into output array

		ChannelCopyVolumes(channel, volume, 0, CCHANVOLUMES);

		dspmix = channel.DspMix;

		// if dsp is off, or room dsp is off, mix 0% to mono room buffer, 100% to facing buffer

		if (g_bDspOff || DSP_RoomDSPIsOff())
			dspmix = 0.0f;

		// duck all sound volumes except speaker's voice
		int duckScale = Math.Min((int)(g_DuckScale * 256), g_SND_VoiceOverdriveInt);
		if (duckScale < 256) {
			if (channel.Mixer != null) {
				AudioSourceBase source = channel.Mixer.GetSource();
				if (!source.IsVoiceSource()) {
					// Apply voice overdrive..
					for (i = 0; i < CCHANVOLUMES; i++)
						volume[i] = (volume[i] * duckScale) >> 8;
				}
			}
		}

		// If mixing to the room buss, adjust volume based on channel's dspmix setting.
		// dspmix is DSP_MIX_MAX (~0.78) if sound is far from player, DSP_MIX_MIN (~0.24) if sound is near player

		if ((mixflag & SOUND_BUSS_ROOM) != 0) {
			// set dsp mix volume, scaled by global dsp_volume

			float dspmixvol = Math.Min(dspmix * g_dsp_volume, 1.0f);

			// if dspmix is 1.0, 100% of sound goes to SOUND_BUFFER_ROOM and 0% to SOUND_BUFFER_FACING

			for (i = 0; i < CCHANVOLUMES; i++)
				volume[i] = (int)((float)volume[i] * dspmixvol);
		}

		// If global dsp volume is less than 1, reduce dspmix (ie: increase dry volume)
		// If gloabl dsp volume is greater than 1,  do not reduce dspmix

		if (g_dsp_volume < 1.0F)
			dspmix *= g_dsp_volume;

		// If mixing to facing/facingaway buss, adjust volume based on sound entity's facing direction.

		// If sound directly faces player, ch->dspface = 1.0.  If facing directly away, ch->dspface = -1.0.
		// mix to lowpass buffer if facing away, to allpass if facing

		// scale 1.0 - facing player, scale 0, facing away

		scale = (channel.DspFace + 1.0F) / 2.0F;

		// UNDONE: get front cone % from channel to set this.

		// bias scale such that 1.0 to 'cone' is considered facing.  Facing cone narrows as cone -> 1.0
		// and 'cone' -> 0.0 becomes 1.0 -> 0.0

		float cone = 0.6f;

		scale = scale * (1 / cone);

		scale = Math.Clamp(scale, 0.0f, 1.0f);

		// pan between facing and facing away buffers

		// if ( !g_bdirectionalfx || wavtype == CHAR_DOPPLER || wavtype == CHAR_OMNI || (wavtype == CHAR_DIRECTIONAL && mixchans == 2) )
		if (!g_bdirectionalfx || wavtype != (char)SoundChars.Directional) {
			// if no directional fx mix 0% to facingaway buffer
			// if wavtype is DOPPLER, mix 0% to facingaway buffer - DOPPLER wavs have a custom mixer
			// if wavtype is OMNI, mix 0% to facingaway buffer - OMNI wavs have no directionality
			// if wavtype is DIRECTIONAL and stereo encoded, mix 0% to facingaway buffer - DIRECTIONAL STEREO wavs have a custom mixer

			scale = 1.0f;
		}

		if ((mixflag & SOUND_BUSS_FACING) != 0) {
			// facing player
			// if dspface is 1.0, 100% of sound goes to SOUND_BUFFER_FACING

			for (i = 0; i < CCHANVOLUMES; i++)
				volume[i] = (int)((float)volume[i] * scale * (1.0 - dspmix));
		}
		else if ((mixflag & SOUND_BUSS_FACINGAWAY) != 0) {
			// facing away from player
			// if dspface is 0.0, 100% of sound goes to SOUND_BUFFER_FACINGAWAY

			for (i = 0; i < CCHANVOLUMES; i++)
				volume[i] = (int)((float)volume[i] * (1.0 - scale) * (1.0 - dspmix));
		}

		// NOTE: this must occur last in this routine:

		if (g_AudioDevice!.IsSurround() && !ppaint.Surround) {
			// if 4ch or 5ch spatialization on, but current mix buffer is 2ch,
			// recombine front + rear volumes (revert to 2ch spatialization)

			volume[IFRONT_RIGHT] += volume[IREAR_RIGHT];
			volume[IFRONT_LEFT] += volume[IREAR_LEFT];

			volume[IFRONT_RIGHTD] += volume[IREAR_RIGHTD];
			volume[IFRONT_LEFTD] += volume[IREAR_LEFTD];

			// if 5 ch, recombine center channel vol

			if (g_AudioDevice.IsSurroundCenter()) {
				volume[IFRONT_RIGHT] += volume[IFRONT_CENTER] / 2;
				volume[IFRONT_LEFT] += volume[IFRONT_CENTER] / 2;

				volume[IFRONT_RIGHTD] += volume[IFRONT_CENTERD] / 2;
				volume[IFRONT_LEFTD] += volume[IFRONT_CENTERD] / 2;
			}

			// clear rear & center volumes

			volume[IREAR_RIGHT] = 0;
			volume[IREAR_LEFT] = 0;
			volume[IFRONT_CENTER] = 0;

			volume[IREAR_RIGHTD] = 0;
			volume[IREAR_LEFTD] = 0;
			volume[IFRONT_CENTERD] = 0;

		}

#if GMOD_DLL
		if (channel.Mixer != null && !(channel.Flags.Dry && channel.DistMult <= 0)
			&& channel.Mixer.GetSource().GetAudioSourceType() != AudioSourceType.AUDIO_SOURCE_VOICE) {
			float sfxVolume = SndGain.volume_sfx.GetFloat();
			for (i = 0; i < CCHANVOLUMES; i++)
				volume[i] = Math.Clamp((int)(volume[i] * sfxVolume), 0, 255);
		}
#endif

		bool fzerovolume = true;

		for (i = 0; i < CCHANVOLUMES; i++) {
			volume[i] = Math.Clamp(volume[i], 0, 255);

			if (volume[i] != 0)
				fzerovolume = false;
		}


		if (fzerovolume) {
			// DevMsg ("Skipping mix of 0 volume sound! \n");
			return false;
		}

		return true;
	}


	//===============================================================================
	// Low level mixing routines
	//===============================================================================
	static void Snd_WriteLinearBlastStereo16(ReadOnlySpan<int> snd_p, Span<short> snd_out) {
		for (int i = 0; i < snd_linear_count; i += 2) {
			// scale and clamp left 16bit signed: [0x8000, 0x7FFF]
			int val = (snd_p[i] * snd_vol) >> 8;
			snd_out[i] = (short)Math.Clamp(val, -32768, 32767);

			// scale and clamp right 16bit signed: [0x8000, 0x7FFF]
			int val2 = (snd_p[i + 1] * snd_vol) >> 8;
			snd_out[i + 1] = (short)Math.Clamp(val2, -32768, 32767);
		}
	}

	public static void SND_InitScaletable() {
		int i, j;

		for (i = 0; i < SND_SCALE_LEVELS; i++)
			for (j = 0; j < 256; j++)
				snd_scaletable[i * 256 + j] = ((sbyte)j) * i * (1 << SND_SCALE_SHIFT);
	}

	static void SND_PaintChannelFrom8(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data8, int count) {
		output = output[..count];
		data8 = data8[..count];
		ReadOnlySpan<int> lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		ReadOnlySpan<int> rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		for (int i = 0; i < output.Length; i++) {
			int data = data8[i];

			output[i].Left += lscale[data];
			output[i].Right += rscale[data];
		}
	}

	//===============================================================================
	// SOFTWARE MIXING ROUTINES
	//===============================================================================

	// UNDONE: optimize these

	// grab samples from left source channel only and mix as if mono.
	// volume array contains appropriate spatialization volumes for doppler left (incoming sound)
	static void SW_Mix8StereoDopplerLeft(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		for (int i = 0; i < output.Length; i++) {
			output[i].Left += lscale[data[sampleIndex]];
			output[i].Right += rscale[data[sampleIndex]];
			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	// grab samples from right source channel only and mix as if mono.
	// volume array contains appropriate spatialization volumes for doppler right (outgoing sound)
	static void SW_Mix8StereoDopplerRight(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice(sampleIndex, 2);
			output[i].Left += lscale[sample[1]];
			output[i].Right += rscale[sample[1]];
			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}

	}


	// grab samples from left source channel only and mix as if mono.
	// volume array contains appropriate spatialization volumes for doppler left (incoming sound)

	static void SW_Mix16StereoDopplerLeft(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;

		for (int i = 0; i < output.Length; i++) {
			output[i].Left += (volume[0] * (int)data[sampleIndex]) >> 8;
			output[i].Right += (volume[1] * (int)data[sampleIndex]) >> 8;

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}


	// grab samples from right source channel only and mix as if mono.
	// volume array contains appropriate spatialization volumes for doppler right (outgoing sound)

	static void SW_Mix16StereoDopplerRight(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice(sampleIndex, 2);
			output[i].Left += (volume[0] * (int)sample[1]) >> 8;
			output[i].Right += (volume[1] * (int)sample[1]) >> 8;

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	// mix left wav (front facing) with right wav (rear facing) based on soundfacing direction
	static void SW_Mix8StereoDirectional(float soundfacing, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		int x;
		int l, r;
		sbyte lb, rb;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		// if soundfacing -1.0, sound source is facing away from player
		// if soundfacing 0.0, sound source is perpendicular to player
		// if soundfacing 1.0, sound source is facing player

		int frontmix = (int)(256.0f * ((1.0f + soundfacing) / 2.0f));   // 0 -> 256

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice(sampleIndex, 2);
			lb = (sbyte)sample[0];      // get left byte
			rb = (sbyte)sample[1];  // get right byte

			l = lb;
			r = rb;

			x = r + (((l - r) * frontmix) >> 8);

			output[i].Left += lscale[x & 0xFF];         // multiply by volume and convert to 16 bit
			output[i].Right += rscale[x & 0xFF];

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}


	// mix left wav (front facing) with right wav (rear facing) based on soundfacing direction
	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.
	static void SW_Mix8StereoDirectional_Interp(float soundfacing, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interpl, interpr;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		int x;

		// if soundfacing -1.0, sound source is facing away from player
		// if soundfacing 0.0, sound source is perpendicular to player
		// if soundfacing 1.0, sound source is facing player

		int frontmix = (int)(256.0f * ((1.0f + soundfacing) / 2.0f));   // 0 -> 256

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 4);
			// interpolate between first & second sample (the samples bordering sampleFrac12 fraction)

			first = (sbyte)sample[0];       // left byte
			second = (sbyte)sample[2];

			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = (sbyte)sample[1];   // right byte
			second = (sbyte)sample[3];

			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			// crossfade between right/left based on directional mix

			x = interpr + (((interpl - interpr) * frontmix) >> 8);

			output[i].Left += lscale[x & 0xFF];             // scale and convert to 16 bit
			output[i].Right += rscale[x & 0xFF];

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}


	// mix left wav (front facing) with right wav (rear facing) based on soundfacing direction

	static void SW_Mix16StereoDirectional(float soundfacing, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;

		int x;
		int l, r;

		// if soundfacing -1.0, sound source is facing away from player
		// if soundfacing 0.0, sound source is perpendicular to player
		// if soundfacing 1.0, sound source is facing player

		int frontmix = (int)(256.0f * ((1.0f + soundfacing) / 2.0f));   // 0 -> 256

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 2);
			// get left, right samples

			l = sample[0];
			r = sample[1];

			// crossfade between left & right based on front/rear facing

			x = r + (((l - r) * frontmix) >> 8);

			output[i].Left += (volume[0] * x) >> 8;
			output[i].Right += (volume[1] * x) >> 8;

			sampleFrac += rateScaleFix;
			sampleIndex += (fixedint)(FIX_INTPART(sampleFrac) << 1);
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	// mix left wav (front facing) with right wav (rear facing) based on soundfacing direction
	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.

	static void SW_Mix16StereoDirectional_Interp(float soundfacing, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int x;
		int first, second, interpl, interpr;

		// if soundfacing -1.0, sound source is facing away from player
		// if soundfacing 0.0, sound source is perpendicular to player
		// if soundfacing 1.0, sound source is facing player

		int frontmix = (int)(256.0f * ((1.0f + soundfacing) / 2.0f));   // 0 -> 256

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 4);
			// get interpolated left, right samples

			first = sample[0];
			second = sample[2];

			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = sample[1];
			second = sample[3];

			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			// crossfade between left & right based on front/rear facing

			x = interpr + (((interpl - interpr) * frontmix) >> 8);

			output[i].Left += (volume[0] * x) >> 8;
			output[i].Right += (volume[1] * x) >> 8;

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}


	// distance variant wav (left is close, right is far)
	static void SW_Mix8StereoDistVar(float distmix, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		int x;
		int l, r;
		sbyte lb, rb;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		// distmix 0 - sound is near player (100% wav left)
		// distmix 1.0 - sound is far from player (100% wav right)

		int nearmix = (int)(256.0f * (1.0f - distmix));
		int farmix = (int)(256.0f * distmix);

		// if mixing at max or min range, skip crossfade (KDB: perf)

		if (nearmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<byte> sample = data.Slice(sampleIndex, 2);
				rb = (sbyte)sample[1];  // get right byte
				x = rb;

				output[i].Left += lscale[x & 0xFF]; // multiply by volume and convert to 16 bit
				output[i].Right += rscale[x & 0xFF];

				sampleFrac += rateScaleFix;
				sampleIndex += FIX_INTPART(sampleFrac) << 1;
				sampleFrac = FIX_FRACPART(sampleFrac);
			}
			return;
		}

		if (farmix == 0) {
			for (int i = 0; i < output.Length; i++) {

				lb = (sbyte)data[sampleIndex];              // get left byte
				x = lb;

				output[i].Left += lscale[x & 0xFF]; // multiply by volume and convert to 16 bit
				output[i].Right += rscale[x & 0xFF];

				sampleFrac += rateScaleFix;
				sampleIndex += FIX_INTPART(sampleFrac) << 1;
				sampleFrac = FIX_FRACPART(sampleFrac);
			}
			return;
		}

		// crossfade left/right

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice(sampleIndex, 2);

			lb = (sbyte)sample[0];      // get left byte
			rb = (sbyte)sample[1];  // get right byte

			l = lb;
			r = rb;

			x = l + (((r - l) * farmix) >> 8);

			output[i].Left += lscale[x & 0xFF]; // multiply by volume and convert to 16 bit
			output[i].Right += rscale[x & 0xFF];

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}


	// distance variant wav (left is close, right is far)
	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.
	static void SW_Mix8StereoDistVar_Interp(float distmix, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int x;

		// distmix 0 - sound is near player (100% wav left)
		// distmix 1.0 - sound is far from player (100% wav right)

		int nearmix = (int)(256.0f * (1.0f - distmix));
		int farmix = (int)(256.0f * distmix);

		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interpl, interpr;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		// if mixing at max or min range, skip crossfade (KDB: perf)

		if (nearmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 4);
				first = (sbyte)sample[1];   // right sample
				second = (sbyte)sample[3];

				interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

				output[i].Left += lscale[interpr & 0xFF];                   // scale and convert to 16 bit
				output[i].Right += rscale[interpr & 0xFF];

				sampleFrac14 += rateScaleFix14;
				sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
				sampleFrac14 = FIX_FRACPART14(sampleFrac14);

			}
			return;
		}

		if (farmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 3);
				first = (sbyte)sample[0];       // left sample
				second = (sbyte)sample[2];

				interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

				output[i].Left += lscale[interpl & 0xFF];                   // scale and convert to 16 bit
				output[i].Right += rscale[interpl & 0xFF];

				sampleFrac14 += rateScaleFix14;
				sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
				sampleFrac14 = FIX_FRACPART14(sampleFrac14);
			}
			return;
		}

		// crossfade left/right

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 4);
			// interpolate between first & second sample (the samples bordering sampleFrac14 fraction)

			first = (sbyte)sample[0];
			second = (sbyte)sample[2];

			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = (sbyte)sample[1];
			second = (sbyte)sample[3];

			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			// crossfade between left and right based on distance mix

			x = interpl + (((interpr - interpl) * farmix) >> 8);

			output[i].Left += lscale[x & 0xFF];             // scale and convert to 16 bit
			output[i].Right += rscale[x & 0xFF];

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}


	// distance variant wav (left is close, right is far)

	static void SW_Mix16StereoDistVar(float distmix, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		int x;
		int l, r;

		// distmix 0 - sound is near player (100% wav left)
		// distmix 1.0 - sound is far from player (100% wav right)

		int nearmix = (int)(256.0f * (1.0f - distmix));
		int farmix = (int)(256.0f * distmix);

		// if mixing at max or min range, skip crossfade (KDB: perf)

		if (nearmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<short> sample = data.Slice(sampleIndex, 2);
				x = sample[1];  // right sample

				output[i].Left += (volume[0] * x) >> 8;
				output[i].Right += (volume[1] * x) >> 8;

				sampleFrac += rateScaleFix;
				sampleIndex += FIX_INTPART(sampleFrac) << 1;
				sampleFrac = FIX_FRACPART(sampleFrac);
			}
			return;
		}

		if (farmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				x = data[sampleIndex];      // left sample

				output[i].Left += (volume[0] * x) >> 8;
				output[i].Right += (volume[1] * x) >> 8;

				sampleFrac += rateScaleFix;
				sampleIndex += FIX_INTPART(sampleFrac) << 1;
				sampleFrac = FIX_FRACPART(sampleFrac);
			}
			return;
		}

		// crossfade left/right

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice(sampleIndex, 2);
			l = sample[0];
			r = sample[1];

			x = l + (((r - l) * farmix) >> 8);

			output[i].Left += (volume[0] * x) >> 8;
			output[i].Right += (volume[1] * x) >> 8;

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	// distance variant wav (left is close, right is far)
	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.

	static void SW_Mix16StereoDistVar_Interp(float distmix, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int x;

		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interpl, interpr;


		// distmix 0 - sound is near player (100% wav left)
		// distmix 1.0 - sound is far from player (100% wav right)

		int nearmix = (int)(256.0f * (1.0f - distmix));
		int farmix = (int)(256.0f * distmix);

		// if mixing at max or min range, skip crossfade (KDB: perf)

		if (nearmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 4);
				first = sample[1];      // right sample
				second = sample[3];
				interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

				output[i].Left += (volume[0] * interpr) >> 8;
				output[i].Right += (volume[1] * interpr) >> 8;

				sampleFrac14 += rateScaleFix14;
				sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
				sampleFrac14 = FIX_FRACPART14(sampleFrac14);
			}
			return;
		}

		if (farmix == 0) {
			for (int i = 0; i < output.Length; i++) {
				ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 3);
				first = sample[0];      // left sample
				second = sample[2];
				interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

				output[i].Left += (volume[0] * interpl) >> 8;
				output[i].Right += (volume[1] * interpl) >> 8;

				sampleFrac14 += rateScaleFix14;
				sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
				sampleFrac14 = FIX_FRACPART14(sampleFrac14);
			}
			return;
		}

		// crossfade left/right

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 4);
			first = sample[0];
			second = sample[2];
			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = sample[1];
			second = sample[3];
			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			// crossfade between left & right samples

			x = interpl + (((interpr - interpl) * farmix) >> 8);

			output[i].Left += (volume[0] * x) >> 8;
			output[i].Right += (volume[1] * x) >> 8;

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}

	static void SW_Mix8Mono(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		// Not using pitch shift?
		if (rateScaleFix == (fixedint)FIX(1)) {
			// native code
			SND_PaintChannelFrom8(output, volume, data, outCount);
			return;
		}

		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		for (int i = 0; i < output.Length; i++) {
			output[i].Left += lscale[data[sampleIndex]];
			output[i].Right += rscale[data[sampleIndex]];
			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac);
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}


	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.
	static void SW_Mix8Mono_Interp(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interp;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		// iterate 0th sample to outCount-1 sample

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 2);
			// interpolate between first & second sample (the samples bordering sampleFrac12 fraction)

			first = (sbyte)sample[0];
			second = (sbyte)sample[1];

			interp = first + (((second - first) * (int)sampleFrac14) >> 14);

			output[i].Left += lscale[interp & 0xFF];                // multiply by volume and convert to 16 bit
			output[i].Right += rscale[interp & 0xFF];

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)FIX_INTPART14(sampleFrac14);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}

	static void SW_Mix8Stereo(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice(sampleIndex, 2);
			output[i].Left += lscale[sample[0]];
			output[i].Right += rscale[sample[1]];

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}


	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.
	static void SW_Mix8Stereo_Interp(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interpl, interpr;
		ReadOnlySpan<int> lscale, rscale;

		lscale = SndScaleTable(volume[0] >> SND_SCALE_SHIFT);
		rscale = SndScaleTable(volume[1] >> SND_SCALE_SHIFT);

		// iterate 0th sample to outCount-1 sample

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<byte> sample = data.Slice((int)sampleIndex, 4);
			// interpolate between first & second sample (the samples bordering sampleFrac12 fraction)

			first = (sbyte)sample[0];       // left
			second = (sbyte)sample[2];

			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = (sbyte)sample[1];   // right
			second = (sbyte)sample[3];

			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			output[i].Left += lscale[interpl & 0xFF];               // multiply by volume and convert to 16 bit
			output[i].Right += rscale[interpr & 0xFF];

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}

	static void SW_Mix16Mono_Shift(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int vol0 = volume[0];
		int vol1 = volume[1];

		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;

		for (int i = 0; i < output.Length; i++) {
			output[i].Left += (vol0 * (int)data[sampleIndex]) >> 8;
			output[i].Right += (vol1 * (int)data[sampleIndex]) >> 8;
			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac);
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	static void SW_Mix16Mono_NoShift(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		data = data[..outCount];
		int vol0 = volume[0];
		int vol1 = volume[1];
		for (int i = 0; i < output.Length; i++) {
			int x = data[i];
			output[i].Left += (x * vol0) >> 8;
			output[i].Right += (x * vol1) >> 8;
		}
	}

	static void SW_Mix16Mono(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		if (rateScaleFix == (fixedint)FIX(1))
			SW_Mix16Mono_NoShift(output, volume, data, outCount);
		else
			SW_Mix16Mono_Shift(output, volume, data, inputOffset, rateScaleFix, outCount);
	}

	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.

	static void SW_Mix16Mono_Interp(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interp;

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 2);
			first = sample[0];
			second = sample[1];

			interp = first + (((second - first) * (int)sampleFrac14) >> 14);

			output[i].Left += (volume[0] * interp) >> 8;
			output[i].Right += (volume[1] * interp) >> 8;

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)FIX_INTPART14(sampleFrac14);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}

	static void SW_Mix16Stereo(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		int sampleIndex = 0;
		fixedint sampleFrac = (fixedint)inputOffset;

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice(sampleIndex, 2);
			output[i].Left += (volume[0] * (int)sample[0]) >> 8;
			output[i].Right += (volume[1] * (int)sample[1]) >> 8;

			sampleFrac += rateScaleFix;
			sampleIndex += FIX_INTPART(sampleFrac) << 1;
			sampleFrac = FIX_FRACPART(sampleFrac);
		}
	}

	// interpolating pitch shifter - sample(s) from preceding buffer are preloaded in
	// pData buffer, ensuring we can always provide 'outCount' samples.

	static void SW_Mix16Stereo_Interp(Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		output = output[..outCount];
		volume = volume[..2];
		fixedint sampleIndex = 0;
		fixedint rateScaleFix14 = FIX_28TO14(rateScaleFix);     // convert 28 bit fixed point to 14 bit fixed point
		fixedint sampleFrac14 = FIX_28TO14((fixedint)inputOffset);

		int first, second, interpl, interpr;

		for (int i = 0; i < output.Length; i++) {
			ReadOnlySpan<short> sample = data.Slice((int)sampleIndex, 4);
			first = sample[0];
			second = sample[2];

			interpl = first + (((second - first) * (int)sampleFrac14) >> 14);

			first = sample[1];
			second = sample[3];

			interpr = first + (((second - first) * (int)sampleFrac14) >> 14);

			output[i].Left += (volume[0] * interpl) >> 8;
			output[i].Right += (volume[1] * interpr) >> 8;

			sampleFrac14 += rateScaleFix14;
			sampleIndex += (fixedint)(FIX_INTPART14(sampleFrac14) << 1);
			sampleFrac14 = FIX_FRACPART14(sampleFrac14);
		}
	}
	// return true if mixer should use high quality pitch interpolation for this sound

	public static bool FUseHighQualityPitch(Channel channel) {
		// do not use interpolating pitch shifter if:
		// low quality flag set on sound (ie: wave name is prepended with CHAR_FAST_PITCH)
		// or pitch has no fractional part
		// or snd_pitchquality is 0
		if (snd_pitchquality.GetInt() == 0 || channel.Flags.FastPitch)
			return false;

		return channel.Pitch != MathF.Floor(channel.Pitch);
	}

	//===============================================================================
	// DISPATCHERS FOR MIXING ROUTINES
	//===============================================================================
	public static void Mix8MonoWavtype(Channel channel, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		if (FUseHighQualityPitch(channel))
			SW_Mix8Mono_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
		else
			SW_Mix8Mono(output, volume, data, inputOffset, rateScaleFix, outCount);
	}

	public static void Mix16MonoWavtype(Channel channel, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		if (FUseHighQualityPitch(channel))
			SW_Mix16Mono_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
		else
			// fast native coded mixers with lower quality pitch shift
			SW_Mix16Mono(output, volume, data, inputOffset, rateScaleFix, outCount);
	}

	public static void Mix8StereoWavtype(Channel channel, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<byte> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		switch ((SoundChars)channel.WavType) {
			case SoundChars.Doppler:
				SW_Mix8StereoDopplerLeft(output, volume, data, inputOffset, rateScaleFix, outCount);
				SW_Mix8StereoDopplerRight(output, volume[IFRONT_LEFTD..], data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.Directional:
				if (FUseHighQualityPitch(channel))
					SW_Mix8StereoDirectional_Interp(channel.DspFace, output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix8StereoDirectional(channel.DspFace, output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.DistVariant:
				if (FUseHighQualityPitch(channel))
					SW_Mix8StereoDistVar_Interp(channel.DistMix, output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix8StereoDistVar(channel.DistMix, output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.Omni:
				// non directional stereo - all channel volumes are the same
				if (FUseHighQualityPitch(channel))
					SW_Mix8Stereo_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix8Stereo(output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			default:
			case SoundChars.SpatialStereo:
				if (FUseHighQualityPitch(channel))
					SW_Mix8Stereo_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix8Stereo(output, volume, data, inputOffset, rateScaleFix, outCount);
				break;
		}
	}


	public static void Mix16StereoWavtype(Channel channel, Span<PortableSamplePair> output, ReadOnlySpan<int> volume, ReadOnlySpan<short> data, int inputOffset, fixedint rateScaleFix, int outCount) {
		switch ((SoundChars)channel.WavType) {
			case SoundChars.Doppler:
				SW_Mix16StereoDopplerLeft(output, volume, data, inputOffset, rateScaleFix, outCount);
				SW_Mix16StereoDopplerRight(output, volume[IFRONT_LEFTD..], data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.Directional:
				if (FUseHighQualityPitch(channel))
					SW_Mix16StereoDirectional_Interp(channel.DspFace, output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix16StereoDirectional(channel.DspFace, output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.DistVariant:
				if (FUseHighQualityPitch(channel))
					SW_Mix16StereoDistVar_Interp(channel.DistMix, output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix16StereoDistVar(channel.DistMix, output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			case SoundChars.Omni:
				// non directional stereo - all channel volumes are same
				if (FUseHighQualityPitch(channel))
					SW_Mix16Stereo_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix16Stereo(output, volume, data, inputOffset, rateScaleFix, outCount);
				break;

			default:
			case SoundChars.SpatialStereo:
				if (FUseHighQualityPitch(channel))
					SW_Mix16Stereo_Interp(output, volume, data, inputOffset, rateScaleFix, outCount);
				else
					SW_Mix16Stereo(output, volume, data, inputOffset, rateScaleFix, outCount);
				break;
		}
	}


	//===============================================================================
	// Client entity mouth movement code.  Set entity mouthopen variable, based
	// on the sound envelope of the voice channel playing.
	// KellyB 10/22/97
	//===============================================================================

	static IClientEntityList? entityList;
	static IClientEntityList? EntityList => entityList ??= OptionalSingleton<IClientEntityList>();

	// called when voice channel is first opened on this entity
	static MouthInfo? GetMouthInfoForChannel(Channel channel) {
		// If it's a sound inside the client UI, ask the client for the mouthinfo
		if (channel.SoundSource == SOUND_FROM_UI_PANEL)
			return soundServices.GetClientUIMouthInfo();

		int mouthentity = channel.SpeakerEntity == -1 ? channel.SoundSource : channel.SpeakerEntity;

		IClientEntity? clientEntity = EntityList?.GetClientEntity(mouthentity);

		if (clientEntity == null)
			return null;

		return clientEntity.GetMouth();
	}

	public static void SND_InitMouth(Channel channel) {
		if (SND_IsMouth(channel)) {
			MouthInfo? mouth = GetMouthInfoForChannel(channel);
			// init mouth movement vars
			if (mouth != null) {
				mouth.MouthOpen = 0;
				mouth.SndAvg = 0;
				mouth.SndCount = 0;
				if (channel.Sfx!.GetSource() != null && channel.Sfx.GetSource()!.GetSentence() != null)
					mouth.AddSource(channel.Sfx.GetSource(), channel.Flags.IgnorePhonemes);
			}
		}
	}

	// called when channel stops

	public static void SND_CloseMouth(Channel channel) {
		if (SND_IsMouth(channel)) {
			MouthInfo? mouth = GetMouthInfoForChannel(channel);
			if (mouth != null) {
				// shut mouth
				int idx = mouth.GetIndexForSource(channel.Sfx?.GetSource());

				if (idx != MouthInfo.UNKNOWN_VOICE_SOURCE)
					mouth.RemoveSourceByIndex(idx);
				else
					mouth.ClearVoiceSources();
				mouth.MouthOpen = 0;
			}
		}
	}

	const int CAVGSAMPLES = 10;
	// need this to make the debug code below work.
	//#include "snd_wave_source.h"
	[SkipLocalsInit]
	public static void SND_MoveMouth8(Channel ch, AudioSourceBase source, int count) {
		int data;
		int i;
		int savg;
		int scount;

		MouthInfo? mouth = GetMouthInfoForChannel(ch);

		if (mouth == null)
			return;

		if (source.GetSentence() != null) {
			int idx = mouth.GetIndexForSource(source);

			if (idx == MouthInfo.UNKNOWN_VOICE_SOURCE) {
				if (mouth.AddSource(source, ch.Flags.IgnorePhonemes) == null)
					DevMsg(1, $"out of voice sources, won't lipsync {ch.Sfx!.GetName()}\n");
			}
			else {
				// Update elapsed time from mixer
				VoiceData? vd = mouth.GetVoiceSource(idx);
				Assert(vd);
				if (vd != null) {
					Assert(source.SampleRate() > 0);

					float elapsed = (float)ch.Mixer!.GetSamplePosition() / (float)source.SampleRate();

					vd.SetElapsedTime(elapsed);
				}
			}
		}

		if (mouth.NeedsEnvelope()) {
			Span<byte> copyBuf = stackalloc byte[AudioSource.AUDIOSOURCE_COPYBUF_SIZE];
			int availableSamples = source.GetOutputData(out ReadOnlySpan<byte> pdata, ch.Mixer!.GetSamplePosition(), count, copyBuf);

			if (pdata.IsEmpty)
				return;

			i = 0;
			scount = mouth.SndCount;
			savg = 0;

			while (i < availableSamples && scount < CAVGSAMPLES) {
				data = (sbyte)pdata[i];
				savg += Math.Abs(data);

				i += 80 + ((byte)data & 0x1F);
				scount++;
			}

			mouth.SndAvg += savg;
			mouth.SndCount = (byte)scount;

			if (mouth.SndCount >= CAVGSAMPLES) {
				mouth.MouthOpen = (byte)(mouth.SndAvg / CAVGSAMPLES);
				mouth.SndAvg = 0;
				mouth.SndCount = 0;
			}
		}
		else
			mouth.MouthOpen = 0;
	}


	public static void SND_UpdateMouth(Channel channel) {
		MouthInfo? m = GetMouthInfoForChannel(channel);
		if (m == null)
			return;

		if (channel.Sfx != null)
			m.AddSource(channel.Sfx.GetSource(), channel.Flags.IgnorePhonemes);
	}


	public static void SND_ClearMouth(Channel channel) {
		MouthInfo? m = GetMouthInfoForChannel(channel);
		if (m == null)
			return;

		if (channel.Sfx != null)
			m.RemoveSource(channel.Sfx.GetSource());
	}


	//-----------------------------------------------------------------------------
	// Purpose:
	// Input  : *pChannel -
	// Output : Returns true on success, false on failure.
	//-----------------------------------------------------------------------------
	public static bool SND_IsMouth(Channel channel) {
		if (channel.SoundSource == SOUND_FROM_UI_PANEL)
			return true;

		if (EntityList == null)
			return false;

		if (channel.EntChannel == (int)SoundEntityChannel.Voice || channel.EntChannel == (int)SoundEntityChannel.Voice2)
			return true;

		if (channel.Sfx != null &&
			 channel.Sfx.GetSource() != null &&
			 channel.Sfx.GetSource()!.GetSentence() != null) {
			return true;
		}

		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	// Input  : *pChannel -
	// Output : Returns true on success, false on failure.
	//-----------------------------------------------------------------------------
	public static bool SND_ShouldPause(Channel channel) {
		return channel.Flags.ShouldPause;
	}

	//===============================================================================
	// Movie recording support
	//===============================================================================

	public static void SND_RecordInit() {
		g_paintedtime = 0;
		g_soundtime = 0;

		// TMP Wave file supports stereo only, so force stereo
		if (snd_surround.GetInt() != 2)
			snd_surround.SetValue(2);
	}

	public static void SND_MovieStart() {
		if (!soundServices.IsMovieRecording())
			return;

		SND_RecordInit();

		// 44k: engine playback rate is now 44100...changed from 22050
		if (soundServices.MovieDoWav())
			WaveCreateTmpFile(soundServices.GetMovieName(), SOUND_DMA_SPEED, 16, 2);
	}

	public static void SND_MovieEnd() {
		if (!soundServices.IsMovieRecording())
			return;

		if (soundServices.MovieDoWav())
			WaveFixupTmpFile(soundServices.GetMovieName());
	}

	public static bool SND_IsRecording() {
		return (IsReplayRendering() || soundServices.IsMovieRecording()) && !soundServices.IsConsoleVisible();
	}

	static void SND_RecordBuffer(ReadOnlySpan<int> snd_p) {
		if (!SND_IsRecording())
			return;

		int i;
		int val;
		int bufferSize = snd_linear_count * sizeof(short);
		Span<short> tmp = snd_linear_count <= 4096 ? stackalloc short[snd_linear_count] : new short[snd_linear_count];

		for (i = 0; i < snd_linear_count; i += 2) {
			val = (snd_p[i] * snd_vol) >> 8;
			tmp[i] = (short)CLIP(val);

			val = (snd_p[i + 1] * snd_vol) >> 8;
			tmp[i + 1] = (short)CLIP(val);
		}

		if (soundServices.MovieDoWav())
			WaveAppendTmpFile(soundServices.GetMovieName(), MemoryMarshal.AsBytes(tmp), 16, snd_linear_count);

		if (soundServices.MovieDoVideoSound())
			soundServices.AppendMovieAudioSamples(tmp[..snd_linear_count]);
	}
}
