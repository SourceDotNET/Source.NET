using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GameUI;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Common.SceneFileCache;
using Source.Common.Server;
using Source.Common.ToolFramework;
using Source.Engine.Client;
using Source.Engine.Server;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using CvarRegistrar = System.Action<int, System.Type, System.Type, Source.Common.Commands.ICvar, System.Func<System.Type, bool, string, object>>;

namespace Source.Engine;

/// <summary>
/// Builds a capable engine instance and provides EngineAPI to interact with it.
/// </summary>
public class EngineBuilder(ICommandLine cmdLine) : ServiceCollection
{
	public EngineBuilder MarkInterface<I, T>() where T : class, I where I : class {
		this.AddSingleton<I>(x => x.GetRequiredService<T>());
		return this;
	}

	/// <summary>
	/// Force loads an assembly.
	/// </summary>
	/// <param name="assemblyName"></param>
	/// <returns></returns>
	public EngineBuilder WithAssembly(string assemblyName) {
		if (!assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			assemblyName += ".dll";

		if (!Path.IsPathFullyQualified(assemblyName))
			assemblyName = Path.Combine(AppContext.BaseDirectory, assemblyName);

		Assembly.LoadFrom(assemblyName);

		return this;
	}
	public EngineBuilder WithComponent<I, T>() where T : class, I where I : class {
		PreInject<T>(this);
		this.AddSingleton<I, T>();
		return this;
	}

	public EngineBuilder WithResolvedComponent<I, T>(Func<IServiceProvider, T> resolver) where T : class, I where I : class {
		this.AddSingleton<I, T>(resolver);
		return this;
	}

	public EngineBuilder WithComponent<T>() where T : class {
		PreInject<T>(this);
		this.AddSingleton<T>();
		return this;
	}

	public EngineBuilder WithComponent<T>(T instance) where T : class {
		PreInject<T>(this);
		this.AddSingleton<T>(instance);
		return this;
	}

	public EngineBuilder WithKeyedComponent<I>(object key, I instance) where I : class {
		this.AddKeyedSingleton<I>(key, instance);
		return this;
	}

	HashSet<Type> injectedTypelist = [];
	void PreInject<T>(IServiceCollection services) {
		if (injectedTypelist.Add(typeof(T))) {
			Type t = typeof(T);
			var preInject = t.GetMethod("DLLInit", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<PreInject>();
			if (preInject != null)
				preInject(services);
		}
	}

	public EngineBuilder WithGameUIDLL<UIDLL>() where UIDLL : class, IGameUI {
		PreInject<UIDLL>(this);
		WithComponent<UIDLL>();
		WithResolvedComponent<IGameUI, UIDLL>(static x => x.GetRequiredService<UIDLL>());
		return this;
	}

	public EngineBuilder WithClientDLL<ClDLL>() where ClDLL : class, IBaseClientDLL {
		PreInject<ClDLL>(this);
		WithComponent<ClDLL>();
		WithResolvedComponent<IBaseClientDLL, ClDLL>(static x => x.GetRequiredService<ClDLL>());
		return this;
	}

	public EngineBuilder WithGameDLL<SvDLL>() where SvDLL : class, IServerGameDLL {
		PreInject<SvDLL>(this);
		WithComponent<SvDLL>();
		WithResolvedComponent<IServerGameDLL, SvDLL>(static x => x.GetRequiredService<SvDLL>());
		return this;
	}

	List<Type> Shaders = [];

	public EngineBuilder WithStdShader<StdShdrDLL>() where StdShdrDLL : class, IShaderDLL {
		PreInject<StdShdrDLL>(this);
		this.AddTransient<IShaderDLL, StdShdrDLL>();
		return this;
	}

	readonly List<MemberInfo> filledDependencies = [];

	/// <summary>
	/// Nulls out all automatic references the EngineBuilder previously created for the EngineAPI.
	/// </summary>
	/// <param name="members"></param>
	public static void InvalidateEngineDeps(List<MemberInfo>? members) {
		if (members == null) return;
		foreach (var member in members) {
			switch (member) {
				case FieldInfo field: field.SetValue(null, null); break;
				case PropertyInfo prop: prop.SetValue(null, null); break;
			}
		}
	}

	void PreBuildAllForms() {
		SetMainThread(); // Setup ThreadUtils
						 // We got the ICommandLine from EngineBuilder, insert it into the app system
		ConVar.Register = ConVar_Register;
		this.AddSingleton(cmdLine);
		// Internal methods. These are class instances for better restart
		// support, and I feel like every time I try this, I end up getting
		// "static creep" where I start to revert like a primate into using
		// static singletons/god classes - if we're gonna use DI we might as
		// well go all the way with it...
		this.AddSingleton<Cbuf>();
		this.AddSingleton<CL>();
		this.AddSingleton<Cmd>();
		this.AddSingleton<Common>();
		this.AddSingleton<Con>();
		this.AddSingleton<Cvar>();
		this.AddSingleton<CvarUtilities>();
		this.AddSingleton<ED>();
		this.AddSingleton<FileSystem>();
		this.AddSingleton<Key>();
		this.AddSingleton<Host>();
		this.AddSingleton<MatSysInterface>();
		this.AddSingleton<MaterialSystem_Config>();
		this.AddSingleton<Net>();
		this.AddKeyedSingleton<NetworkStringTableContainer>(Realm.Client);
		this.AddKeyedSingleton(typeof(INetworkStringTableContainer), Realm.Client, (x, _) => x.GetRequiredKeyedService<NetworkStringTableContainer>(Realm.Client));
		this.AddKeyedSingleton<NetworkStringTableContainer>(Realm.Server);
		this.AddKeyedSingleton(typeof(INetworkStringTableContainer), Realm.Server, (x, _) => x.GetRequiredKeyedService<NetworkStringTableContainer>(Realm.Server));
		this.AddSingleton<Render>();
		this.AddSingleton<RenderUtils>();
		this.AddSingleton<Scr>();
		this.AddSingleton<Shader>();
		this.AddSingleton<Sound>();
		this.AddSingleton<SV>();
		this.AddSingleton<Sys>();
		this.AddSingleton<View>();
		this.AddSingleton<IVEfx, VEfx>();
		this.AddSingleton<IServerPluginHelpers, ServerPlugin>(_ => g_pServerPluginHandler);
		// Engine components that we provide.
		this.AddSingleton<ICvar, Cvar>((services) => services.GetRequiredService<Cvar>());
		this.AddSingleton<ICvarQuery, DefaultCvarQuery>();
		this.AddSingleton<IHostState, HostState>();
		this.AddSingleton<CommonHostState>();
		this.AddSingleton<EngineParms>();
		this.AddSingleton<ClientDLL>();
		this.AddSingleton<IVideoMode, VideoMode_MaterialSystem>();
		this.AddSingleton<IRender, Render>(x => x.GetRequiredService<Render>());
		this.AddSingleton<IRegistry, Registry>();
#if GMOD_DLL
		this.AddSingleton<Source.Common.GarrysMod.IGet, GarrysMod.Get>();
		this.AddSingleton<Source.Common.GarrysMod.IResources, GarrysMod.Resources>();
#endif

		this.AddSingleton<IEngineServer, EngineServer>();
		this.AddSingleton<IVoiceServer, VoiceServer>();
		// We have to tell the dependency injection system how to resolve parent classes ourselves.
		this.AddSingleton<BaseServer>(x => x.GetRequiredService<GameServer>());
		this.AddSingleton<IEngine, GameEngine>();
		this.AddSingleton<ModelLoader>();
		this.AddSingleton<IModelLoader>(x => x.GetRequiredService<ModelLoader>());
		this.AddKeyedSingleton<EngineTraceServer>(Realm.Server);
		this.AddKeyedSingleton(typeof(IEngineTrace), Realm.Server, (x, _) => x.GetRequiredKeyedService<EngineTraceServer>(Realm.Server));
		this.AddKeyedSingleton<EngineSoundServer>(Realm.Server);
		this.AddKeyedSingleton(typeof(IEngineSound), Realm.Server, (x, _) => x.GetRequiredKeyedService<EngineSoundServer>(Realm.Server));
		this.AddSingleton<ISpatialPartition, SpatialPartitionImpl>(x => g_SpatialPartition);
		this.AddSingleton<IGame, Game>();
		this.AddSingleton<IVDebugOverlay, DebugOverlay>();
		this.AddSingleton<IGameEventManager2, GameEventManager>();
		this.AddSingleton<ISceneFileCache, SceneFileCache>();
		this.AddSingleton<ModInfo>(); // This may not be valid for a while! At least until gameinfo is readable!
									  // Client state and server state singletons
		this.AddSingleton<ClientState>();
		this.AddSingleton<BaseClientState>(x => x.GetRequiredService<ClientState>());
		this.AddSingleton<GameServer>();
		this.AddSingleton<ClientGlobalVariables>();
		this.AddSingleton<ServerGlobalVariables>();
		this.AddSingleton<ServerPlugin>();
		this.AddSingleton<IServerPluginHelpers, ServerPlugin>(x => x.GetRequiredService<ServerPlugin>());
		this.AddSingleton<IUniformRandomStream, UniformRandomStream>();
		this.AddSingleton<ISoundServices, EngineSoundServices>();
		this.AddSingleton<IGameUIFuncs, GameUIFuncs>();
		this.AddSingleton<DtCommonEng>();
		this.AddSingleton<EngineToolImpl>();
		this.AddSingleton<IEngineToolInternal>(x => x.GetRequiredService<EngineToolImpl>());
		this.AddSingleton<IEngineTool>(x => x.GetRequiredService<EngineToolImpl>());
	}

	T PostBuildAllForms<T>() where T : IEngineAPI {
		List<Type> wantsInjection = [];
		object?[]? linkInput = [this];
		List<MemberInfo> populateLater = [];
		List<MemberInfo> populateLaterKeyed = [];
		void populateLookups(Type? type) {
			if (type == null)
				return;
			foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
				if (field.GetCustomAttribute<DependencyAttribute>() != null)
					populateLater.Add(field);
				if (field.GetCustomAttribute<KeyedDependencyAttribute>() != null)
					populateLater.Add(field);
			}
			foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
				if (property.GetCustomAttribute<DependencyAttribute>() != null)
					populateLater.Add(property);
				if (property.GetCustomAttribute<KeyedDependencyAttribute>() != null)
					populateLater.Add(property);
			}
		}
		foreach (var assembly in ReflectionUtils.GetAssemblies()) {
			// This allows a type to define a class named SourceDllMain, with a static void Link(IServiceCollection),
			// which allows a loaded assembly to insert whatever it wants into the DI system before the provider is
			// fully built.
			Type? sourceDLL = assembly.GetTypes().FirstOrDefault(x => x.Name == "SourceDllMain");
			sourceDLL
				?.GetMethod("Link", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?.Invoke(null, linkInput);
			populateLookups(sourceDLL);

			// This checks for any classes with the MarkForDependencyInjection attribute.
			// They are then injected into the service collection.
			foreach (var typeKVP in assembly.GetTypesWithAttribute<EngineComponentAttribute>()) {
				populateLookups(typeKVP.Key);
				if (typeKVP.Key.IsAbstract && typeKVP.Key.IsSealed)
					continue;

				this.AddSingleton(typeKVP.Key);
			}
		}

		// Everything else should be provided by the launcher!
		ServiceProvider provider = this.BuildServiceProvider();

		// Start using this provider for the engine
		using ServiceLocatorScope locatorScope = new(provider);

		T api = (T)provider.GetRequiredService<IEngineAPI>();
		api.__INTERNAL_FilledDependencies = filledDependencies;

		object? getService(Type service, DependencyAttribute depAttr) {
			if (depAttr is KeyedDependencyAttribute keyed)
				return depAttr.Required ? provider.GetRequiredKeyedService(service, keyed.Key) : provider.GetKeyedService(service, keyed.Key);
			else
				return depAttr.Required ? provider.GetRequiredService(service) : provider.GetService(service);
		}

		void handleSet(MemberInfo member, DependencyAttribute? depAttr) {
			if (depAttr == null)
				return;

			switch (member) {
				case FieldInfo field:
					field.SetValue(null, getService(depAttr.GetUnderlyingType() ?? field.FieldType, depAttr));
					break;
				case PropertyInfo prop:
					prop.SetValue(null, getService(depAttr.GetUnderlyingType() ?? prop.PropertyType, depAttr));
					break;
				default: // don't add a dependency for junk
					return;
			}

			filledDependencies.Add(member);
		}

		foreach (var member in populateLater) {
			handleSet(member, member.GetCustomAttribute<DependencyAttribute>());
			handleSet(member, member.GetCustomAttribute<KeyedDependencyAttribute>());
		}

		return api;
	}

	/// <summary>
	/// Finalizes the dependency injection setup and returns a finalized <see cref="IServiceProvider"/> (as a <see cref="ClientLauncherAPI"/>).
	/// </summary>
	/// <param name="dedicated"></param>
	/// <returns></returns>
	public ClientLauncherAPI BuildClient() {
		PreBuildAllForms();
#if !SWDS
		this.AddSingleton<IRenderView, RenderView>();
		this.AddKeyedSingleton<EngineTraceClient>(Realm.Client);
		this.AddKeyedSingleton(typeof(IEngineTrace), Realm.Client, (x, _) => x.GetRequiredKeyedService<EngineTraceClient>(Realm.Client));
		this.AddKeyedSingleton<EngineSoundClient>(Realm.Client);
		this.AddKeyedSingleton(typeof(IEngineSound), Realm.Client, (x, _) => x.GetRequiredKeyedService<EngineSoundClient>(Realm.Client));
		this.AddSingleton<IModelRender, ModelRender>();
		this.AddSingleton<IVModelInfoClient, ModelInfoClient>();
		this.AddSingleton<IVModelInfo>(x => x.GetRequiredService<IVModelInfoClient>());
		this.AddKeyedSingleton<ModelInfoServer>(Realm.Server);
		this.AddKeyedSingleton(typeof(IVModelInfo), Realm.Server, (x, _) => x.GetRequiredKeyedService<ModelInfoServer>(Realm.Server));
		// Engine VGUI and how to read it later
		this.AddSingleton<EngineVGui>();
		this.AddSingleton<IEngineVGuiInternal, EngineVGui>(x => x.GetRequiredService<EngineVGui>());
		this.AddSingleton<IEngineVGui, EngineVGui>(x => x.GetRequiredService<EngineVGui>());
		// These interfaces go to client and game dll's
		this.AddSingleton<IEngineClient, EngineClient>();
		this.AddSingleton<ClientLauncherAPI>();
		this.AddSingleton<IEngineAPI, ClientLauncherAPI>(x => x.GetRequiredService<ClientLauncherAPI>());
		this.AddSingleton<IClientLauncherAPI, ClientLauncherAPI>(x => x.GetRequiredService<ClientLauncherAPI>());
#endif
		return PostBuildAllForms<ClientLauncherAPI>();
	}

	/// <summary>
	/// Finalizes the dependency injection setup and returns a finalized <see cref="IServiceProvider"/> (as a <see cref="DedicatedServerAPI"/>).
	/// </summary>
	/// <param name="dedicated"></param>
	/// <returns></returns>
	public DedicatedServerAPI BuildServer() {
		PreBuildAllForms();
#if SWDS
		this.AddSingleton<IVModelInfoClient, ModelInfoServer>();
		this.AddSingleton<IVModelInfo>(x => x.GetRequiredService<IVModelInfoClient>());
		// Game server DLL resolves modelinfo via the server realm key; route it to the same instance.
		this.AddKeyedSingleton(typeof(IVModelInfo), Realm.Server, (x, _) => x.GetRequiredService<IVModelInfoClient>());
		this.AddSingleton<DedicatedServerAPI>();
		this.AddSingleton<IEngineAPI, DedicatedServerAPI>(x => x.GetRequiredService<DedicatedServerAPI>());
		this.AddSingleton<IDedicatedServerAPI, DedicatedServerAPI>(x => x.GetRequiredService<DedicatedServerAPI>());
#endif
		return PostBuildAllForms<DedicatedServerAPI>();
	}

	static IEnumerable<Type> safeTypeGet(Assembly assembly) {
		IEnumerable<Type?> types;
		try {
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e) {
			types = e.Types;
		}
		foreach (var t in types.Where(t => t != null && t.Assembly.GetName().Name != "Steamworks.NET"))
			yield return t!;
	}

	private static object? DetermineInstance(IEngineAPI engineAPI, Type type, bool concommand, ReadOnlySpan<char> name) {
		// We need to find an appropriate instance of the type in question.
		// If it's not registered with the dependency injection framework, then we can't really link anything
		// Should've made it static...
		object? instance = engineAPI.GetService(type);

		// As a last resort, try pulling at interface types.
		if (instance == null) {
			foreach (var iface in type.GetInterfaces()) {
				instance = engineAPI.GetService(iface);
				if (instance != null)
					return instance;
			}
		}
		else
			return instance;

		throw new Exception($"{(concommand ? "ConCommand" : "ConVar")} at member '{name}' was marked as by-instance, and the EngineAPI cannot find an instance of the type it's contained in ({type.Name}). Review if the instance type is an engine component, or if this should be a static field/method. If you are trying to hold a reference to a ConVar, either use a ConVarRef or mark the field/property with a [CvarIgnore] attribute.");
	}

	static bool cvar_initialized;
	public static void ConVar_Register() {
		var engineAPI = Singleton<IEngineAPI>();
		ICvar cvar = engineAPI.GetRequiredService<ICvar>();
		Assembly[] assemblies = [.. AppDomain.CurrentDomain.GetAssemblies().Where(ReflectionUtils.IsSourceEngineAssembly)];

		// Source.CodeAnalysis.ConVars emits Source.Generated.CvarRegistration into each Source assembly. Types it lists
		// are registered through generated code; anything else falls back to the reflection path below.
		Dictionary<Type, CvarRegistrar> declared = [];
		Dictionary<Type, CvarRegistrar> inherited = [];
		HashSet<Type> reflected = [];
		HashSet<Assembly> generated = [];
		foreach (var assembly in assemblies) {
			if (LoadGeneratedCvarRegistration(assembly, declared, inherited, reflected))
				generated.Add(assembly);
		}

		Func<Type, bool, string, object> resolve = (type, concommand, name) => DetermineInstance(engineAPI, type, concommand, name)!;
		List<(Type Declaring, CvarRegistrar Registrar)> registrars = [];

		foreach (var assembly in assemblies) {
			cvar.SetAssemblyIdentifier(assembly);
			bool hasGenerated = generated.Contains(assembly);
			foreach (var type in assembly.GetTypes()) {
				if (type.IsAssignableTo(typeof(ConVar)) || type == typeof(ConVarRef))
					continue;

				if (!hasGenerated || !FindGeneratedCvarRegistrars(type, assemblies, generated, declared, inherited, reflected, registrars)) {
					ConVar_RegisterByReflection(engineAPI, cvar, type);
					continue;
				}

				RunStaticConstructor(type);

				// Properties, then fields, then methods; the same order the reflection path uses.
				for (int phase = 0; phase < 3 && registrars.Count > 0; phase++) {
					foreach (var (declaring, registrar) in registrars)
						registrar(phase, declaring, type, cvar, resolve);
				}
			}
		}
	}

	static bool LoadGeneratedCvarRegistration(Assembly assembly, Dictionary<Type, CvarRegistrar> declared, Dictionary<Type, CvarRegistrar> inherited, HashSet<Type> reflected) {
		MethodInfo? populate = assembly.GetType("Source.Generated.CvarRegistration")?.GetMethod("Populate", BindingFlags.Public | BindingFlags.Static);
		if (populate == null)
			return false;

		Dictionary<string, CvarRegistrar> declaredNames = [];
		Dictionary<string, CvarRegistrar> inheritedNames = [];
		List<string> reflectedNames = [];
		populate.CreateDelegate<Action<Dictionary<string, CvarRegistrar>, Dictionary<string, CvarRegistrar>, List<string>>>()(declaredNames, inheritedNames, reflectedNames);

		foreach (var (name, registrar) in declaredNames)
			declared.Add(assembly.GetType(name, true)!, registrar);
		foreach (var (name, registrar) in inheritedNames)
			inherited.Add(assembly.GetType(name, true)!, registrar);
		foreach (string name in reflectedNames)
			reflected.Add(assembly.GetType(name, true)!);

		return true;
	}

	/// <summary>
	/// Collects the generated registrars that apply to <paramref name="type"/>: its own, then those of each base type
	/// (reflection returns inherited non-private instance members, so derived types register them too).
	/// Returns false if the type has to go through reflection instead.
	/// </summary>
	static bool FindGeneratedCvarRegistrars(Type type, Assembly[] sourceAssemblies, HashSet<Assembly> generated, Dictionary<Type, CvarRegistrar> declared, Dictionary<Type, CvarRegistrar> inherited, HashSet<Type> reflected, List<(Type Declaring, CvarRegistrar Registrar)> registrars) {
		registrars.Clear();
		if (reflected.Contains(type))
			return false;

		if (declared.TryGetValue(type, out CvarRegistrar? registrar))
			registrars.Add((type, registrar));

		for (Type? baseType = type.BaseType; baseType != null; baseType = baseType.BaseType) {
			Type key = baseType.IsConstructedGenericType ? baseType.GetGenericTypeDefinition() : baseType;
			if (reflected.Contains(key))
				return false;
			if (!generated.Contains(key.Assembly) && Array.IndexOf(sourceAssemblies, key.Assembly) != -1)
				return false;
			if (inherited.TryGetValue(key, out registrar))
				registrars.Add((key, registrar));
		}

		return true;
	}

	static void RunStaticConstructor(Type type) {
		try {
			RuntimeHelpers.RunClassConstructor(type.TypeHandle);
		}
		catch (TypeInitializationException tie) when (tie.InnerException != null) {
			ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
			throw;
		}
	}

	static void ConVar_RegisterByReflection(IEngineAPI engineAPI, ICvar cvar, Type type) {
		Type CVAR = typeof(ConVar);
		{
			{
				var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
				var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
				var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

				// If any props/fields exist, run the cctor so we can pull out static cvars/concmds
				if ((props.Length + fields.Length) > 0) {
					try {
						RuntimeHelpers.RunClassConstructor(type.TypeHandle);
					}
					catch (TypeInitializationException tie) when (tie.InnerException != null) {
						ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
						throw;
					}
				}

				for (int i = 0; i < props.Length; i++) {
					PropertyInfo prop = props[i];
					if (!prop.PropertyType.IsAssignableTo(CVAR))
						continue;
					if (prop.GetCustomAttribute<CvarIgnoreAttribute>() != null)
						continue;

					var getMethod = prop.GetGetMethod();

					if (getMethod == null)
						continue;

					if (getMethod.IsStatic) {
						// Pull a static reference out to link
						ConVar? cv = (ConVar?)getMethod.Invoke(null, null);
						if (cv == null) continue;
						if (cv.GetName() == null!) cv.SetName(prop.Name);
						cvar.RegisterConCommand(cv);
					}
					else {
						object? instance = DetermineInstance(engineAPI, type, false, prop.Name);
						ConVar? cv = (ConVar?)getMethod.Invoke(instance, null);
						if (cv == null) continue;
						if (cv.GetName() == null!) cv.SetName(prop.Name);
						cvar.RegisterConCommand(cv);
					}
				}

				for (int i = 0; i < fields.Length; i++) {
					FieldInfo field = fields[i];
					if (!field.FieldType.IsAssignableTo(CVAR))
						continue;
					if (field.GetCustomAttribute<CvarIgnoreAttribute>() != null)
						continue;

					if (field.IsStatic) {
						// Pull a static reference out to link
						ConVar? cv = (ConVar?)field.GetValue(null);
						if (cv == null) continue;
						if (cv.GetName() == null!) cv.SetName(field.Name);
						cvar.RegisterConCommand(cv);
					}
					else {
						object? instance = DetermineInstance(engineAPI, type, false, field.Name);
						ConVar? cv = (ConVar?)field.GetValue(instance);
						if (cv == null) continue;
						if (cv.GetName() == null!) cv.SetName(field.Name);
						cvar.RegisterConCommand(cv);
					}
				}

				for (int i = 0; i < methods.Length; i++) {
					MethodInfo method = methods[i];
					ConCommandAttribute? cmdAttr = method.GetCustomAttribute<ConCommandAttribute>();
					if (cmdAttr == null)
						continue;

					ConCommandAttribute attribute = method.GetCustomAttribute<ConCommandAttribute>()!; // < never null!
					object? instance = method.IsStatic ? null : DetermineInstance(engineAPI, type, true, method.Name);

					// Lets see if we can find a FnCommandCompletionCallback...
					FnCommandCompletionCallback? completionCallback = null;
					if (attribute.AutoCompleteMethod != null)
						type.TryExtractMethodDelegate(instance, x => x.Name == attribute.AutoCompleteMethod, out completionCallback);

					// Construct a new ConCommand
					string cmdName = attribute.Name ?? method.Name;
					string? helpText = attribute.HelpText;
					FCvar flags = attribute.Flags;

					ConCommand cmd;

					if (method.TryToDelegate<FnCommandCallbackVoid>(instance, out var callbackVoid))
						cmd = new ConCommand(cmdName, callbackVoid, helpText, flags, completionCallback);
					else if (method.TryToDelegate<FnCommandCallback>(instance, out var callback))
						cmd = new ConCommand(cmdName, callback, helpText, flags, completionCallback);
					else if (method.TryToDelegate<FnCommandCallbackSourced>(instance, out var callbackSourced))
						cmd = new ConCommand(cmdName, callbackSourced, helpText, flags, completionCallback);
					else
						throw new ArgumentException("Cannot dynamically produce ConCommand with the arguments we were given");

					cvar.RegisterConCommand(cmd);
				}
			}
		}
	}
}
