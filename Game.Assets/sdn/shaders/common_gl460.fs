#ifndef COMMON_GL460_FS
#define COMMON_GL460_FS

#include "common_gl460.glsl"

// System defined pixel shader constants

// NOTE: w == 1.0f / (Dest alpha compressed depth range).
#define g_LinearFogColor            ps_const[29]
#define OO_DESTALPHA_DEPTH_RANGE    (g_LinearFogColor.w)

// Linear and gamma light scale values
#define cLightScale                 ps_const[30]
#define LINEAR_LIGHT_SCALE          (cLightScale.x)
#define LIGHT_MAP_SCALE             (cLightScale.y)
#define ENV_MAP_SCALE               (cLightScale.z)
#define GAMMA_LIGHT_SCALE           (cLightScale.w)

// Flashlight constants
#define cFlashlightColor            ps_const[28]
#define cFlashlightScreenScale      ps_const[31] // .zw are currently unused
#define flFlashlightNoLambertValue  cFlashlightColor.w // This is either 0.0 or 2.0

#define TONEMAP_SCALE_NONE 0
#define TONEMAP_SCALE_LINEAR 1
#define TONEMAP_SCALE_GAMMA 2

#define PIXEL_FOG_TYPE_NONE -1 //MATERIAL_FOG_NONE is handled by PIXEL_FOG_TYPE_RANGE, this is for explicitly disabling fog in the shader
#define PIXEL_FOG_TYPE_RANGE 0 //range+none packed together in ps2b. Simply none in ps20 (instruction limits)
#define PIXEL_FOG_TYPE_HEIGHT 1
#define PIXEL_FOG_TYPE_RANGE_RADIAL 2

// If you change these, make the corresponding change in hardwareconfig.cpp
#define NVIDIA_PCF_POISSON	0
#define ATI_NOPCF			1
#define ATI_NO_PCF_FETCH4	2

// Needs to match NormalDecodeMode_t enum in imaterialsystem.h
#define NORM_DECODE_NONE			0
#define NORM_DECODE_ATI2N			1
#define NORM_DECODE_ATI2N_ALPHA		2

vec4 DecompressNormal(sampler2D NormalSampler, vec2 tc, int nDecompressionMode, sampler2D AlphaSampler)
{
    vec4 normalTexel = texture(NormalSampler, tc);
    vec4 result;

    if (nDecompressionMode == NORM_DECODE_NONE)
    {
        result = vec4(normalTexel.xyz * 2.0 - 1.0, normalTexel.a);
    }
    else if (nDecompressionMode == NORM_DECODE_ATI2N)
    {
        result.xy = normalTexel.xy * 2.0 - 1.0;
        result.z = sqrt(1.0 - dot(result.xy, result.xy));
        result.a = 1.0;
    }
    else // ATI2N plus ATI1N for alpha
    {
        result.xy = normalTexel.xy * 2.0 - 1.0;
        result.z = sqrt(1.0 - dot(result.xy, result.xy));
        result.a = texture(AlphaSampler, tc).x;					// Note that this comes in on the X channel
    }

    return result;
}

vec4 DecompressNormal(sampler2D NormalSampler, vec2 tc, int nDecompressionMode)
{
    return DecompressNormal(NormalSampler, tc, nDecompressionMode, NormalSampler);
}

// texture combining modes for combining base and detail/basetexture2
#define TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2 0				// original mode
#define TCOMBINE_RGB_ADDITIVE 1								// base.rgb+detail.rgb*fblend
#define TCOMBINE_DETAIL_OVER_BASE 2
#define TCOMBINE_FADE 3										// straight fade between base and detail.
#define TCOMBINE_BASE_OVER_DETAIL 4                         // use base alpha for blend over detail
#define TCOMBINE_RGB_ADDITIVE_SELFILLUM 5                   // add detail color post lighting
#define TCOMBINE_RGB_ADDITIVE_SELFILLUM_THRESHOLD_FADE 6
#define TCOMBINE_MOD2X_SELECT_TWO_PATTERNS 7				// use alpha channel of base to select between mod2x channels in r+a of detail
#define TCOMBINE_MULTIPLY 8
#define TCOMBINE_MASK_BASE_BY_DETAIL_ALPHA 9                // use alpha channel of detail to mask base
#define TCOMBINE_SSBUMP_BUMP 10								// use detail to modulate lighting as an ssbump
#define TCOMBINE_SSBUMP_NOBUMP 11					        // detail is an ssbump but use it as an albedo. shader does the magic here - no user needs to specify mode 11

