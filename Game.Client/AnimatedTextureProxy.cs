namespace Game.Client;

[ExposeMaterialProxy(Name = "AnimatedTexture")]
public class AnimatedTextureProxy : BaseAnimatedTextureProxy
{
	protected override TimeUnit_t GetAnimationStartTime(object? baseEntity) => 0;
}
