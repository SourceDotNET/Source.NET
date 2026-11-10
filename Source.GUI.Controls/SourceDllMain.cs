global using static Source.GUI.Controls.SourceDllMain;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

namespace Source.GUI.Controls;

[EngineComponent]
public static class SourceDllMain
{
#if SWDS
	const bool ClientRequired = false;
#else
	const bool ClientRequired = true;
#endif

	[Dependency(Required = ClientRequired)] public static ISurface Surface { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static ISchemeManager SchemeManager { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IVGui VGui { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IInputSystem input { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IVGuiInput Input { get; private set; } = null!;
	[Dependency] public static IEngineAPI EngineAPI { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static ILocalize Localize { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static ILauncherManager Launcher { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static ISystem system { get; private set; } = null!;
	[Dependency] public static IMaterialSystem Materials { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;
	[Dependency] public static IFileSystem fileSystem { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IEngineClient engine { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IGameUIFuncs gameuifuncs { get; private set; } = null!;
	[Dependency(Required = ClientRequired)] public static IMatSystemSurface matSys { get; private set; } = null!;

	public static void Link(IServiceCollection services) {
		services.AddSingleton<AnimationController>();
		services.AddSingleton<IAnimationController>(x => x.GetRequiredService<AnimationController>());
	}
}
