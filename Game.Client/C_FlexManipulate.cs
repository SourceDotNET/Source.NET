using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[LinkEntityToClass("manipulate_flex")]
[NetworkName("CFlexManipulate")]
public partial class C_FlexManipulate : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ExScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EyesLocalTarget)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FlexWeights)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flexScale")]
	public float ExScale;
	[NetworkVar("m_EyesLocalTarget")]
	public Vector3 EyesLocalTarget;
	[NetworkVar("m_iFlexWeights")]
	public InlineArray96<float> FlexWeights;

	public void SetFlexWeight(byte index, float weight) {
		if (index < 96 && FlexWeights[index] != weight)
			FlexWeights[index] = weight;
	}
}
