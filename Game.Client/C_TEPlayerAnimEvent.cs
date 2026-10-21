using Game.Shared;

using Source.Common;

namespace Game.Client; 

[NetworkName("CTEPlayerAnimEvent", NoBase = true)]
public partial class C_TEPlayerAnimEvent : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(Player),
		new(nameof(Event)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(Data)) { Bits = 32 },
	];

	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_iEvent")]
	public readonly int Event;
	[NetworkVar("m_nData")]
	public readonly int Data;
}
