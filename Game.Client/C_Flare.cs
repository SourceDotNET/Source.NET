using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CFlare")]
public partial class C_Flare : C_BaseCombatCharacter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TimeBurnOut)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Light),
		nameof(Smoke),
		nameof(PropFlare),
	];

	[NetworkVar("m_flTimeBurnOut")]
	public float TimeBurnOut;
	[NetworkVar("m_flScale")]
	public float Scale;
	[NetworkVar("m_bLight")]
	public bool Light;
	[NetworkVar("m_bSmoke")]
	public bool Smoke;
	[NetworkVar("m_bPropFlare")]
	public bool PropFlare;
}
