using CefSharp;
using CefSharp.Callback;

using Source.Common.GarrysMod;

using System.Text;

namespace HTML.Chromium;

public sealed class ResourceHandlerFactory : ISchemeHandlerFactory
{
	public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request) => new ResourceHandler();
}

public sealed class ResourceHandler : IResourceHandler
{
	public static IHtmlResourceHandler? g_pHtmlResourceHandler;

	string Host = "";
	string Path = "";
	ICallback? ResponseCallback;

	byte[]? Buffer;
	int BufferPtr;

	public void Handle() {
		HtmlResource? resource = g_pHtmlResourceHandler!.OpenResource(Host, Path);

		if (resource != null) {
			long size = g_pHtmlResourceHandler.GetLength(resource);
			Buffer = new byte[size];
			BufferPtr = 0;

			g_pHtmlResourceHandler.ReadData(resource, Buffer);
			g_pHtmlResourceHandler.CloseResource(resource);
		}

		if (ResponseCallback != null && !ResponseCallback.IsDisposed)
			ResponseCallback.Continue();
	}

	public bool Open(IRequest request, out bool handleRequest, ICallback callback) {
		handleRequest = true;

		if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? url))
			return false;

		Host = url.Host;
		Path = URIDecode(url.AbsolutePath);

		handleRequest = false;
		ResponseCallback = callback;
		ChromiumSystem.g_ChromiumSystem.QueueRequest(this);

		return true;
	}

	public bool ProcessRequest(IRequest request, ICallback callback) => false;

	static string GetFileExtension(string path) {
		int i = path.LastIndexOf('.');
		if (i == -1)
			return "";
		return path[(i + 1)..];
	}

	public void GetResponseHeaders(IResponse response, out long responseLength, out string redirectUrl) {
		redirectUrl = null!;

		if (Buffer == null) {
			responseLength = 0;
			response.ErrorCode = CefErrorCode.FileNotFound;
			return;
		}

		string mimeType = "text/html";

		string ext = GetFileExtension(Path);
		if (ext == "png" || Host == "mapimage") mimeType = "image/png";
		else if (ext == "jpg") mimeType = "image/jpg";
		else if (ext == "jpeg") mimeType = "image/jpg";
		else if (ext == "css") mimeType = "text/css";
		else if (ext == "js") mimeType = "text/javascript";
		else if (ext == "svg") mimeType = "image/svg+xml";
		else if (ext == "gif") mimeType = "image/gif";
		else if (ext == "webm") mimeType = "video/webm";
		else if (ext == "mp4") mimeType = "video/mp4";

		response.StatusCode = 200;
		response.MimeType = mimeType;
		responseLength = Buffer.Length;
	}

	public bool Skip(long bytesToSkip, out long bytesSkipped, IResourceSkipCallback callback) {
		bytesSkipped = -2;
		return false;
	}

	public bool Read(Stream dataOut, out int bytesRead, IResourceReadCallback callback) {
		int bytesToRead = (int)Math.Min(dataOut.Length, (Buffer?.Length ?? 0) - BufferPtr);
		bytesToRead = Math.Max(bytesToRead, 0);

		if (bytesToRead > 0) {
			dataOut.Write(Buffer!, BufferPtr, bytesToRead);
			BufferPtr += bytesToRead;
		}
		bytesRead = bytesToRead;

		if (bytesToRead == 0)
			Buffer = null;

		return bytesToRead > 0;
	}

	public bool ReadResponse(Stream dataOut, out int bytesRead, ICallback callback) {
		bytesRead = 0;
		return false;
	}

	public void Cancel() { }

	public void Dispose() { }

	static string URIDecode(string encoded) {
		byte[] source = Encoding.UTF8.GetBytes(encoded);
		List<byte> bytes = new(source.Length);
		for (int i = 0; i < source.Length; i++) {
			byte c = source[i];
			if (c == '%' && i + 2 < source.Length && IsHex(source[i + 1]) && IsHex(source[i + 2])) {
				int value = (HexValue(source[i + 1]) << 4) | HexValue(source[i + 2]);
				if (value >= 0x20 && value != 0x7F && value != '/' && value != '\\') {
					bytes.Add((byte)value);
					i += 2;
					continue;
				}
			}

			bytes.Add(c);
		}
		return Encoding.UTF8.GetString(bytes.ToArray());
	}

	static bool IsHex(byte c) => c is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f') or (>= (byte)'A' and <= (byte)'F');
	static int HexValue(byte c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}
