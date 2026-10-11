using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
using FIELD = FIELD<FlexManipulate>;
[LinkEntityToClass("manipulate_flex")]
[NetworkName("CFlexManipulate")]
public class FlexManipulate : BaseEntity
{
	public static readonly SendTable DT_FlexManipulate = new(DT_BaseEntity, [
		SendPropFloat(FIELD.OF(nameof(ExScale)), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF(nameof(EyesLocalTarget)), 0, PropFlags.NoScale),
		SendPropArray3(FIELD.OF_ARRAY(nameof(FlexWeights)), SendPropFloat(null!, 0, PropFlags.NoScale)),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_FlexManipulate);

	[NetworkName("m_flexScale")]
	public float ExScale;
	[NetworkName("m_EyesLocalTarget")]
	public Vector3 EyesLocalTarget;
	[NetworkName("m_iFlexWeights")]
	public InlineArray96<float> FlexWeights;

	public void SetFlexWeight(byte index, float weight) {
		if (index < 96 && FlexWeights[index] != weight) {
			NetworkStateChanged();
			FlexWeights[index] = weight;
		}
	}
}
