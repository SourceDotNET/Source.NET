using ICSharpCode.SharpZipLib.BZip2;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Networking;
using Source.Engine.Server;

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;

namespace Source.Engine;

public enum HTTPStatus
{
	Invalid = -1,
	Connecting = 0, 
	Fetch,          
	Done,           
	Aborted,        
	Error           
}

public enum HttpRequestError
{
	None = 0,
	ZeroLengthFile,
	ConnectionClosed,
	InvalidUrl,     
	InvalidProtocol,
	CantBindSocket,
	CantConnect,
	NoHeaders,      
	FileNonexistent,
	Max
}

public class RequestContext
{
	public const int BufferSize = 256;

	public volatile bool ShouldStop;
	public volatile bool ThreadDone;

	public bool IsBZ2;
	public bool AsHTTP;
	public uint RequestID;
	public int DownloadableIndex = -1; // index into the downloadables string table (for NetChan RequestFile)

	public volatile HTTPStatus Status;
	public uint FetchStatus;          
	public HttpRequestError Error;    

	public InlineArray256<char> BaseURL;      
	public InlineArray256<char> URLPath;      
	public InlineArray256<char> AbsLocalPath; 
	public InlineArray256<char> GamePath;     
	public InlineArray256<char> ServerURL;    

	public bool SuppressFileWrite; 

	public InlineArray256<char> CachedTimestamp;

	public uint BytesTotal;   
	public uint BytesCurrent; 
	public uint BytesCached;  

	public byte[]? Data;
	public byte[]? CacheData;

	public object? UserData;
}

public class DownloadCache
{
	public const string CacheDirectory = "cache";
	public const string CacheFilename = "cache/DownloadCache.db";

	public static readonly Color DownloadColor = new(0, 200, 100, 255);
	public static readonly Color DownloadErrorColor = new(200, 100, 100, 255);
	public static readonly Color DownloadCompleteColor = new(100, 200, 100, 255);

	public static readonly ConVar download_debug = new("download_debug", "0", FCvar.DontRecord); // For debug printouts

	public const string DownloadPathID = "download";

	private KeyValues? Cache;
	readonly char[] CacheFileKey = new char[RequestContext.BufferSize + 64];
	readonly char[] TimeStampKey = new char[RequestContext.BufferSize + 64];

	[MemberNotNull(nameof(Cache))]
	public void Init() {
		Cache = new KeyValues("DownloadCache");
		Cache.LoadFromFile(g_pFileSystem, CacheFilename, null);
		g_pFileSystem.CreateDirHierarchy(CacheDirectory, "DEFAULT_WRITE_PATH");
	}

	void BuildKeyNames(ReadOnlySpan<char> gamePath) {
		gamePath = gamePath.SliceNullTerminatedString();
		if (gamePath.IsStringEmpty) {
			CacheFileKey[0] = '\0';
			TimeStampKey[0] = '\0';
			return;
		}

		Span<char> tmp = stackalloc char[RequestContext.BufferSize];
		int len = strcpy(tmp, gamePath);
		for (int i = 0; i < len; i++) {
			if (tmp[i] == '/' || tmp[i] == '\\')
				tmp[i] = '_';
		}

		strcpy(CacheFileKey, "cachefile_");
		strcat(CacheFileKey, tmp[..len]);
		strcpy(TimeStampKey, "timestamp_");
		strcat(TimeStampKey, tmp[..len]);
	}

	public void GetCachedData(RequestContext rc) {
		if (Cache == null)
			return;

		Span<char> cachePath = stackalloc char[MAX_PATH];
		GetCacheFilename(rc, cachePath);

		if (cachePath.IsStringEmpty)
			return;

		IFileHandle? fp = g_pFileSystem.Open(cachePath.SliceNullTerminatedString(), FileOpenOptions.Read | FileOpenOptions.Binary);

		if (fp == null)
			return;

		int size = (int)fp.Stream.Length;
		byte[] data = new byte[size];
		int read = fp.Stream.Read(data);
		fp.Dispose();

		if (read == 0) {
			rc.CacheData = null;
		}
		else {
			rc.CacheData = data;
			BuildKeyNames(rc.GamePath);
			rc.BytesCached = (uint)size;
			strcpy(rc.CachedTimestamp, Cache.GetString(TimeStampKey.AsSpan().SliceNullTerminatedString(), ""));
		}
	}