vec4 TextureCombine(vec4 baseColor, vec4 detailColor, int combine_mode, float fBlendFactor)
{
    if (combine_mode == TCOMBINE_MOD2X_SELECT_TWO_PATTERNS)
    {
        vec3 dc = vec3(mix(detailColor.r, detailColor.a, baseColor.a));
        baseColor.rgb *= mix(vec3(1, 1, 1), 2.0 * dc, fBlendFactor);
    }
    else if (combine_mode == TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2)
        baseColor.rgb *= mix(vec3(1, 1, 1), 2.0 * detailColor.rgb, fBlendFactor);
    else if (combine_mode == TCOMBINE_RGB_ADDITIVE)
        baseColor.rgb += fBlendFactor * detailColor.rgb;
    else if (combine_mode == TCOMBINE_DETAIL_OVER_BASE)
    {
        float fblend = fBlendFactor * detailColor.a;
        baseColor.rgb = mix(baseColor.rgb, detailColor.rgb, fblend);
    }
    else if (combine_mode == TCOMBINE_FADE)
        baseColor = mix(baseColor, detailColor, fBlendFactor);
    else if (combine_mode == TCOMBINE_BASE_OVER_DETAIL)
    {
        float fblend = fBlendFactor * (1.0 - baseColor.a);
        baseColor.rgb = mix(baseColor.rgb, detailColor.rgb, fblend);
        baseColor.a = detailColor.a;
    }
    else if (combine_mode == TCOMBINE_MULTIPLY)
        baseColor = mix(baseColor, baseColor * detailColor, fBlendFactor);
    else if (combine_mode == TCOMBINE_MASK_BASE_BY_DETAIL_ALPHA)
        baseColor.a = mix(baseColor.a, baseColor.a * detailColor.a, fBlendFactor);
    else if (combine_mode == TCOMBINE_SSBUMP_NOBUMP)
        baseColor.rgb = baseColor.rgb * dot(detailColor.rgb, vec3(2.0 / 3.0));
    return baseColor;
}

vec3 TextureCombinePostLighting(vec3 lit_baseColor, vec4 detailColor, int combine_mode, float fBlendFactor)
{
    if (combine_mode == TCOMBINE_RGB_ADDITIVE_SELFILLUM)
        lit_baseColor += fBlendFactor * detailColor.rgb;
    else if (combine_mode == TCOMBINE_RGB_ADDITIVE_SELFILLUM_THRESHOLD_FADE)
    {
        // fade in an unusual way - instead of fading out color, remap an increasing band of it from
        // 0..1
        float f = fBlendFactor - 0.5;
        float fMult = (f >= 0.0) ? 1.0 / fBlendFactor : 4.0 * fBlendFactor;
        float fAdd = (f >= 0.0) ? 1.0 - fMult : -0.5 * fMult;
        lit_baseColor += clamp(fMult * detailColor.rgb + fAdd, 0.0, 1.0);
    }
    return lit_baseColor;
}

float CalcWaterFogAlpha(float flWaterZ, float flEyePosZ, float flWorldPosZ, float flProjPosZ, float flFogOORange)
{
    float flDepthFromWater = flWaterZ - flWorldPosZ;

    // Calculate the ratio of water fog to regular fog (ie. how much of the distance from the viewer
    // to the vert is actually underwater.
    float flDepthFromEye = flEyePosZ - flWorldPosZ;
    float f = clamp(flDepthFromWater * (1.0 / flDepthFromEye), 0.0, 1.0);

    // $tmp.w is now the distance that we see through water.
    return clamp(f * flProjPosZ * flFogOORange, 0.0, 1.0);
}

float CalcRangeFog(float flProjPosZ, float flFogStartOverRange, float flFogMaxDensity, float flFogOORange)
{
    return clamp(min(flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange), 0.0, 1.0);
}

float CalcRadialFog_NonFixedFunction(vec3 vWorldPos, vec3 vEyePos, float flFogMaxDensity, float flFogEndOverRange, float flFogOORange)
{
    float flDistance = distance(vEyePos.xyz, vWorldPos.xyz);
    return min(flFogMaxDensity, clamp((flDistance * flFogOORange) - flFogEndOverRange, 0.0, 1.0));
}

float CalcPixelFogFactor(int iPIXELFOGTYPE, vec4 fogParams, vec3 vEyePos, vec3 vWorldPos, float flProjPosZ)
{
    float retVal = 0.0;
    if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_NONE)
    {
        retVal = 0.0;
    }
    else if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE) //range fog, or no fog depending on fog parameters
    {
        retVal = CalcRangeFog(flProjPosZ, fogParams.x, fogParams.z, fogParams.w);
    }
    else if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE_RADIAL)
    {
        float flFogMaxDensity = fogParams.z;
        float flFogEndOverRange = fogParams.x;
        float flFogOORange = fogParams.w;

        retVal = CalcRadialFog_NonFixedFunction(vWorldPos, vEyePos, flFogMaxDensity, flFogEndOverRange, flFogOORange);
    }
    else if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT) //height fog
    {
        retVal = CalcWaterFogAlpha(fogParams.y, vEyePos.z, vWorldPos.z, flProjPosZ, fogParams.w);
    }

    return retVal;
}

