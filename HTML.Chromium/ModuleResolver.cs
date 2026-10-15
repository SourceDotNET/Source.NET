using System.Runtime.Loader;

namespace HTML.Chromium;

static class ModuleResolver
{
	public static bool Install() {
		string moduleDir = Path.GetDirectoryName(typeof(ModuleResolver).Assembly.Location)!;
		AssemblyLoadContext.Default.Resolving += (context, name) => {
			string path = Path.Combine(moduleDir, $"{name.Name}.dll");
			return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
		};
		return true;
	}
}