	public void PersistToDisk(RequestContext rc) {
		if (Cache == null)
			return;

		if (rc.Data != null && rc.BytesTotal != 0) {
			Span<char> absPath = stackalloc char[MAX_PATH];
			if (rc.IsBZ2)
				StrTools.StripExtension(rc.AbsLocalPath, absPath);
			else
				strcpy(absPath, rc.AbsLocalPath);

			string absPathStr = new(absPath.SliceNullTerminatedString());

			if (!FileExistsOnDisk(absPathStr)) {
				CreatePath(absPathStr);

				bool success;
				if (rc.IsBZ2) {
					success = DecompressBZipToDisk(absPathStr, rc.AbsLocalPath, rc.Data, (int)rc.BytesTotal);
				}
				else {
					success = false;
					try {
						using FileStream fp = File.Create(absPathStr);
						fp.Write(rc.Data, 0, (int)rc.BytesTotal);
						success = true;
					}
					catch { }
				}

				if (success) {
					Span<char> cachePath = stackalloc char[MAX_PATH];
					GetCacheFilename(rc, cachePath);
					if (!cachePath.IsStringEmpty)
						g_pFileSystem.RemoveFile(cachePath.SliceNullTerminatedString(), null);

					BuildKeyNames(rc.GamePath);
					KeyValues? kv = Cache.FindKey(CacheFileKey.AsSpan().SliceNullTerminatedString(), false);
					if (kv != null)
						Cache.RemoveSubKey(kv);
					kv = Cache.FindKey(TimeStampKey.AsSpan().SliceNullTerminatedString(), false);
					if (kv != null)
						Cache.RemoveSubKey(kv);
				}
			}
		}

		SaveCacheToDisk();
	}

	public void PersistToCache(RequestContext rc) {
		if (Cache == null || rc.Data == null || rc.BytesTotal == 0 || rc.BytesCurrent == 0)
			return;

		Span<char> cachePath = stackalloc char[MAX_PATH];
		GenerateCacheFilename(rc, cachePath);

		IFileHandle? fp = g_pFileSystem.Open(cachePath.SliceNullTerminatedString(), FileOpenOptions.Write | FileOpenOptions.Binary);
		if (fp != null) {
			fp.Stream.Write(rc.Data, 0, (int)rc.BytesCurrent);
			fp.Dispose();
			SaveCacheToDisk();
		}
	}

	void GetCacheFilename(RequestContext rc, Span<char> cachePath) {
		BuildKeyNames(rc.GamePath);
		ReadOnlySpan<char> path = Cache!.GetString(CacheFileKey.AsSpan().SliceNullTerminatedString(), "");
		if (path.IsStringEmpty || strncmp(path, CacheDirectory, CacheDirectory.Length) != 0) {
			cachePath[0] = '\0';
			return;
		}
		strcpy(cachePath, path);
	}

	void GenerateCacheFilename(RequestContext rc, Span<char> cachePath) {
		GetCacheFilename(rc, cachePath);
		BuildKeyNames(rc.GamePath);

		Cache!.SetString(TimeStampKey.AsSpan().SliceNullTerminatedString(), ((ReadOnlySpan<char>)rc.CachedTimestamp).SliceNullTerminatedString());

		if (cachePath.IsStringEmpty) {
			ReadOnlySpan<char> gamePath = ((ReadOnlySpan<char>)rc.GamePath).SliceNullTerminatedString();
			int slash = gamePath.LastIndexOfAny('/', '\\');
			ReadOnlySpan<char> gameFilename = slash >= 0 ? gamePath[(slash + 1)..] : gamePath;

			Span<char> num = stackalloc char[8];
			for (int i = 0; i < 1000; ++i) {
				i.TryFormat(num, out int numWritten, "D4");
				strcpy(cachePath, CacheDirectory);
				strcat(cachePath, "/");
				strcat(cachePath, gameFilename);
				strcat(cachePath, num[..numWritten]);
				if (!g_pFileSystem.FileExists(cachePath.SliceNullTerminatedString())) {
					Cache.SetString(CacheFileKey.AsSpan().SliceNullTerminatedString(), cachePath.SliceNullTerminatedString());
					return;
				}
			}
			// all 1000 were invalid?!?
			strcpy(cachePath, CacheDirectory);
			strcat(cachePath, "/overflow");
			Cache.SetString(CacheFileKey.AsSpan().SliceNullTerminatedString(), cachePath.SliceNullTerminatedString());
		}
	}

