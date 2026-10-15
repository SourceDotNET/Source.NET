using Source.Common.GarrysMod;

using System.Text;

using Xilium.CefGlue;

namespace HTML.CEF;

public sealed class ResourceHandlerFactory : CefSchemeHandlerFactory
{
	protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request) => new ResourceHandler();
}

public sealed class ResourceHandler : CefResourceHandler
{
	public static IHtmlResourceHandler? g_pHtmlResourceHandler;

	string Host = "";
	string Path = "";
	CefCallback? ResponseCallback;

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

		CefCallback? callback = ResponseCallback;
		ResponseCallback = null;
		callback?.Continue();
	}

	protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback) {
		handleRequest = true;

		if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? url))
			return false;

		Host = url.Host;
		Path = URIDecode(url.AbsolutePath);

		handleRequest = false;
		ResponseCallback = callback;
		CefSystem.g_CefSystem.QueueRequest(this);

		return true;
	}

	static string GetFileExtension(string path) {
		int i = path.LastIndexOf('.');
		if (i == -1)
			return "";
		return path[(i + 1)..];
	}

	protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl) {
		redirectUrl = null!;

		if (Buffer == null) {
			responseLength = 0;
			response.Error = CefErrorCode.FileNotFound;
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

		response.Status = 200;
		response.MimeType = mimeType;
		responseLength = Buffer.Length;
	}

	protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback) {
		bytesSkipped = -2;
		return false;
	}

	protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback) {
		int count = Math.Min(bytesToRead, (Buffer?.Length ?? 0) - BufferPtr);
		count = Math.Max(count, 0);

		if (count > 0) {
			response.Write(Buffer!, BufferPtr, count);
			BufferPtr += count;
		}
		bytesRead = count;

		if (count == 0)
			Buffer = null;

		return count > 0;
	}

	protected override void Cancel() {
		ResponseCallback = null;
	}

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
