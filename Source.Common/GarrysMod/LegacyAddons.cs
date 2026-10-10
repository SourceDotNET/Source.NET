namespace Source.Common.GarrysMod;

public static class ILegacyAddons
{
	public struct Information
	{
		public string Name;
		public string Path;
		public string LuaPath;
		public string GamemodesPath;
	}
}

public static class LegacyAddons
{
	public interface System
	{
		void Refresh();
		List<ILegacyAddons.Information> GetList();
	}
}
