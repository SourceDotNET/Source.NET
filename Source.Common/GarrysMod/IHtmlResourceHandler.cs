namespace Source.Common.GarrysMod;

public abstract class HtmlResource;

public interface IHtmlResourceHandler
{
	HtmlResource? OpenResource(ReadOnlySpan<char> host, ReadOnlySpan<char> path);
	void CloseResource(HtmlResource resource);

	long GetLength(HtmlResource resource);
	void ReadData(HtmlResource resource, Span<byte> destination);

	void Message(ReadOnlySpan<char> data);
}
