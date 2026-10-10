
using Source.Common.Filesystem;

namespace Source.Engine;

public static class SteamInfVersionInfo
{
	static SteamAppInfo info;
	static bool initialized;
	public static ref readonly SteamAppInfo GetSteamInfIDVersionInfo() {
		if (!initialized) {
			info = SteamAppInfo.GetSteamInf(Singleton<IFileSystem>());
			initialized = true;
		}
		return ref info;
	}

	public static int build_number() => GetSteamInfIDVersionInfo().ServerVersion;
}

public struct SteamAppInfo
{
	public string PatchVersion; // version string
	public string ProductName;  // product string
	public int AppID;
	public int ServerAppID;
	public int ServerVersion;
	public int ClientVersion;

	public static SteamAppInfo GetSteamInf(IFileSystem fileSystem) {
		using var handle = fileSystem.Open("steam.inf", FileOpenOptions.Read, null);
		using StreamReader reader = new(handle!.Stream);
		SteamAppInfo inf = new SteamAppInfo();
		while (!reader.EndOfStream) {
			var line = reader.ReadLine();
			if (line == null) continue;
			var split = line.Split('=');
			if (split.Length < 2) continue;
			switch (split[0].ToLower()) {
				case "patchversion": inf.PatchVersion = split[1]; break;
				case "productname": inf.ProductName = split[1]; break;
				case "clientversion": inf.ClientVersion = int.TryParse(split[1], out int i1) ? i1 : 0; break;
				case "serverversion": inf.ServerVersion = int.TryParse(split[1], out int i2) ? i2 : 0; break;
				case "appid": inf.AppID = int.TryParse(split[1], out int i3) ? i3 : 0; break;
			}
		}
		return inf;
	}
}
