global using static Game.MenuSystem.SourceDllMain;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.GarrysMod;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Engine;

namespace Game.MenuSystem;

public static class SourceDllMain
{
	public static GlobalVarsBase gpGlobals = null!;
	public static IGet get = null!;
	public static IGarrysMod garrysmod = null!;
	public static INetworkStringTableContainer? networkstringtable;

	[Dependency] public static IEngineClient engine { get; private set; } = null!;
	[Dependency] public static IEngineVGui enginevgui { get; private set; } = null!;
	[Dependency] public static IGameUIFuncs gameuifuncs { get; private set; } = null!;
	[Dependency] public static IGameUI gameUI { get; private set; } = null!;
	[Dependency] public static IMatSystemSurface surface { get; private set; } = null!;
	[Dependency] public static ISurface Surface { get; private set; } = null!;
	[Dependency] public static IVGui VGui { get; private set; } = null!;
	[Dependency] public static IVGuiInput vguiInput { get; private set; } = null!;
	[Dependency] public static IInputSystem inputsystem { get; private set; } = null!;
	[Dependency] public static ISchemeManager SchemeManager { get; private set; } = null!;
	[Dependency] public static ISchemeManager vguiSchemeManager { get; private set; } = null!;
	[Dependency] public static ILocalize localize { get; private set; } = null!;
	[Dependency] public static ILocalize Localize { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;
	[Dependency] public static IFileSystem filesystem { get; private set; } = null!;
	[Dependency] public static IMaterialSystem materials { get; private set; } = null!;
	[Dependency] public static ICommandLine commandLine { get; private set; } = null!;
	[Dependency] public static ISystem system { get; private set; } = null!;
}
