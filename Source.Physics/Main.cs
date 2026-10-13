global using static Source.Physics.SourceDllMain;

using Box3D;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Physics;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


[assembly: DisableRuntimeMarshalling]

namespace Source.Physics;


public static class SourceDllMain
{
	[Dependency] public static IPhysicsSurfaceProps physprops { get; private set; } = null!;
	[Dependency] public static IPhysicsCollision physcollision { get; private set; } = null!;
}

internal sealed class PhysicsCollisionSet : IPhysicsCollisionSet
{
	readonly uint[] Bits = new uint[32];

	public void EnableCollisions(int index0, int index1) {
		Bits[index0] |= 1u << index1;
		Bits[index1] |= 1u << index0;
	}

	public void DisableCollisions(int index0, int index1) {
		Bits[index0] &= ~(1u << index1);
		Bits[index1] &= ~(1u << index0);
	}

	public bool ShouldCollide(int index0, int index1) => (Bits[index0] & (1u << index1)) != 0;
}

public unsafe class PhysicsInterface : IPhysics
{
	readonly List<IPhysicsEnvironment> Environments = [];
	readonly Dictionary<uint, PhysicsCollisionSet> CollisionSets = [];

	public static void DLLInit(IServiceCollection services) {
		services.AddSingleton<IPhysicsCollision, PhysicsCollide>();
		services.AddSingleton<IPhysicsSurfaceProps, PhysicsSurfaceProps>();

		B3.SetAssertHandler(DiagnosticHandler.Instance);
		B3.SetLogHandler(DiagnosticHandler.Instance);
	}

	sealed class DiagnosticHandler : IAssertHandler, ILogHandler
	{
		public static readonly DiagnosticHandler Instance = new();

		public int OnAssert(string condition, string fileName, int lineNumber) {
			Warning($"Box3D assert: {condition} ({fileName}:{lineNumber})\n");
			return 0;
		}

		public void OnLog(string message) => Msg(message);
	}

	public IPhysicsEnvironment CreateEnvironment() {
		IPhysicsEnvironment environment = CreatePhysicsEnvironment();
		Environments.Add(environment);
		return environment;
	}

	public void DestroyEnvironment(IPhysicsEnvironment? env) {
		if (env == null)
			return;

		Environments.Remove(env);
		(env as PhysicsEnvironment)?.Destroy();
	}

	[ConCommand("vbox_dumpcollide", "Dumps collision and mass data of physics objects whose name contains the argument.", FCvar.Cheat)]
	void vbox_dumpcollide(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg("Usage: vbox_dumpcollide <object name substring>\n");
			return;
		}

		for (int i = 0; i < Environments.Count; i++) {
			if (Environments[i] is PhysicsEnvironment environment)
				environment.DumpCollide(i, args[1]);
		}
	}

	public IPhysicsEnvironment? GetActiveEnvironmentByIndex(int index) {
		if (index < 0 || index >= Environments.Count)
			return null;

		return Environments[index];
	}

	public IPhysicsObjectPairHash CreateObjectPairHash() => PhysicsEnvironmentGlobals.CreateObjectPairHash();

	public void DestroyObjectPairHash(IPhysicsObjectPairHash hash) { }

	public IPhysicsCollisionSet FindOrCreateCollisionSet(uint id, int maxElementCount) {
		if (maxElementCount > 32)
			return null!;

		if (CollisionSets.TryGetValue(id, out PhysicsCollisionSet? set))
			return set;

		set = new PhysicsCollisionSet();
		CollisionSets[id] = set;
		return set;
	}

	public IPhysicsCollisionSet FindCollisionSet(uint id) => CollisionSets.TryGetValue(id, out PhysicsCollisionSet? set) ? set : null!;

	public void DestroyAllCollisionSets() => CollisionSets.Clear();
}
