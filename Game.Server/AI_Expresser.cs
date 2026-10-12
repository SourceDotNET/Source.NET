global using static Game.Server.AI_SpeechGlobals;

using Game.Shared;

using Source.Common;

namespace Game.Server;

public class AI_TimedSemaphore
{
	public AI_TimedSemaphore() {
		ReleaseTime = 0;
		CurrentTalker.Set(null);
	}

	public void Acquire(TimeUnit_t time, BaseEntity? talker) {
		ReleaseTime = gpGlobals.CurTime + time;
		CurrentTalker.Set(talker);
	}

	public void Release() {
		ReleaseTime = 0;
		CurrentTalker.Set(null);
	}

	public bool IsAvailable(BaseEntity? talker) => (gpGlobals.CurTime > ReleaseTime) || (CurrentTalker.Get() == talker);
	public TimeUnit_t GetReleaseTime() => ReleaseTime;

	public BaseEntity? GetOwner() => CurrentTalker.Get();

	TimeUnit_t ReleaseTime;
	EHANDLE CurrentTalker = new();
}

public static class AI_SpeechGlobals
{
	public static readonly AI_TimedSemaphore g_AIFriendliesTalkSemaphore = new();
	public static readonly AI_TimedSemaphore g_AIFoesTalkSemaphore = new();

	public static AI_TimedSemaphore GetSpeechSemaphore(AI_BaseNPC npc) => npc.IsPlayerAlly() ? g_AIFriendliesTalkSemaphore : g_AIFoesTalkSemaphore;
}

public class AI_Expresser
{
	public AI_Expresser(BaseFlex? outer = null) {
		Outer.Set(outer);
		Sink = null;
		StopTalkTime = 0;
		LastTimeAcceptedSpeak = 0;
		StopTalkTimeWithoutDelay = 0;
		VoicePitch = 100;
	}

	public bool Connect(IAI_ExpresserSink? sink) {
		Sink = sink;
		return true;
	}

	public bool Disconnect(IAI_ExpresserSink? sink) {
		Sink = null;
		return true;
	}

	public IAI_ExpresserSink? GetSink() => Sink;

	public BaseFlex? GetOuter() => Outer.Get();

	public void NoteSpeaking(TimeUnit_t duration, TimeUnit_t delay = 0) {
		duration += delay;

		GetSink()!.OnStartSpeaking();

		if (duration <= 0) {
			StopTalkTime = gpGlobals.CurTime + 3;
			duration = 0;
		}
		else
			StopTalkTime = gpGlobals.CurTime + duration;

		StopTalkTimeWithoutDelay = StopTalkTime - delay;

		if (GetSink()!.UseSemaphore()) {
			AI_TimedSemaphore? semaphore = GetMySpeechSemaphore(GetOuter());
			semaphore?.Acquire(duration, GetOuter());
		}
	}

	public void ForceNotSpeaking() {
		if (IsSpeaking()) {
			StopTalkTime = gpGlobals.CurTime;
			StopTalkTimeWithoutDelay = gpGlobals.CurTime;

			AI_TimedSemaphore? semaphore = GetMySpeechSemaphore(GetOuter());
			if (semaphore != null) {
				if (semaphore.GetOwner() == GetOuter())
					semaphore.Release();
			}
		}
	}

	public bool IsSpeaking() {
		if (LastTimeAcceptedSpeak == gpGlobals.CurTime)
			return true;

		return StopTalkTime > gpGlobals.CurTime;
	}

	public bool CanSpeakConcept(ReadOnlySpan<char> concept) => throw new NotImplementedException();

	public bool Speak(ReadOnlySpan<char> concept) => throw new NotImplementedException();

	public AI_TimedSemaphore? GetMySpeechSemaphore(BaseEntity? npc) {
		if (npc!.MyNPCPointer() == null)
			return null;

		return npc.MyNPCPointer()!.IsPlayerAlly() ? g_AIFriendliesTalkSemaphore : g_AIFoesTalkSemaphore;
	}

	IAI_ExpresserSink? Sink;
	TimeUnit_t StopTalkTime;
	TimeUnit_t LastTimeAcceptedSpeak;
	TimeUnit_t StopTalkTimeWithoutDelay;
	int VoicePitch;
	Handle<BaseFlex> Outer = new();
}
