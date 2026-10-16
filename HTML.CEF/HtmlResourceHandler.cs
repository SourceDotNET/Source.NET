using System.Collections.Specialized;

using Xilium.CefGlue;

namespace HTML.CEF;

public sealed class HtmlResourceHandlerFactory : CefSchemeHandlerFactory
{
	protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request) => new HtmlResourceHandler();
}

public sealed class HtmlResourceHandler : CefResourceHandler
{
	byte[] Html = [];
	int Current;

	protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback) {
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

	protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl) {
		redirectUrl = null!;

		response.Status = 200;
		response.SetHeaderMap(new NameValueCollection { { "X-XSS-Protection", "0" } });
		response.MimeType = "text/html";
		responseLength = Html.Length;
	}

	protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback) {
		bytesSkipped = -2;
		return false;
	}

	protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback) {
		bytesRead = 0;
		if (Html.Length <= 0)
			return false;

		int count = Math.Max(Math.Min(bytesToRead, Html.Length - Current), 0);

		response.Write(Html, Current, count);
		bytesRead = count;
		Current += count;

		return count > 0;
	}

	protected override void Cancel() { }
}
