using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CColorCorrectionVolume")]
public partial class C_ColorCorrectionVolume : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Enabled),
		new(nameof(MaxWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeDuration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Weight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LookupFilename)) { Encoding = NetEncoding.String },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
	];

	[NetworkVar("m_bEnabled")]
	public bool Enabled;
	[NetworkVar("m_MaxWeight")]
	public float MaxWeight;
	[NetworkVar("m_FadeDuration")]
	public float FadeDuration;
	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(Weight),
	];

	[NetworkVar("m_Weight")]
	public float Weight;
	[NetworkVar("m_lookupFilename")]
	public InlineArrayMaxPath<char> LookupFilename;
}
