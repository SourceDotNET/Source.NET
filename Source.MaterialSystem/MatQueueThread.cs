using System.Runtime.ExceptionServices;

namespace Source.MaterialSystem;

public sealed class MatQueueJob(MatQueueJob.JobKind kind, MatQueuedRenderContext? context = null)
{
	public enum JobKind
	{
		AcquireThreadOwnership,
		ReleaseThreadOwnership,
		ExecuteQueuedContext
	}

	public readonly JobKind Kind = kind;
	public readonly MatQueuedRenderContext? Context = context;

	readonly ManualResetEventSlim Done = new(false);
	ExceptionDispatchInfo? Exception;

	public bool IsFinished() => Done.IsSet;

	public void Reset() {
		Exception = null;
		Done.Reset();
	}

	internal void Complete(Exception? exception) {
		if (exception != null)
			Exception = ExceptionDispatchInfo.Capture(exception);
		Done.Set();
	}

	public void WaitForFinish() {
		Done.Wait();
		ExceptionDispatchInfo? exception = Exception;
		if (exception != null) {
			Exception = null;
			exception.Throw();
		}
	}
}

public sealed class MatQueueThread
{
	readonly MaterialSystem Materials;
	readonly Thread Thread;
	readonly Queue<MatQueueJob> Jobs = new();
	readonly object Sync = new();
	bool Stopping;

	public MatQueueThread(MaterialSystem materials) {
		Materials = materials;
		Thread = new Thread(Run) {
			Name = "MatQueue",
			IsBackground = true
		};
		Thread.Start();
	}

	public void AddJob(MatQueueJob job) {
		lock (Sync) {
			Jobs.Enqueue(job);
			Monitor.Pulse(Sync);
		}
	}

	public void Stop() {
		lock (Sync) {
			Stopping = true;
			Monitor.Pulse(Sync);
		}
		Thread.Join();
	}

	void Run() {
		while (true) {
			MatQueueJob job;
			lock (Sync) {
				while (Jobs.Count == 0 && !Stopping)
					Monitor.Wait(Sync);

				if (Jobs.Count == 0)
					return;

				job = Jobs.Dequeue();
			}

			Exception? exception = null;
			try {
				Materials.ExecuteJob(job);
			}
			catch (Exception e) {
				Warning($"MatQueue job {job.Kind} failed: {e}\n");
				exception = e;
			}
			job.Complete(exception);
		}
	}
}
