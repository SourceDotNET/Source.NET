using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPropCrane")]
public partial class C_PropCrane : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(MagnetOn),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_bMagnetOn")]
	public bool MagnetOn;
	[NetworkVar("m_bEnterAnimOn")]
	public bool EnterAnimOn;
	[NetworkVar("m_bExitAnimOn")]
	public bool ExitAnimOn;
	[NetworkVar("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint;
}
