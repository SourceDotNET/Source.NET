namespace Source.Common.Server;

/// <summary>
/// This module defines the IVoiceServer interface, which is used by
/// game code to control which clients are listening to which other
/// clients' voice streams.
/// </summary>
public interface IVoiceServer
{
	// Use these to setup who can hear whose voice.
	// Pass in client indices (which are their ent indices - 1).
	bool GetClientListening(int receiver, int sender);
	bool SetClientListening(int receiver, int sender, bool listen);
	bool SetClientProximity(int receiver, int sender, bool useProximity);
#if GMOD_DLL
	bool GetClientProximity(int receiver, int sender);
#endif
}