	void SaveCacheToDisk() {
		try {
			Cache?.WriteToFile(g_pFileSystem, CacheFilename, "DEFAULT_WRITE_PATH");
		}
		catch { }
	}

	static bool DecompressBZipToDisk(string outFilename, ReadOnlySpan<char> srcFilenameSpan, byte[]? data, int bytesTotal) {
		string srcFilename = new(srcFilenameSpan.SliceNullTerminatedString());

		if (FileExistsOnDisk(outFilename) || data == null || bytesTotal < 1)
			return false;

		try {
			CreatePath(outFilename);

			if (!FileExistsOnDisk(srcFilename)) 
				File.WriteAllBytes(srcFilename, data.AsSpan(0, bytesTotal).ToArray());
			
			// Is this the map file? Map files are allowed to exceed MAX_FILE_SIZE.
			Span<char> outBase = stackalloc char[MAX_PATH];
			StrTools.FileBase(outFilename, outBase);
			bool bMapFile = false;
			ReadOnlySpan<char> mapName = cl.LevelBaseName;
			if (!mapName.IsEmpty)
				bMapFile = stricmp(outBase.SliceNullTerminatedString(), mapName) == 0;

			const int OutBufSize = 65536;
			byte[] buf = new byte[OutBufSize];
			long totalBytes = 0;

			using (FileStream inStream = File.OpenRead(srcFilename))
			using (BZip2InputStream bz = new(inStream))
			using (FileStream outStream = File.Create(outFilename)) {
				while (true) {
					int bytesRead = bz.Read(buf, 0, OutBufSize);
					if (bytesRead <= 0)
						break;

					outStream.Write(buf, 0, bytesRead);
					totalBytes += bytesRead;
					if (!bMapFile && totalBytes > Protocol.MAX_FILE_SIZE) {
						ConDColorMsg(DownloadErrorColor, $"DecompressBZipToDisk: '{srcFilename}' too big (max {Protocol.MAX_FILE_SIZE} bytes).\n");
						throw new IOException("Decompressed file too large");
					}
				}
			}

			DeleteFromDisk(srcFilename);
			return true;
		}
		catch {
			DeleteFromDisk(srcFilename);
			DeleteFromDisk(outFilename);
			return false;
		}
	}

	static bool FileExistsOnDisk(string path) {
		try { return File.Exists(path); }
		catch { return false; }
	}

	static void CreatePath(string path) {
		try {
			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
		}
		catch { }
	}

	static void DeleteFromDisk(string path) {
		try { if (File.Exists(path)) File.Delete(path); }
		catch { }
	}

	public static DownloadCache? TheDownloadCache = null;
}

