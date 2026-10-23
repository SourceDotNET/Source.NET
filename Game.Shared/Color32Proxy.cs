#if CLIENT_DLL || GAME_DLL
using Source.Common;

namespace Game.Shared;

public static class Color32Proxy
{
#if GAME_DLL
	public static void Send(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID)
		=> Game.Server.SendProxy.SendProxy_Color32ToInt(prop, instance, field, ref outData, element, objectID);
#else
	public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field)
		=> Game.Client.RecvProxy.RecvProxy_IntToColor32(in data, instance, field);
#endif
}
#endif