float CalcPixelFogFactor(int iPIXELFOGTYPE, vec4 fogParams, float flEyePosZ, float flWorldPosZ, float flProjPosZ)
{
    if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE_RADIAL)
        return CalcPixelFogFactor(PIXEL_FOG_TYPE_RANGE, fogParams, vec3(0.0, 0.0, flEyePosZ), vec3(0.0, 0.0, flWorldPosZ), flProjPosZ);

    return CalcPixelFogFactor(iPIXELFOGTYPE, fogParams, vec3(0.0, 0.0, flEyePosZ), vec3(0.0, 0.0, flWorldPosZ), flProjPosZ);
}

vec3 BlendPixelFog(vec3 vShaderColor, float pixelFogFactor, vec3 vFogColor, int iPIXELFOGTYPE)
{
    if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE || iPIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE_RADIAL)
    {
        pixelFogFactor = clamp(pixelFogFactor, 0.0, 1.0);
        return mix(vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor);
    }
    else if (iPIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT)
    {
        return mix(vShaderColor.rgb, vFogColor.rgb, clamp(pixelFogFactor, 0.0, 1.0));
    }

    return vShaderColor;
}

// The framebuffer performs the linear->gamma conversion for us (GL_FRAMEBUFFER_SRGB), which is
// the equivalent of the CONVERT_TO_SRGB == 0 path.
vec3 SRGBOutput(vec3 vShaderColor)
{
    return vShaderColor;
}

float SoftParticleDepth(float flDepth)
{
    return flDepth * OO_DESTALPHA_DEPTH_RANGE;
}

float DepthToDestAlpha(float flProjZ)
{
    return SoftParticleDepth(flProjZ);
}

vec4 FinalOutput(vec4 vShaderColor, float pixelFogFactor, int iPIXELFOGTYPE, int iTONEMAP_SCALE_TYPE, bool bWriteDepthToDestAlpha, float flProjZ)
{
    vec4 result;
    if (iTONEMAP_SCALE_TYPE == TONEMAP_SCALE_LINEAR)
    {
        result.rgb = vShaderColor.rgb * LINEAR_LIGHT_SCALE;
    }
    else if (iTONEMAP_SCALE_TYPE == TONEMAP_SCALE_GAMMA)
    {
        result.rgb = vShaderColor.rgb * GAMMA_LIGHT_SCALE;
    }
    else if (iTONEMAP_SCALE_TYPE == TONEMAP_SCALE_NONE)
    {
        result.rgb = vShaderColor.rgb;
    }

    if (bWriteDepthToDestAlpha)
        result.a = DepthToDestAlpha(flProjZ);
    else
        result.a = vShaderColor.a;

    result.rgb = BlendPixelFog(result.rgb, pixelFogFactor, g_LinearFogColor.rgb, iPIXELFOGTYPE);

    result.rgb = SRGBOutput(result.rgb); //SRGB in pixel shader conversion

    return result;
}

vec4 FinalOutput(vec4 vShaderColor, float pixelFogFactor, int iPIXELFOGTYPE, int iTONEMAP_SCALE_TYPE)
{
    return FinalOutput(vShaderColor, pixelFogFactor, iPIXELFOGTYPE, iTONEMAP_SCALE_TYPE, false, 1.0);
}

float RemapValClamped(float val, float A, float B, float C, float D)
{
    float cVal = (val - A) / (B - A);
    cVal = clamp(cVal, 0.0, 1.0);

    return C + (D - C) * cVal;
}

float DepthFeathering(sampler2D DepthSampler, vec2 vScreenPos, float fProjZ, float fProjW, vec4 vDepthBlendConstants)
{
    float flFeatheredAlpha;
    float flSceneDepth = texture(DepthSampler, vScreenPos).a;	// PC uses dest alpha of the frame buffer
    float flSpriteDepth = SoftParticleDepth(fProjZ);

    flFeatheredAlpha = abs(flSceneDepth - flSpriteDepth) * vDepthBlendConstants.x;
    flFeatheredAlpha = max(smoothstep(0.75, 1.0, flSceneDepth), flFeatheredAlpha); //as the sprite approaches the edge of our compressed depth space, the math stops working. So as the sprite approaches the far depth, smoothly remove feathering.
    flFeatheredAlpha = clamp(flFeatheredAlpha, 0.0, 1.0);

    return flFeatheredAlpha;
}

#endif // COMMON_GL460_FS
