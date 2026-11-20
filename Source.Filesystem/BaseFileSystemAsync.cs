using Source.Common.Filesystem;

namespace Source.FileSystem;

public partial class BaseFileSystem
{
	enum JobPriority
	{
		Low,
		Normal,
		High
	}

	class FileAsyncReadJob(BaseFileSystem fileSystem, in FileAsyncRequest request, JobPriority priority) : FSAsyncControl
	{
		public FileAsyncRequest Request = request;
		public FSAsyncCallbackFunc? CallerCallback;
		public object? CallerContext;
		public byte[]? ResultData;
		public int ResultSize;
		public volatile FSAsyncStatus Status = FSAsyncStatus.StatusUnserviced;
		public readonly JobPriority Priority = priority;
		readonly Lock Mutex = new();

		public FSAsyncStatus Execute() {
			using Lock.Scope scope = Mutex.EnterScope();
			if (Status is FSAsyncStatus.StatusPending or FSAsyncStatus.StatusUnserviced) {
				Status = FSAsyncStatus.StatusInProgress;
				Status = DoExecute();
			}
			return Status;
		}

		FSAsyncStatus DoExecute() {
			// todo: async_simulate_delay
			// todo: async fetchers
			return fileSystem.SyncRead(Request);
		}
	}

	readonly Queue<FileAsyncReadJob>[] AsyncJobQueues = [new(), new(), new()];
	readonly SemaphoreSlim AsyncJobSignal = new(0);
	readonly Lock AsyncCallbackMutex = new();
	Thread? AsyncThread;

	void AddAsyncJob(FileAsyncReadJob job) {
		lock (AsyncJobQueues) {
			job.Status = FSAsyncStatus.StatusPending;
			AsyncJobQueues[(int)job.Priority].Enqueue(job);
			if (AsyncThread == null) {
				AsyncThread = new(AsyncThreadMain) { IsBackground = true, Name = "FileSystem Async" };
				AsyncThread.Start();
			}
		}
		AsyncJobSignal.Release();
	}

	FileAsyncReadJob? NextAsyncJob(JobPriority toPriority) {
		lock (AsyncJobQueues) {
			for (int i = (int)JobPriority.High; i >= (int)toPriority; i--) {
				if (AsyncJobQueues[i].TryDequeue(out FileAsyncReadJob? job))
					return job;
			}
		}
		return null;
	}

	void AsyncThreadMain() {
		while (true) {
			AsyncJobSignal.Wait();
			NextAsyncJob(JobPriority.Low)?.Execute();
		}
	}

	static JobPriority ConvertPriority(int priority) => priority == 0 ? JobPriority.Normal : priority > 0 ? JobPriority.High : JobPriority.Low;

	public FSAsyncStatus AsyncReadMultiple(ReadOnlySpan<FileAsyncRequest> requests, Span<FSAsyncControl?> controls) {
		// todo: async_mode
		bool synchronous = requests.Length > 0 && (requests[0].Flags & FSAsyncFlags.Sync) != 0;

		for (int i = 0; i < requests.Length; i++) {
			if (requests[i].Bytes < 0) {
				// todo: CFileAsyncFileSizeJob
				continue;
			}

			FileAsyncReadJob job = new(this, requests[i], ConvertPriority(requests[i].Priority));

			char[] fileName = (requests[i].FileName ?? "").ToCharArray();
			StrTools.FixSlashes(fileName);
			job.Request.FileName = new(fileName);

			job.CallerCallback = job.Request.Callback;
			job.CallerContext = job.Request.Context;
			job.Request.Callback = InterceptCallback;
			job.Request.Context = job;

			// todo: async fetchers

			if (!synchronous)
				AddAsyncJob(job);
			else
				job.Execute();

			if (i < controls.Length)
				controls[i] = job;
		}

		return FSAsyncStatus.OK;
	}

	static void InterceptCallback(in FileAsyncRequest request, int bytesRead, FSAsyncStatus err) {
		FileAsyncReadJob job = (FileAsyncReadJob)request.Context!;
		if (err == FSAsyncStatus.OK && (request.Flags & FSAsyncFlags.FreeDataPtr) == 0) {
			job.ResultData = request.Data;
			job.ResultSize = bytesRead;
		}

		if (job.CallerCallback != null) {
			FileAsyncRequest callerRequest = request;
			callerRequest.Callback = job.CallerCallback;
			callerRequest.Context = job.CallerContext;
			job.CallerCallback(callerRequest, bytesRead, err);
		}
	}

