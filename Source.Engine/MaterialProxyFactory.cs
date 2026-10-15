using Source.Common.MaterialSystem;

using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Engine;

public class MaterialProxyFactory : IMaterialProxyFactory
{
	public IMaterialProxy? CreateProxy(ReadOnlySpan<char> proxyName) {
#if SWDS
		return null;
#else
		IMaterialProxy? materialProxy = LookupProxy(proxyName);
		if (materialProxy == null) {
			ConDMsg($"Can't find material proxy \"{proxyName}\"\n");
			return null;
		}

		return materialProxy;
#endif
	}

	public void DeleteProxy(IMaterialProxy proxy) {
		if (proxy != null)
			proxy.Release();
	}

	private IMaterialProxy? LookupProxy(ReadOnlySpan<char> proxyName) {
		return g_ClientDLL?.GetMaterialProxyInterfaceFn()?.Invoke(proxyName);
	}
}
