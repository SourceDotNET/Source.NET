using Source.Common.Filesystem;
using Source.Common.GarrysMod;

namespace Game.Client.GarrysMod;

public class HtmlResourceHandler : IHtmlResourceHandler
{
	public static readonly HtmlResourceHandler g_HtmlResourceHandler = new();

	sealed class Resource(IFileHandle file) : HtmlResource
	{
		public readonly IFileHandle File = file;
	}

	static IFileHandle? OpenMapImage(ReadOnlySpan<char> path) {
		if (path.IsEmpty)
			return null;

		ReadOnlySpan<char> name = path[1..];
		if (name.Contains('/'))
			return null;

		IFileHandle? file = filesystem.Open($"maps/thumb/{name}.png", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (file != null)
			return file;

		file = filesystem.Open($"maps/{name}.png", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (file != null)
			return file;

		return filesystem.Open("maps/thumb/noicon.png", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
	}

	public HtmlResource? OpenResource(ReadOnlySpan<char> host, ReadOnlySpan<char> path) {
		IFileHandle? file;

		if (stricmp(host, "garrysmod") == 0) {
			if (path.IsEmpty)
				return null;

			ReadOnlySpan<char> fileName = path[1..];

			Span<char> fullPath = stackalloc char[MAX_PATH];
			fullPath.Clear();
			ReadOnlySpan<char> full = filesystem.RelativePathToFullPath(fileName, "GAME", fullPath);

			Span<char> relativePath = stackalloc char[MAX_PATH];
			relativePath.Clear();
			bool relative = filesystem.FullPathToRelativePathEx(full, "GAME", relativePath);

			if (full.IsEmpty || !relative)
				return null;

			file = filesystem.Open(fileName, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		}
		else if (stricmp(host, "mapimage") == 0)
			file = OpenMapImage(path);
		else
			return null;

		if (file == null)
			return null;

		return new Resource(file);
	}

	public void CloseResource(HtmlResource resource) => ((Resource)resource).File.Dispose();

	public long GetLength(HtmlResource resource) => (uint)((Resource)resource).File.Stream.Length;

	public void ReadData(HtmlResource resource, Span<byte> destination) {
		Stream stream = ((Resource)resource).File.Stream;
		int total = 0;
		while (total < destination.Length) {
			int read = stream.Read(destination[total..]);
			if (read <= 0)
				break;
			total += read;
		}
	}

	public void Message(ReadOnlySpan<char> data) => Msg(data);
}
