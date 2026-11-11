# Source.NET
### [Join our Discord](https://discord.gg/rFzd5uEDSE)
###### Table of Contents
> <sub>[What is this?](#what-is-this)</sub><br>
> <sub>[Credits & Thanks](#credits--thanks)</sub><br>
> <sub>[Structure](#structure)</sub><br>
>> <sub>[Stage 0: External Dependencies + Global Usings](#stage-0-external-dependencies--global-usings)</sub><br>
>> <sub>[Stage 1: Engine Common + Tier Libraries](#stage-1-engine-common--tier-libraries)</sub><br>
>> <sub>[Stage 2: Pre-Engine Components](#stage-2-pre-engine-components)</sub><br>
>> <sub>[Stage 3: Engine Main](#stage-3-engine-main)</sub><br>
>> <sub>[Stage 4: Post-Engine Components](#stage-4-post-engine-components)</sub><br>
>> <sub>[Stage 5: Game Realms](#stage-5-game-realms)</sub><br>
>> <sub>[Stage 6: Executables](#stage-6-executables)</sub><br>

> <sub>[API Notes](#api-notes)</sub><br>

## What is this?
This is an open source clone of the Source Engine, based on [RaphaelIT7's Source Engine branch](https://github.com/RaphaelIT7/obsolete-source-engine), written in C#. It aims to be as compatible as possible with Source Engine's formats and protocols, more specifically targetting [Garry's Mod](https://store.steampowered.com/app/4000/Garrys_Mod/) compatibility. 

<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/b382c7f6-f3ca-43d5-9ce6-fdfc347a5aa2" />
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/d456bd33-b96a-4847-b97c-0a2cb16fbe0a" />
<img width="1600" height="900" alt="image" src="https://github.com/user-attachments/assets/903488b0-0c1d-4485-940a-d9acc3c2b929" />

It can currently create singleplayer games (WIP,) connect to real Garry's Mod servers, and load some basic Garry's Mod Lua code on both realms.

## Credits & Thanks
- Valve for obvious reasons
- [RaphaelIT7](https://github.com/RaphaelIT7) for various contributions & unintentionally getting me down the rabbit-hole of Source Engine tomfoolery in the first place
- [maddnias](https://github.com/maddnias) for his work on SharpVPK, and [antim0118](https://github.com/antim0118) for his changes which fixed some incompatibilities
- [Leystryku](https://github.com/Leystryku) for his work on [leysourceengineclient](https://github.com/Leystryku/leysourceengineclient) - which has served as a very good baseline for networking implementations when I was struggling

## Building
You need the following:
- A C#-compatible IDE obviously (Visual Studio 2026 is what I use for now, although I'm sure it would work fine on Rider or VS Code)
- The .NET 11.0 SDK
- A license for Garry's Mod on Steam

## Structure
The engine is very similar to Source, with various deviations where I saw fit, to better match .NET/C# implementation details. Things like UtlVector/UtlMap/UtlLinkedList are replaced with their C# equivalents. Each "stage" described here builds on each other incrementally - ie. stage 3 can include stage 2 and stage 1 libraries, stage 2 can include stage 1 libraries, but stage 1 cannot include stage 2 libraries, etc. Libraries can include other libraries within their own stage if needed - but is done only in a few cases (VTF including Common and Bitmap, for example). This was done at the start to get a general idea of the Source Engine structure.

There are currently seven stages. The Solution is organized via Solution Folders as well in this order.

> [!NOTE]  
> AppFramework in Source is instead replaced by [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/microsoft.extensions.dependencyinjection/).

> > [!NOTE]  
> A lot of this is outdated (or missing new libraries that now exist), however the majority of this information and the general structure is still correct.

---

### Stage 0: External Dependencies + Global Usings
Various external dependencies & components, along with various non-project-specific C# files, which allow defining global using statements for a project. For example, almost every library uses this in its .csproj file to add the various type definitions/global static methods/constants:
```xml
<ItemGroup>
	<Compile Include="..\Usings\GlobalUsings.cs" Link="Usings/GlobalUsings.cs" />
</ItemGroup>
```

---

### Stage 1: Engine Common + Tier Libraries
The Source SDK provides a "public" folder which contains the vast majority of engine interfaces, enumerations, etc. These are statically linked at compile time. There are also dynamic libraries (tier0) and static libraries (tier1, tier2) which various components use shared functionality from. In Source.NET, these are all merged into one single common dynamic library, since C# doesn't support static linking.

> [!NOTE]  
> A future goal is to further separate the files in Common.

> [!NOTE]  
> This stage also includes a couple other libraries, such as bitmap/common - which also are static libraries in C++, but in our case, are dynamic libraries.

#### Libraries
- ##### Bitmap
	- Bitmap processing
	- Image format utilities
- ##### Common
	- Public interfaces and enumerations
	- Various helper components
	- Tier0 functionality - Dbg, Spew, Asserts, etc
	- Tier1 functionality - ConVars, implementation of ICVar happens here instead of a vstdlib equiv
- ##### VTF
	- VTF texture parsing
	- Implementation of IVTFTexture
- ##### VPK
	- A fork of [SharpVPK](https://github.com/maddnias/SharpVPK) with changes from [antim0118's branch](https://github.com/antim0118/SharpVPK/)
	- Parses VPK files, only really used by the filesystem implementation

---

### Stage 2: Pre-Engine Components
These are components of the engine which the engine depends upon directly. This is a rare case as most things are implemented in the post-engine stage, but things like VGUI and VGUI controls have to be in this stage.

#### Libraries
- ##### GUI
	- Defines Panel : IPanel, which is the root of all elements
	- Implementation of IVGui
	- Implementation of IVGuiInput
	- Implementation of ISchemeManager
	- Implementation of ILocalize
- ##### GUI.Controls
	- Defines the various GUI controls
	- Also defines AnimationControllers, BuildGroups, NavGroups, etc.

---

### Stage 3: Engine Main
This is only the engine core. Everything else beyond this point are extensions which are included by the launcher/dedicated projects.

#### Libraries
- ##### Engine
	- Core engine logic
	- Cbuf, Cmd, Key, ModelLoader, Render, Scr, Sound, Sys, View, etc.
	- Implementation of ICommandLine
	- Datatable encoding definitions
	- Implementation of IEngineAPI and EngineBuilder to build one

---

### Stage 4: Post-Engine Components
These are the implementations of systems that the engine does not require as a direct dependency.

#### Libraries
- ##### AudioSystem
	- Reimplementation of the Source audio system as a separate interface
- ##### DataCache
	- Model loading code (IMDLCache etc)
- ##### FileSystem
	- Implements IFileSystem
	- Handles the various types of search paths
		- Folder/disk based
		- .vpk v1/v2 files
		- .bsp pakfile lumps
- ##### MaterialSystem
	- Implements IMaterialSystem & IMatRenderContext
	- Implements IShaderAPI
	- Implements IShaderDevice
	- Implements IShaderUtil
	- Implements ITextureManager
	- Implements IShaderSystem
	- Implements ISurface/IMatSystemSurface
- ##### InputSystem
	- Implements IInputSystem around SDL 3
	- Produces input events from the game window (wrapped around an IWindow interface, which ILauncherMgr produces)
- ##### LauncherManager
	- Implements ILauncherMgr around SDL 3
	- Produces the game window
- ##### Physics
	- Currently a wrapper around Jitter physics, but moving to Box3D very soon
- ##### InputSystem
- ##### System
	- Implements ISystem around SDL 3
- ##### StdShader.Gl46
	- Defines BaseShader, BaseVSShader
	- Implements IShaderDLL (StdShaderGl46)
	- The core logic for various shaders (currently only UnlitGeneric for now)
	- Currently the only available shader API, but we likely will move to Vulkan at a later point
- ##### SoundEmitterSystem
- ##### StudioRender

---

### Stage 5: Game Realms
This is the actual game code. The closest thing to a static library in this entire repository is the Shared project. It is explicitly set to not compile as a library, and is instead included as a folder in both the Client and Server libraries. The client DLL defines CLIENT_DLL and the server DLL defines GAME_DLL. This allows the shared code to use shared logic with deviations in client/server, pretty much exactly how Source does it.

#### Libraries
- ##### Game.Client
	- Implements IBaseClientDLL
	- Core client logic
- ##### Game.Server
	- Implements IServerGameDLL
	- Core server logic
- ##### Game.Shared
	- Directly compiled as code in Client and Server
	- CLIENT_DLL and GAME_DLL constants can be used to change how Shared is built
- ##### Game.UI
	- Implements IGameUI
	- Main menu logic

---

### Stage 6: Executables
The Launcher/Dedicated executables. For now, no dedicated project is being worked on - but if it ever is, it would be made here.

#### Executables
- ##### Launcher
	- Defines the IBaseClientDLL, IServerGameDLL, IGameUI implementations from stage 5
	- Builds the engine service collection

---

## API Notes
> [!IMPORTANT]
> This was just a rough list of things I wanted to write down. I probably will document things better later. This just goes into some details about the app structure

### Adding Components
There are three ways to do this:

#### The Common Way: Engine Builders\<T>
> [!IMPORTANT]
> Dedicated does not exist yet, but it would fundamentally do the same thing.

In Launcher/Dedicated projects, the engine is built up with an EngineBuilder instance, with individual calls to WithComponent to specify which components the game project needs. 
#### WithAssembly(string assemblyName)
This pre-loads an assembly by name. This is only needed for projects with no references in-code when the engine is built - because otherwise, ConVar resolving, SourceDllMain resolving, etc. will miss those assemblies entirely.
#### WithComponent\<T> or WithComponent\<Interface, Implementation>
This allows you to specify a component by its explicit type or implementation of an interface. For example; this is how IFileSystem is defined in Launcher:
```cs
.WithComponent<IFileSystem, BaseFileSystem>()
```
... and here's how SDL LauncherManager is defined:
```cs
.WithComponent<SDL3_LauncherManager>()
.WithResolvedComponent<ILauncherManager, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
.WithResolvedComponent<IGraphicsProvider, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
```
> [!IMPORTANT]
> WithComponent<> and its various extensions defined later search for a static "DLLInit" method in the concrete type. If that method is found, it is cast to a delegate void(IServiceCollection) and executed on the spot. This allows components to define their own individual includes/usings when included as a dependency. An example can be found in HLClient, where it uses DLLInit to include the input system and a few other things.

#### WithResolvedComponent\<Interface, Implementation>
WithResolvedComponent allows resolving an interface type to a concrete type. It is only used in some cases (like the above). 

#### WithGameDLL\<T> where T : IServerGameDLL
#### WithClientDLL\<T> where T : IBaseClientDLL
#### WithGameUIDLL\<T> where T : IGameUI
#### WithStdShader\<T> where T : IShaderDLL
These are all used to include their specific implementations.

---

#### Public Static Class Named "SourceDllMain"
All assemblies are searched for a static class named "SourceDllMain". If this class is found, a method named "Link" is searched for, and cast to a  delegate void (IServiceCollection). This is searched for and ran after EngineBuilder.Build() inserts its own engine components. This can be helpful if an assembly has its own specific components it wishes to include.
> [!NOTE]
> A few components also use this as a way to perform some static actions. SDLManager for example uses this to define assert dialog logic.

#### Public Class with [EngineComponent] Attribute
All assemblies are searched for classes with the EngineComponentAttribute. All classes with this attribute are added as concrete singletons into the service collection after EngineBuilder.Build() inserts its own engine components.
