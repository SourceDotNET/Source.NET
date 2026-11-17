using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.GUI;
using Source.GUI.Controls;
using System.Numerics;

namespace Game.Client.HL2;

[DeclareHudElement(Name = "CHudHealth")]
public class HudHealth : HudNumericDisplay, IHudElement
{
	int Health;
#if !GMOD_DLL
	int BitsDamage;
#endif

	public HudHealth(string? panelName) : base(null, "HudHealth") {
		/*(IHudElement.)*/ ElementName = panelName;

		var parent = clientMode.GetViewport();
		SetParent(parent);
#if GMOD_DLL
		((IHudElement)this).SetHiddenBits(HideHudBits.Health | HideHudBits.PlayerDead | HideHudBits.NeedSuit);
		Health = 0;
#endif
	}

	public void Init() {
		IHudElement.HookMessage("Damage", Damage);
		Reset();
	}

	public void VidInit() {
		Reset();
	}
	public override void Reset() {
		Health = -1;
#if !GMOD_DLL
		BitsDamage = 0;
#endif
		ReadOnlySpan<char> tempString = Localize.Find("#Valve_Hud_HEALTH");

		if (!tempString.IsEmpty) 
			SetLabelText(tempString);
		else
			SetLabelText("HEALTH");
		
		SetDisplayValue(Health);
	}
	public override void OnThink() {
		int newHealth = 0;
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local != null)
			newHealth = Math.Max(local.GetHealth(), 0);
		
		if (newHealth == Health)
			return;

		Health = newHealth;

		if (Health >= 20) {
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthIncreasedAbove20");
		}
		else if (Health > 0) {
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthIncreasedBelow20");
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthLow");
		}

#if GMOD_DLL
		if (Health < 1000)
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthBelow999");
		else
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthOver999");
#endif

		SetDisplayValue(Health);
	}
	private void Damage(bf_read msg) {
		int armor = msg.ReadByte(); 
		int damageTaken = msg.ReadByte();   
		long bitsDamage = msg.ReadLong();

		Vector3 vecFrom = new() {
			X = msg.ReadBitCoord(),
			Y = msg.ReadBitCoord(),
			Z = msg.ReadBitCoord()
		};

		if (damageTaken > 0 || armor > 0) 
			if (damageTaken > 0)
				clientMode.GetViewportAnimationController()!.StartAnimationSequence("HealthDamageTaken");
	}
}
