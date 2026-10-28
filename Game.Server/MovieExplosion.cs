using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("env_movieexplosion")]
[NetworkName("MovieExplosion", Table = "DT_MovieExplosion")]
public partial class MovieExplosion : BaseParticleEntity
{
}
