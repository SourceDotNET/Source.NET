global using static Game.UI.SourceDllMain;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Runtime.CompilerServices;

using DependencyAttribute = Source.DependencyAttribute;

namespace Game.UI;

[EngineComponent]
public static class SourceDllMain
{
	[Dependency] public static IGameUIFuncs gameuifuncs = null!;
	[Dependency] public static IFileSystem g_pFileSystem { get; private set; } = null!;
	[Dependency] public static IEngineClient engine { get; private set; } = null!;
	[Dependency] public static IInputSystem inputSystem { get; private set; } = null!;
	[Dependency] public static ISchemeManager SchemeManager { get; private set; } = null!;
	[Dependency] public static ISurface Surface { get; private set; } = null!;
	[Dependency] public static IVGuiInput Input { get; private set; } = null!;
	[Dependency] public static ILocalize Localize { get; private set; } = null!;
	[Dependency] public static IGameUI gameUI { get; private set; } = null!;
	[Dependency] public static IMaterialSystem Materials { get; private set; } = null!;
	[Dependency] public static ISystem system { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IGameUI GameUI() => gameUI;

	public static void Link(IServiceCollection services) {
		services.AddSingleton<IGameConsole, GameConsole>();
	}
}
