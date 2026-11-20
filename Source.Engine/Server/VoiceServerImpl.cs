using Source.Common.Server;

namespace Source.Engine.Server;

/// <summary>
/// This module implements the IVoiceServer interface.
/// </summary>
public class VoiceServer : IVoiceServer
{
	public bool GetClientListening(int receiver, int sender) {
		// Make into client indices..
		--receiver;
		--sender;

		if (receiver < 0 || receiver >= sv.GetClientCount() || sender < 0 || sender >= sv.GetClientCount())
			return false;

		return sv.GetClient(sender)!.IsHearingClient(receiver);
	}

	public bool SetClientListening(int receiver, int sender, bool listen) {
		// Make into client indices..
		--receiver;
		--sender;

		if (receiver < 0 || receiver >= sv.GetClientCount() || sender < 0 || sender >= sv.GetClientCount())
			return false;

		GameClient cl = sv.Client(sender);

		cl.VoiceStreams.Set(receiver, listen);

		return true;
	}

	public bool SetClientProximity(int receiver, int sender, bool useProximity) {
		// Make into client indices..
		--receiver;
		--sender;

		if (receiver < 0 || receiver >= sv.GetClientCount() || sender < 0 || sender >= sv.GetClientCount())
			return false;

		GameClient cl = sv.Client(sender);

		cl.VoiceProximity.Set(receiver, useProximity);

		return true;
	}

#if GMOD_DLL
	public bool GetClientProximity(int receiver, int sender) {
		--receiver;
		--sender;

		if (receiver < 0 || receiver >= sv.GetClientCount() || sender < 0 || sender >= sv.GetClientCount())
			return false;

		return sv.Client(sender).VoiceProximity.Get(receiver) != 0;
	}
#endif
}
