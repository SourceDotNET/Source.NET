#if CLIENT_DLL || GAME_DLL
using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Shared;

public class IPredictionSystem
{
	public IPredictionSystem? NextSystem;
	public static IPredictionSystem? PredictionSystems;

	public IPredictionSystem(){
		NextSystem = PredictionSystems;
		PredictionSystems = this;

		SuppressEvent = false;
		SuppressHost = null;
		StatusPushed = 0;
	}

	public void SetSuppressEvent(bool state) => SuppressEvent = state;
	public void SetSuppressHost(BaseEntity? host) => SuppressHost = host;
	public BaseEntity? GetSuppressHost() => DisableFiltering() ? null : SuppressHost;
	public bool CanPredict() => DisableFiltering() ? false : !SuppressEvent;

	public static void SuppressEvents(bool state) {
		IPredictionSystem? sys = PredictionSystems;
		while (sys != null) {
			sys.SetSuppressEvent(state);
			sys = sys.GetNext();
		}
	}

	public static void SuppressHostEvents(BaseEntity? host) {
		IPredictionSystem? sys = PredictionSystems;
		while (sys != null) {
			sys.SetSuppressHost(host);
			sys = sys.GetNext();
		}
	}


	public bool SuppressEvent;
	// todo: GMod keeps the suppress host as an EHANDLE, so a removed host counts as no host
	public BaseEntity? SuppressHost;
	public int StatusPushed;

	static void Push(){
		IPredictionSystem? sys = PredictionSystems;
		while(sys != null){
			sys._Push();
			sys = sys.GetNext();
		}
	}
	static void Pop(){
		IPredictionSystem? sys = PredictionSystems;
		while (sys != null) {
			sys._Pop();
			sys = sys.GetNext();
		}
	}
	void _Push() => ++StatusPushed;
	void _Pop() => --StatusPushed;
	bool DisableFiltering() => StatusPushed > 0 ? true : false;

	public IPredictionSystem? GetNext() => NextSystem;
}
#endif
