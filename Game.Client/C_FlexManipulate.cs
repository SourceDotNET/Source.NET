using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
using FIELD = FIELD<C_FlexManipulate>;
[LinkEntityToClass("manipulate_flex")]
[NetworkName("CFlexManipulate")]
public class C_FlexManipulate : C_BaseEntity
{
	public static readonly RecvTable DT_FlexManipulate = new(DT_BaseEntity, [
		RecvPropFloat(FIELD.OF(nameof(ExScale))),
		RecvPropVector(FIELD.OF(nameof(EyesLocalTarget))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(FlexWeights)), RecvPropFloat(null!)),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_FlexManipulate);

	[NetworkName("m_flexScale")]
	public float ExScale;
	[NetworkName("m_EyesLocalTarget")]
	public Vector3 EyesLocalTarget;
	[NetworkName("m_iFlexWeights")]
	public InlineArray96<float> FlexWeights;

	public void SetFlexWeight(byte index, float weight) {
		if (index < 96 && FlexWeights[index] != weight)
			FlexWeights[index] = weight;
	}
}
