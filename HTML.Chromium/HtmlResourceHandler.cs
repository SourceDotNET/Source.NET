using CefSharp;
using CefSharp.Callback;

using System.Collections.Specialized;

namespace HTML.Chromium;

public sealed class HtmlResourceHandlerFactory : ISchemeHandlerFactory
{
	public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request) => new HtmlResourceHandler();
}

public sealed class HtmlResourceHandler : IResourceHandler
{
	byte[] Html = [];
	int Current;

	public bool Open(IRequest request, out bool handleRequest, ICallback callback) {
		handleRequest = true;
		Current = 0;

		if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? url))
			return false;

		string encodedHtml = url.Query.StartsWith('?') ? url.Query[1..] : url.Query;
		byte[] data = new byte[encodedHtml.Length];
		if (Convert.TryFromBase64String(encodedHtml, data, out int written))
			Html = data[..written];

		return true;
	}

	public bool ProcessRequest(IRequest request, ICallback callback) => false;

	public void GetResponseHeaders(IResponse response, out long responseLength, out string redirectUrl) {
		redirectUrl = null!;

		response.StatusCode = 200;
		response.Headers = new NameValueCollection { { "X-XSS-Protection", "0" } };
		response.MimeType = "text/html";
		responseLength = Html.Length;
	}

	public bool Skip(long bytesToSkip, out long bytesSkipped, IResourceSkipCallback callback) {
		bytesSkipped = -2;
		return false;
	}

	public bool Read(Stream dataOut, out int bytesRead, IResourceReadCallback callback) {
		bytesRead = 0;
		if (Html.Length <= 0)
			return false;

		int bytesToRead = (int)Math.Min(dataOut.Length, Html.Length - Current);
		bytesToRead = Math.Max(bytesToRead, 0);

		dataOut.Write(Html, Current, bytesToRead);
		bytesRead = bytesToRead;
		Current += bytesToRead;

		return bytesToRead > 0;
	}

	public bool ReadResponse(Stream dataOut, out int bytesRead, ICallback callback) {
		bytesRead = 0;
		return false;
	}

	public void Cancel() { }

	public void Dispose() { }
}