public class DownloadManager
{
	static readonly HttpClient http = CreateHttpClient();
	static HttpClient CreateHttpClient() {
		HttpClientHandler handler = new() {
			AllowAutoRedirect = true,
			MaxAutomaticRedirections = 1,
			ServerCertificateCustomValidationCallback = static (_, _, _, _) => true,
		};
		HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(30) };
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Half-Life 2");
		return client;
	}

	readonly List<RequestContext> m_queuedRequests = [];    
	RequestContext? m_activeRequest;                        
	readonly List<RequestContext> m_completedRequests = []; 

	int m_lastPercent;    
	int m_totalRequests;  

	readonly List<string> m_downloadedMaps = []; 

	public int GetQueueSize() => m_queuedRequests.Count;
	public void Stop() => Reset();

	bool ShouldAttemptCompressedFileDownload() => true;

	void SetupURLPath(RequestContext rc, ReadOnlySpan<char> urlPath) {
		strcpy(rc.URLPath, ((ReadOnlySpan<char>)rc.GamePath).SliceNullTerminatedString());
	}

	void SetupServerURL(RequestContext rc) {
		ReadOnlySpan<char> address = "";
		if (cl.NetChannel is NetChannel nc && nc.GetRemoteAddress() is NetAddress adr)
			address = adr.ToString(false);
		strcpy(rc.ServerURL, address);
	}
	
	void OnHttpConnecting(RequestContext rc) { }
	void OnHttpFetch(RequestContext rc) { }
	void OnHttpDone(RequestContext rc) { }
	void OnHttpError(RequestContext rc) { }

	public bool HasMapBeenDownloadedFromServer(ReadOnlySpan<char> serverMapName) {
		if (serverMapName.IsStringEmpty)
			return false;

		foreach (string oldServerMapName in m_downloadedMaps) {
			if (oldServerMapName != null && stricmp(serverMapName, oldServerMapName) == 0)
				return true;
		}
		return false;
	}

	public void MarkMapAsDownloadedFromServer(ReadOnlySpan<char> serverMapName) {
		if (serverMapName.IsStringEmpty)
			return;

		if (HasMapBeenDownloadedFromServer(serverMapName))
			return;

		m_downloadedMaps.Add(new string(serverMapName.SliceNullTerminatedString()));
	}

	public bool FileDenied(uint requestID) {
		if (m_activeRequest == null)
			return false;
		if (m_activeRequest.RequestID != requestID)
			return false;
		if (m_activeRequest.AsHTTP)
			return false;

		if (DownloadCache.download_debug.GetBool())
			ConDColorMsg(DownloadCache.DownloadErrorColor, $"Error downloading {((ReadOnlySpan<char>)m_activeRequest.AbsLocalPath).SliceNullTerminatedString()}\n");

		UpdateProgressBar();

		// try to download the next file
		m_completedRequests.Add(m_activeRequest);
		m_activeRequest = null;

		return true;
	}

	public bool FileReceived(ReadOnlySpan<char> filename, uint requestID) {
		if (m_activeRequest == null)
			return false;
		if (m_activeRequest.RequestID != requestID)
			return false;
		if (m_activeRequest.AsHTTP)
			return false;

		if (DownloadCache.download_debug.GetBool())
			ConDColorMsg(DownloadCache.DownloadCompleteColor, "Download finished!\n");

		UpdateProgressBar();

		m_completedRequests.Add(m_activeRequest);
		m_activeRequest = null;

		return true;
	}

	void QueueInternal(ReadOnlySpan<char> pBaseURL, ReadOnlySpan<char> pURLPath, ReadOnlySpan<char> pGamePath, bool bAsHttp, bool bCompressed, int downloadableIndex) {
		// NOTE: Assumes valid game path (i.e. CL.IsGamePathValidAndSafeForDownload() has been called already)

		++m_totalRequests;

		if (DownloadCache.TheDownloadCache == null) {
			DownloadCache.TheDownloadCache = new DownloadCache();
			DownloadCache.TheDownloadCache.Init();
		}

		RequestContext rc = new();
		m_queuedRequests.Add(rc);

		rc.IsBZ2 = bCompressed;
		rc.AsHTTP = bAsHttp;
		rc.DownloadableIndex = downloadableIndex;
		rc.Status = HTTPStatus.Connecting;

		Span<char> szBasePath = stackalloc char[MAX_PATH];
		strcpy(szBasePath, Source.Engine.Common.Gamedir);
		StrTools.AppendSlash(szBasePath);
		strcat(szBasePath, DownloadCache.DownloadPathID);

		strcpy(rc.GamePath, pGamePath);
		if (bCompressed)
			strcat(rc.GamePath, ".bz2");
		StrTools.FixSlashes(rc.GamePath); 

		Span<char> szGamePathLower = stackalloc char[MAX_PATH];
		strcpy(szGamePathLower, ((ReadOnlySpan<char>)rc.GamePath).SliceNullTerminatedString());
		StrTools.StrLower(szGamePathLower);

		strcpy(rc.AbsLocalPath, szBasePath.SliceNullTerminatedString());
		StrTools.AppendSlash(rc.AbsLocalPath);
		strcat(rc.AbsLocalPath, szGamePathLower.SliceNullTerminatedString());
		StrTools.FixSlashes(rc.AbsLocalPath);

		if (bAsHttp) {
			strcpy(rc.BaseURL, pBaseURL);
			StripTrailingSlash(rc.BaseURL);
			strcat(rc.BaseURL, "/");
		}

		SetupURLPath(rc, pURLPath);
		SetupServerURL(rc);

		if (DownloadCache.download_debug.GetBool())
			ConDColorMsg(DownloadCache.DownloadColor, $"Queueing {((ReadOnlySpan<char>)rc.BaseURL).SliceNullTerminatedString()}{pGamePath}.\n");

		if (bAsHttp)
			OnHttpConnecting(rc);
	}


	public void Queue(ReadOnlySpan<char> baseURL, ReadOnlySpan<char> urlPath, ReadOnlySpan<char> gamePath, int downloadableIndex) {
		if (!CL.IsGamePathValidAndSafeForDownload(gamePath))
			return;

		// Don't download existing files
		if (g_pFileSystem.FileExists(gamePath))
			return;

#if !DEBUG
		if (sv.IsActive())
			return;
#endif

		bool bAsHTTP = !baseURL.IsStringEmpty && (strnicmp(baseURL, "http://", 7) == 0 || strnicmp(baseURL, "https://", 8) == 0);

		if (bAsHTTP && ShouldAttemptCompressedFileDownload() && !g_pFileSystem.FileExists($"{gamePath}.bz2"))
			QueueInternal(baseURL, urlPath, gamePath, true, true, downloadableIndex);

		QueueInternal(baseURL, urlPath, gamePath, bAsHTTP, false, downloadableIndex);

		if (DownloadCache.download_debug.GetBool())
			ConDColorMsg(DownloadCache.DownloadColor, $"Queueing {baseURL}{gamePath}.\n");
	}

	void Reset() {
		if (m_activeRequest != null) {
			if (DownloadCache.download_debug.GetBool())
				ConDColorMsg(DownloadCache.DownloadColor, $"Aborting download of {((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}\n");

			if (m_activeRequest.BytesTotal != 0 && m_activeRequest.BytesCurrent != 0) {
				DownloadCache.TheDownloadCache?.PersistToCache(m_activeRequest);
			}
			m_activeRequest.ShouldStop = true;
			m_completedRequests.Add(m_activeRequest);
			m_activeRequest = null;
			//TODO: StopLoadingProgressBar();
		}

		// clear out any queued requests
		foreach (RequestContext rc in m_queuedRequests) 
			if (DownloadCache.download_debug.GetBool())
				ConDColorMsg(DownloadCache.DownloadColor, $"Discarding queued download of {((ReadOnlySpan<char>)rc.GamePath).SliceNullTerminatedString()}\n");
		
		m_queuedRequests.Clear();

		DownloadCache.TheDownloadCache = null;

		m_lastPercent = 0;
		m_totalRequests = 0;
	}

	void PruneCompletedRequests() {
		for (int i = m_completedRequests.Count - 1; i >= 0; --i) {
			if (m_completedRequests[i].ThreadDone || !m_completedRequests[i].AsHTTP) {
				m_completedRequests[i].CacheData = null;
				m_completedRequests.RemoveAt(i);
			}
		}
	}

	void CheckActiveDownload() {
		if (m_activeRequest == null)
			return;

		if (!m_activeRequest.AsHTTP) {
			UpdateProgressBar();
			return;
		}

		switch (m_activeRequest.Status) {
			case HTTPStatus.Done:
				if (DownloadCache.download_debug.GetBool())
					ConDColorMsg(DownloadCache.DownloadCompleteColor, "Download finished!\n");

				UpdateProgressBar();
				OnHttpDone(m_activeRequest);
				if (m_activeRequest.BytesTotal != 0) {
					DownloadCache.TheDownloadCache?.PersistToDisk(m_activeRequest);
					m_activeRequest.ShouldStop = true;
					m_completedRequests.Add(m_activeRequest);
					m_activeRequest = null;
					if (m_queuedRequests.Count == 0) {
						//TODO: StopLoadingProgressBar();
						//TODO: Cbuf_AddText("retry\n");
					}
				}
				break;
			case HTTPStatus.Error:
				if (DownloadCache.download_debug.GetBool())
					ConDColorMsg(DownloadCache.DownloadErrorColor, $"Error downloading {((ReadOnlySpan<char>)m_activeRequest.BaseURL).SliceNullTerminatedString()}{((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}\n");

				UpdateProgressBar();

				// try to download the next file
				m_activeRequest.ShouldStop = true;
				m_completedRequests.Add(m_activeRequest);
				OnHttpError(m_activeRequest);
				m_activeRequest = null;
				if (m_queuedRequests.Count == 0) {
					//TODO: StopLoadingProgressBar();
					//TODO: Cbuf_AddText("retry\n");
				}
				break;
			case HTTPStatus.Fetch:
				UpdateProgressBar();
				if (m_activeRequest.BytesTotal != 0) {
					int percent = (int)(m_activeRequest.BytesCurrent * 100 / m_activeRequest.BytesTotal);
					if (percent != m_lastPercent) {
						if (DownloadCache.download_debug.GetBool())
							ConDColorMsg(DownloadCache.DownloadColor, $"Downloading {((ReadOnlySpan<char>)m_activeRequest.BaseURL).SliceNullTerminatedString()}{((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}: {percent}%% - {m_activeRequest.BytesCurrent} of {m_activeRequest.BytesTotal} bytes\n");

						m_lastPercent = percent;
						//TODO: SetSecondaryProgressBar( m_lastPercent * 0.01f );
					}
				}
				OnHttpFetch(m_activeRequest);
				break;
		}
	}

	// Starts a new download if there are queued requests
	void StartNewDownload() {
		if (m_activeRequest != null || m_queuedRequests.Count == 0)
			return;

		while (m_activeRequest == null && m_queuedRequests.Count != 0) {
			// Remove one request from the queue and make it active
			m_activeRequest = m_queuedRequests[0];
			m_queuedRequests.RemoveAt(0);

			if (g_pFileSystem.FileExists(((ReadOnlySpan<char>)m_activeRequest.AbsLocalPath).SliceNullTerminatedString())) {
				if (DownloadCache.download_debug.GetBool())
					ConDColorMsg(DownloadCache.DownloadColor, $"Skipping existing file {((ReadOnlySpan<char>)m_activeRequest.BaseURL).SliceNullTerminatedString()}{((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}.\n");

				m_activeRequest.ShouldStop = true;
				m_activeRequest.ThreadDone = true;
				m_completedRequests.Add(m_activeRequest);
				m_activeRequest = null;
			}
		}

		if (m_activeRequest == null)
			return;

		if (m_activeRequest.AsHTTP) {
			// Check cache for partial match
			DownloadCache.TheDownloadCache?.GetCachedData(m_activeRequest);

			UpdateProgressBar();

			if (DownloadCache.download_debug.GetBool())
				ConDColorMsg(DownloadCache.DownloadColor, $"Downloading {((ReadOnlySpan<char>)m_activeRequest.BaseURL).SliceNullTerminatedString()}{((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}.\n");

			m_lastPercent = 0;

			// Start the thread
			RequestContext ctx = m_activeRequest;
			Task.Run(() => DownloadThread(ctx));
		}
		else {
			UpdateProgressBar();

			if (DownloadCache.download_debug.GetBool())
				ConDColorMsg(DownloadCache.DownloadColor, $"Downloading {((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}.\n");

			m_lastPercent = 0;

			// GMod requests downloadables by their index in the downloadables string table, not by filename.
			if (m_activeRequest.DownloadableIndex >= 0)
				m_activeRequest.RequestID = cl.NetChannel?.RequestFile(RequestFile.Downloadable, (uint)m_activeRequest.DownloadableIndex) ?? 0;
			else if (DownloadCache.download_debug.GetBool())
				ConDColorMsg(DownloadCache.DownloadErrorColor, $"Cannot NetChan-download '{((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}': no downloadables index.\n");
		}
	}

	void UpdateProgressBar() {
		if (m_activeRequest == null)
			return;

		float progress;

		if (m_activeRequest.AsHTTP) {
			int overallPercent = (m_totalRequests - m_queuedRequests.Count - 1) * 100 / m_totalRequests;
			int filePercent = 0;
			if (m_activeRequest.BytesTotal > 0)
				filePercent = (int)(m_activeRequest.BytesCurrent * 100 / m_activeRequest.BytesTotal);

			progress = (overallPercent + filePercent * 1.0f / m_totalRequests) * 0.01f;
		}
		else {
			if (cl.NetChannel == null)
				return;
			cl.NetChannel.GetStreamProgress(NetFlow.FLOW_INCOMING, out int received, out int total);
			progress = total != 0 ? (float)received / total : 0.0f;
		}

		if (__EngineVGui != null)
			EngineVGui().UpdateCustomProgressBar(progress, $"Downloading {((ReadOnlySpan<char>)m_activeRequest.GamePath).SliceNullTerminatedString()}");
	}

	// Monitors download thread, starts new downloads, and updates progress bar
	public bool Update() {
		PruneCompletedRequests();
		CheckActiveDownload();
		StartNewDownload();

		return m_activeRequest != null;
	}

	static void DownloadThread(RequestContext rc) {
		string fullURL = new string(((ReadOnlySpan<char>)rc.BaseURL).SliceNullTerminatedString())
			+ new string(((ReadOnlySpan<char>)rc.URLPath).SliceNullTerminatedString());

		if (!fullURL.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
			!fullURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
			CleanUpDownload(rc, HTTPStatus.Error, HttpRequestError.InvalidProtocol);
			return;
		}

		HttpResponseMessage? resp = null;
		try {
			using HttpRequestMessage req = new(HttpMethod.Get, fullURL);

			ReadOnlySpan<char> serverURL = ((ReadOnlySpan<char>)rc.ServerURL).SliceNullTerminatedString();
			ReadOnlySpan<char> cachedTimestamp = ((ReadOnlySpan<char>)rc.CachedTimestamp).SliceNullTerminatedString();

			if (!cachedTimestamp.IsEmpty && rc.BytesCached != 0) {
				req.Headers.TryAddWithoutValidation("If-Range", new string(cachedTimestamp));
				req.Headers.Range = new RangeHeaderValue(rc.BytesCached, null);
			}
			if (!serverURL.IsEmpty)
				req.Headers.TryAddWithoutValidation("Referer", $"hl2://{new string(serverURL)}");

			resp = http.Send(req, HttpCompletionOption.ResponseHeadersRead);
		}
		catch {
			CleanUpDownload(rc, HTTPStatus.Error, HttpRequestError.CantConnect);
			return;
		}

		using (resp) {
			int code = (int)resp.StatusCode;

			// Only status codes we're looking for are 200 (OK) and 206 (Partial Content).
			if (code != 200 && code != 206) {
				CleanUpDownload(rc, HTTPStatus.Error, HttpRequestError.FileNonexistent);
				return;
			}

			// Get the timestamp, and save it off for future resumes, in case we abort this transfer later.
			if (resp.Content.Headers.LastModified is DateTimeOffset lastModified)
				strcpy(rc.CachedTimestamp, lastModified.ToString("R"));
			else
				rc.CachedTimestamp[0] = '\0';

			// If we're not getting a partial download, don't use any cached data, even if we have some.
			if (code != 206)
				rc.BytesCached = 0;

			// Check the resource size, and allocate a buffer
			if (resp.Content.Headers.ContentLength is not long contentLength) {
				CleanUpDownload(rc, HTTPStatus.Error, HttpRequestError.ZeroLengthFile);
				return;
			}

			rc.BytesTotal = (uint)contentLength + rc.BytesCached;
			if (contentLength > 0)
				rc.Data = new byte[rc.BytesTotal];

			// copy cached data into buffer
			if (rc.CacheData != null && rc.BytesCached != 0 && rc.Data != null) {
				int len = (int)Math.Min(rc.BytesCached, rc.BytesTotal);
				Array.Copy(rc.CacheData, 0, rc.Data, 0, len);
			}

			if (rc.ShouldStop) {
				CleanUpDownload(rc, HTTPStatus.Aborted);
				return;
			}

			// now download the actual data
			try {
				using Stream stream = resp.Content.ReadAsStream();
				ReadData(rc, stream);
			}
			catch {
				rc.Status = HTTPStatus.Error;
				rc.Error = HttpRequestError.ConnectionClosed;
			}
		}

		// and stick around until the main thread has gotten the data.
		CleanUpDownload(rc, rc.Status, rc.Error);
	}

	static void ReadData(RequestContext rc, Stream stream) {
		const int BufferSize = 2048;
		byte[] data = new byte[BufferSize];

		if (rc.BytesTotal == 0) {
			rc.Status = HTTPStatus.Error;
			rc.Error = HttpRequestError.ZeroLengthFile;
			return;
		}
		rc.BytesCurrent = rc.BytesCached;
		rc.Status = HTTPStatus.Fetch;

		while (!rc.ShouldStop) {
			int dwSize = stream.Read(data, 0, BufferSize);
			if (dwSize == 0) {
				if (rc.BytesCurrent != rc.BytesTotal) {
					rc.Status = HTTPStatus.Error;
					rc.Error = HttpRequestError.ZeroLengthFile;
					Warning($"FastDL: Received incomplete data! Got {rc.BytesCurrent} of {rc.BytesTotal} bytes\n");
					return;
				}

				// We're at the end of the file. If the file doesn't exist, write it out.
				WriteFileFromRequestContext(rc);

				// Let the main thread know we finished reading data, and wait for it to let us exit.
				rc.Status = HTTPStatus.Done;
				return;
			}
			else {
				// We've read some data. Make sure we won't walk off the end of our buffer, then save the data off.
				uint safeSize = Math.Min(rc.BytesTotal - rc.BytesCurrent, (uint)dwSize);
				if (rc.Data != null && safeSize > 0)
					Array.Copy(data, 0, rc.Data, rc.BytesCurrent, safeSize);
				rc.BytesCurrent += safeSize;
			}
		}

		// If we get here, rc.ShouldStop was set early (user hit cancel).
		rc.Status = HTTPStatus.Aborted;
	}

	static void WriteFileFromRequestContext(RequestContext rc) {
		string absLocalPath = new(((ReadOnlySpan<char>)rc.AbsLocalPath).SliceNullTerminatedString());
		if (rc.SuppressFileWrite)
			return;

		try {
			if (File.Exists(absLocalPath))
				return;

			string? dir = Path.GetDirectoryName(absLocalPath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			using FileStream fp = File.Create(absLocalPath);
			if (rc.Data != null)
				fp.Write(rc.Data, 0, (int)rc.BytesTotal);
		}
		catch { }
	}

	static void CleanUpDownload(RequestContext rc, HTTPStatus status, HttpRequestError error = HttpRequestError.None) {
		rc.Status = status;
		rc.Error = error;

		// wait until the main thread says we can go away (so it can look at rc.Data).
		while (!rc.ShouldStop)
			Thread.Sleep(100);

		// Release rc.Data, which was allocated in this thread
		rc.Data = null;

		// and tell the main thread we're exiting, so it can release rc.CacheData and the RequestContext itself.
		rc.ThreadDone = true;
	}

	static void StripTrailingSlash(Span<char> str) {
		int len = (int)strlen(str);
		if (len > 0 && (str[len - 1] == '/' || str[len - 1] == '\\'))
			str[len - 1] = '\0';
	}
}

