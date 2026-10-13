using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

namespace Game.Client;

public abstract class BaseAnimatedTextureProxy : IMaterialProxy
{
	public BaseAnimatedTextureProxy() {
		Cleanup();
	}

	public virtual bool Init(IMaterial material, KeyValues keyValues) {
		ReadOnlySpan<char> animatedTextureVarName = keyValues.GetString("animatedTextureVar");
		if (animatedTextureVarName.IsEmpty)
			return false;

		AnimatedTextureVar = material.FindVar(animatedTextureVarName, out bool foundVar, false);
		if (!foundVar)
			return false;

		ReadOnlySpan<char> animatedTextureFrameNumVarName = keyValues.GetString("animatedTextureFrameNumVar");
		if (animatedTextureFrameNumVarName.IsEmpty)
			return false;

		AnimatedTextureFrameNumVar = material.FindVar(animatedTextureFrameNumVarName, out foundVar, false);
		if (!foundVar)
			return false;

		FrameRate = keyValues.GetFloat("animatedTextureFrameRate", 15);
		WrapAnimation = keyValues.GetInt("animationNoWrap", 0) == 0;
		return true;
	}

	void Cleanup() {
		AnimatedTextureVar = null;
		AnimatedTextureFrameNumVar = null;
	}

	public virtual void OnBind(object? entity) {
		Assert(AnimatedTextureVar != null);

		if (AnimatedTextureVar!.GetVarType() != MaterialVarType.Texture)
			return;

		ITexture texture = AnimatedTextureVar.GetTextureValue()!;
		int numFrames = texture.GetNumAnimationFrames();

		if (numFrames <= 0) {
			AssertMsg(false, "0 frames in material calling animated texture proxy");
			return;
		}

		TimeUnit_t startTime = GetAnimationStartTime(entity);
		TimeUnit_t deltaTime = gpGlobals.CurTime - startTime;
		TimeUnit_t prevTime = deltaTime - gpGlobals.FrameTime;

		if (deltaTime < 0.0)
			deltaTime = 0.0;
		if (prevTime < 0.0)
			prevTime = 0.0;

		TimeUnit_t frame = FrameRate * deltaTime;
		TimeUnit_t prevFrame = FrameRate * prevTime;

		int intFrame = ((int)frame) % numFrames;
		int intPrevFrame = ((int)prevFrame) % numFrames;

		if (intPrevFrame > intFrame) {
			if (WrapAnimation)
				AnimationWrapped(entity);
			else {
				if (prevFrame < numFrames)
					AnimationWrapped(entity);
				intFrame = numFrames - 1;
			}
		}

		AnimatedTextureFrameNumVar!.SetIntValue(intFrame);
	}

	public virtual void Release() { }

	public virtual IMaterial GetMaterial() => AnimatedTextureVar!.GetOwningMaterial();

	protected abstract TimeUnit_t GetAnimationStartTime(object? baseEntity);
	protected virtual void AnimationWrapped(object? baseEntity) { }

	protected IMaterialVar? AnimatedTextureVar;
	protected IMaterialVar? AnimatedTextureFrameNumVar;
	protected float FrameRate;
	protected bool WrapAnimation;
}
