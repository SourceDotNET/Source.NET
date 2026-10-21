using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("manipulate_flex")]
[NetworkName("CFlexManipulate")]
public partial class FlexManipulate : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ExScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EyesLocalTarget)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FlexWeights)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flexScale")]
	public partial float ExScale { get; set; }
	[NetworkVar("m_EyesLocalTarget")]
	public partial Vector3 EyesLocalTarget { get; set; }
	[NetworkVar("m_iFlexWeights")]
	public partial ref readonly InlineArray96<float> FlexWeights { get; }

	public void SetFlexWeight(byte index, float weight) {
		if (index < 96 && FlexWeights[index] != weight) {
			NetworkStateChanged();
			FlexWeightsForModify()[index] = weight;
		}
	}
}