public partial class CL
{
	static readonly DownloadManager s_DownloadManager = new();
	internal static bool DownloadUpdate() => s_DownloadManager.Update();
	internal static void HTTPStop_f() => s_DownloadManager.Stop();
	internal static bool FileReceived(ReadOnlySpan<char> filename, uint requestID) => s_DownloadManager.FileReceived(filename, requestID);
	internal static bool FileDenied(uint requestID) => s_DownloadManager.FileDenied(requestID);
	internal static void QueueDownload(ReadOnlySpan<char> filename, int downloadableIndex) => s_DownloadManager.Queue(BaseServer.sv_downloadurl.GetString(), null, filename, downloadableIndex);
	internal static int GetDownloadQueueSize() => s_DownloadManager.GetQueueSize();

	internal static int CanUseHTTPDownload() {
		ReadOnlySpan<char> downloadURL = BaseServer.sv_downloadurl.GetString();
		if (!downloadURL.IsStringEmpty) {
			string serverMapName = $"{downloadURL}:{cl.LevelFileName}";
			return s_DownloadManager.HasMapBeenDownloadedFromServer(serverMapName) ? 0 : 1;
		}
		return 0;
	}

	internal static void MarkMapAsUsingHTTPDownloaD() {
		string serverMapName = $"{BaseServer.sv_downloadurl.GetString()}:{cl.LevelFileName}";
		s_DownloadManager.MarkMapAsDownloadedFromServer(serverMapName);
	}

	internal static bool IsGamePathValidAndSafeForDownload(ReadOnlySpan<char> pGamePath) {
		return (cl.NetChannel as NetChannel)?.IsValidFileForTransfer(pGamePath) ?? false;
	}
}