	void DoAsyncCallback(in FileAsyncRequest request, byte[]? data, int bytesRead, FSAsyncStatus err) {
		if (request.Callback == null)
			return;

		using Lock.Scope scope = AsyncCallbackMutex.EnterScope();
		if (data != null && request.Data != data) {
			FileAsyncRequest temp = request;
			temp.Data = data;
			request.Callback(temp, bytesRead, err);
		}
		else
			request.Callback(request, bytesRead, err);
	}

	FSAsyncStatus SyncRead(in FileAsyncRequest request) {
		if (request.Bytes < 0 || request.Offset < 0) {
			Msg($"Invalid async read of {request.FileName}\n");
			DoAsyncCallback(request, null, 0, FSAsyncStatus.ErrFileOpen);
			return FSAsyncStatus.ErrFileOpen;
		}

		// todo: hSpecificAsyncFile (AsyncBeginRead held files)
		IFileHandle? file = Open(request.FileName, FileOpenOptions.Read | FileOpenOptions.Binary, request.PathID);
		if (file == null) {
			DoAsyncCallback(request, null, 0, FSAsyncStatus.ErrFileOpen);
			// todo: fs_log async
			return FSAsyncStatus.ErrFileOpen;
		}

		FSAsyncStatus result;
		using (file) {
			Stream stream = file.Stream;
			long fileSize = stream.Length;

			int bytesToRead = request.Bytes != 0 ? request.Bytes : (int)(fileSize - request.Offset);
			if (bytesToRead < 0)
				bytesToRead = 0;

			byte[] dest;
			if (request.Data != null)
				dest = request.Data;
			else {
				int allocSize = bytesToRead + ((request.Flags & FSAsyncFlags.NullTerminate) != 0 ? 1 : 0);
				// todo: GetOptimalIOConstraints alignment
				dest = request.Alloc != null ? request.Alloc(request.FileName!, allocSize) : new byte[allocSize];
			}

			if (request.Offset + bytesToRead > fileSize)
				Dbg.Warning($"Failed to read {bytesToRead} bytes @ {request.Offset} (end offst = {request.Offset + bytesToRead}) from \"{request.FileName}\" - file only has {fileSize} bytes\n");

			if (request.Offset > 0)
				stream.Seek(request.Offset, SeekOrigin.Begin);

			int toRead = Math.Min(bytesToRead, dest.Length);
			int bytesRead = 0;
			while (bytesRead < toRead) {
				int read = stream.Read(dest, bytesRead, toRead - bytesRead);
				if (read <= 0)
					break;
				bytesRead += read;
			}

			if ((request.Flags & FSAsyncFlags.NullTerminate) != 0 && bytesRead < dest.Length)
				dest[bytesRead] = 0;

			result = bytesRead == 0 && bytesToRead != 0 ? FSAsyncStatus.ErrReading : FSAsyncStatus.OK;
			DoAsyncCallback(request, dest, Math.Min(bytesRead, bytesToRead), result);
		}

		if (WarningLevel >= FileWarningLevel.ReportAllAccessesAsync)
			LogAccessToFile("async", request.FileName, "");

		return result;
	}

	public void AsyncFinishAll(int toPriority = 0) {
		while (NextAsyncJob(ConvertPriority(toPriority)) is FileAsyncReadJob job)
			job.Execute();
	}

	public FSAsyncStatus AsyncFinish(FSAsyncControl control, bool wait = true) {
		if (control is not FileAsyncReadJob job)
			return FSAsyncStatus.ErrUnknownID;

		if (wait)
			return job.Execute();

		return job.Status;
	}

	public FSAsyncStatus AsyncGetResult(FSAsyncControl control, out byte[]? data, out int size) {
		data = null;
		size = 0;
		if (control is not FileAsyncReadJob job)
			return FSAsyncStatus.ErrUnknownID;

		if (job.ResultData != null) {
			data = job.ResultData;
			size = job.ResultSize;
		}
		return job.Status;
	}

	public FSAsyncStatus AsyncStatus(FSAsyncControl control) {
		if (control is not FileAsyncReadJob job)
			return FSAsyncStatus.ErrUnknownID;

		return job.Status;
	}
}
