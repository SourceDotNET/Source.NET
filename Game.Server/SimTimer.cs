using Source;
using Source.Common;

namespace Game.Server;

public class SimpleSimTimer
{
	public const double ST_EPS = 0.001;

	public SimpleSimTimer() {
		Next = -1;
	}

	public void Force() => Next = -1;

	public bool Expired() => gpGlobals.CurTime - Next > -ST_EPS;

	public TimeUnit_t GetNext() => Next;

	public void Set(float interval) => Next = gpGlobals.CurTime + interval;

	public void Set(float minInterval, float maxInterval) {
		if (maxInterval > 0.0)
			Next = gpGlobals.CurTime + RandomFloat(minInterval, maxInterval);
		else
			Next = gpGlobals.CurTime + minInterval;
	}

	public float GetRemaining() {
		float result = (float)(Next - gpGlobals.CurTime);
		if (result < 0)
			return 0;
		return result;
	}

	protected TimeUnit_t Next;

	public static readonly DataMap DataDesc = new(typeof(SimpleSimTimer), [
		DEFINE<SimpleSimTimer>.FIELD(nameof(Next), FieldType.Time),
	]);
}

public class SimTimer : SimpleSimTimer
{
	public SimTimer(float interval = 0.0f, bool startExpired = true) {
		Set(interval, startExpired);
	}

	public void Set(float interval, bool startExpired = true) {
		Interval = interval;
		Next = startExpired ? -1.0 : gpGlobals.CurTime + Interval;
	}

	public void Reset(float interval = -1.0f) {
		if (interval == -1.0f)
			Next = gpGlobals.CurTime + Interval;
		else
			Next = gpGlobals.CurTime + interval;
	}

	public float GetInterval() => Interval;

	float Interval;

	public static readonly new DataMap DataDesc = new(typeof(SimTimer), SimpleSimTimer.DataDesc, [
		DEFINE<SimTimer>.FIELD(nameof(Interval), FieldType.Float),
	]);
}

public class StopwatchBase : SimpleSimTimer
{
	public StopwatchBase() {
		IsRunningValue = false;
	}

	public bool IsRunning() => IsRunningValue;

	public void Stop() => IsRunningValue = false;

	public new bool Expired() => IsRunningValue && base.Expired();

	protected bool IsRunningValue;

	public static readonly new DataMap DataDesc = new(typeof(StopwatchBase), SimpleSimTimer.DataDesc, [
		DEFINE<StopwatchBase>.FIELD(nameof(IsRunningValue), FieldType.Boolean),
	]);
}

public class RandStopwatch : StopwatchBase
{
	public RandStopwatch(float minInterval = 0.0f, float maxInterval = 0.0f) {
		Set(minInterval, maxInterval);
	}

	public new void Set(float minInterval, float maxInterval = 0.0f) {
		MinInterval = minInterval;
		MaxInterval = maxInterval;
	}

	public void Start(float minOverride, float maxOverride = 0.0f) {
		IsRunningValue = true;
		if (maxOverride == 0)
			Next = gpGlobals.CurTime + minOverride;
		else
			Next = gpGlobals.CurTime + RandomFloat(minOverride, maxOverride);
	}

	public void Start() => Start(MinInterval, MaxInterval);

	float MinInterval;
	float MaxInterval;

	public static readonly new DataMap DataDesc = new(typeof(RandStopwatch), StopwatchBase.DataDesc, [
		DEFINE<RandStopwatch>.FIELD(nameof(MinInterval), FieldType.Float),
		DEFINE<RandStopwatch>.FIELD(nameof(MaxInterval), FieldType.Float),
	]);
}
