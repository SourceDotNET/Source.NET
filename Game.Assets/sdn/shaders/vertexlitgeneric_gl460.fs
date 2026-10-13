#version 460
// STATIC: "DETAILTEXTURE"				"0..1"
// STATIC: "CUBEMAP"					"0..1"
// STATIC: "DIFFUSELIGHTING"			"0..1"
// STATIC: "ENVMAPMASK"					"0..1"
// STATIC: "BASEALPHAENVMAPMASK"		"0..1"
// STATIC: "SELFILLUM"					"0..1"
// STATIC: "VERTEXCOLOR"				"0..1"
// STATIC: "FLASHLIGHT"					"0..1"
// STATIC: "SELFILLUM_ENVMAPMASK_ALPHA" "0..1"
// STATIC: "DETAIL_BLEND_MODE"          "0..9"
// STATIC: "SEAMLESS_BASE"              "0..1"
// STATIC: "SEAMLESS_DETAIL"            "0..1"
// STATIC: "DISTANCEALPHA"              "0..1"
// STATIC: "DISTANCEALPHAFROMDETAIL"    "0..1"
// STATIC: "SOFT_MASK"                  "0..1"
// STATIC: "OUTLINE"                    "0..1"
// STATIC: "OUTER_GLOW"                 "0..1"
// STATIC: "FLASHLIGHTDEPTHFILTERMODE"	"0..2"
// STATIC: "DEPTHBLEND"					"0..1"
// STATIC: "BLENDTINTBYBASEALPHA"       "0..1"
// STATIC: "SRGB_INPUT_ADAPTER"			"0..1"
// STATIC: "CUBEMAP_SPHERE_LEGACY"		"0..1"

// DYNAMIC: "LIGHTING_PREVIEW"          "0..2"
// DYNAMIC: "FLASHLIGHTSHADOWS"			"0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..2"

#if SEAMLESS_BASE
in vec3 vs_SeamlessTexCoord;
#define i_baseTexCoord vs_SeamlessTexCoord
#else
in vec2 vs_BaseTexCoord;
#define i_baseTexCoord vs_BaseTexCoord
#endif
#if SEAMLESS_DETAIL
in vec3 vs_SeamlessDetailTexCoord;
#define i_detailTexCoord vs_SeamlessDetailTexCoord
#else
in vec2 vs_DetailTexCoord;
#define i_detailTexCoord vs_DetailTexCoord
#endif
in vec4 vs_Color;
#if CUBEMAP
in vec3 vs_WorldVertToEyeVector;
#endif
in vec3 vs_WorldSpaceNormal;
in vec4 vs_ProjPos;
in vec4 vs_WorldPos_ProjPosZ;
#if SEAMLESS_DETAIL || SEAMLESS_BASE
in vec3 vs_SeamlessWeights;
#endif

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

out vec4 fragColor;

#include "common_flashlight_gl460.fs"
#include "common_vertexlitgeneric_gl460.fs"

#define g_EnvmapTint_TintReplaceFactor		ps_const[0]
#define g_DiffuseModulation					ps_const[1]
#define g_EnvmapContrast_ShadowTweaks		ps_const[2]
#define g_EnvmapSaturation_SelfIllumMask	ps_const[3]
#define g_SelfIllumTint_and_BlendFactor		ps_const[4]

#define g_ShaderControls					ps_const[12]
#define g_DepthFeatheringConstants			ps_const[13]

#define g_EyePos							ps_const[20]
#define g_FogParams							ps_const[21]

#define g_SelfIllumTint				g_SelfIllumTint_and_BlendFactor.xyz
#define g_DetailBlendFactor			g_SelfIllumTint_and_BlendFactor.w
#define g_EnvmapSaturation			g_EnvmapSaturation_SelfIllumMask.xyz
#define g_SelfIllumMaskControl		g_EnvmapSaturation_SelfIllumMask.w

#define g_FlashlightAttenuationFactors		ps_const[22]
#define g_FlashlightPos						ps_const[23].xyz
#define g_FlashlightWorldToTexture			mat4(ps_const[24], ps_const[25], ps_const[26], ps_const[27]) // through c27

#define g_GlowParameters			ps_const[5]
#define g_GlowColor					ps_const[6]
#define GLOW_UV_OFFSET				g_GlowParameters.xy
#define OUTER_GLOW_MIN_DVALUE		g_GlowParameters.z
#define OUTER_GLOW_MAX_DVALUE		g_GlowParameters.w
#define OUTER_GLOW_COLOR			g_GlowColor

#define g_fWriteDepthToAlpha			g_ShaderControls.y
#define g_fWriteWaterFogToDestAlpha		g_ShaderControls.z
#define g_fVertexAlpha					g_ShaderControls.w

#define g_DistanceAlphaParams		ps_const[7]
#define SOFT_MASK_MAX				g_DistanceAlphaParams.x
#define SOFT_MASK_MIN				g_DistanceAlphaParams.y

#define g_OutlineColor				ps_const[8]

// these are ordered this way for optimal ps20 swizzling
#define g_OutlineParams				ps_const[9]

#if DETAILTEXTURE
#define g_DetailTint				ps_const[10].rgb
#endif

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform samplerCube EnvmapSampler;
layout(binding = 2) uniform sampler2D DetailSampler;
layout(binding = 4) uniform sampler2D EnvmapMaskSampler;
layout(binding = 6) uniform sampler2D RandRotSampler;			// RandomRotation sampler
layout(binding = 7) uniform sampler2D FlashlightSampler;
layout(binding = 8) uniform sampler2DShadow ShadowDepthSampler;	// Flashlight shadow depth map sampler
layout(binding = 8) uniform sampler2D ShadowDepthSamplerRaw;
layout(binding = 10) uniform sampler2D DepthSampler;			//depth buffer sampler for depth blending
layout(binding = 11) uniform sampler2D SelfIllumMaskSampler;	// selfillummask

void main()
{
    bool bDetailTexture = DETAILTEXTURE != 0;
    bool bCubemap = CUBEMAP != 0;
    bool bDiffuseLighting = DIFFUSELIGHTING != 0;
    bool bHasNormal = bCubemap || bDiffuseLighting;
    bool bEnvmapMask = ENVMAPMASK != 0;
    bool bBaseAlphaEnvmapMask = BASEALPHAENVMAPMASK != 0;
    bool bSelfIllum = SELFILLUM != 0;
    bool bVertexColor = VERTEXCOLOR != 0;
    bool bFlashlight = FLASHLIGHT != 0;
    bool bBlendTintByBaseAlpha = BLENDTINTBYBASEALPHA != 0;

    vec4 baseColor = vec4(1.0, 1.0, 1.0, 1.0);
#if SEAMLESS_BASE
    baseColor =
        vs_SeamlessWeights.x * texture(BaseTextureSampler, i_baseTexCoord.yz) +
        vs_SeamlessWeights.y * texture(BaseTextureSampler, i_baseTexCoord.zx) +
        vs_SeamlessWeights.z * texture(BaseTextureSampler, i_baseTexCoord.xy);
#else
    baseColor = texture(BaseTextureSampler, i_baseTexCoord.xy);

#if SRGB_INPUT_ADAPTER
    baseColor.rgb = GammaToLinear(baseColor.rgb);
#endif

#endif // !SEAMLESS_BASE

    if(isAlphaTesting){
        switch(alphaTestFunc){
            case 0: discard; break;
            case 1: if(baseColor.a >= alphaTestRef){ discard; } break;
            case 2: if(baseColor.a != alphaTestRef){ discard; } break;
            case 3: if(baseColor.a > alphaTestRef){ discard; } break;
            case 4: if(baseColor.a <= alphaTestRef){ discard; } break;
            case 5: if(baseColor.a == alphaTestRef){ discard; } break;
            case 6: if(baseColor.a < alphaTestRef){ discard; } break;
        }
    }

#if DISTANCEALPHA
    float distAlphaMask = baseColor.a;
#endif

#if DETAILTEXTURE
#if SEAMLESS_DETAIL
    vec4 detailColor =
        vs_SeamlessWeights.x * texture(DetailSampler, i_detailTexCoord.yz) +
        vs_SeamlessWeights.y * texture(DetailSampler, i_detailTexCoord.zx) +
        vs_SeamlessWeights.z * texture(DetailSampler, i_detailTexCoord.xy);
#else
    vec4 detailColor = texture(DetailSampler, i_detailTexCoord.xy);
#endif
    detailColor.rgb *= g_DetailTint;

#if DISTANCEALPHA && (DISTANCEALPHAFROMDETAIL == 1)
    distAlphaMask = detailColor.a;
    detailColor.a = 1.0;									// make tcombine treat as 1.0
#endif
    baseColor =
        TextureCombine(baseColor, detailColor, DETAIL_BLEND_MODE, g_DetailBlendFactor);
#endif

#if DISTANCEALPHA
    // now, do all distance alpha effects
#if OUTLINE
    {
        vec4 oFactors = smoothstep(g_OutlineParams.xyzw, g_OutlineParams.wzyx, vec4(distAlphaMask));
        baseColor = mix(baseColor, g_OutlineColor, oFactors.x * oFactors.y);
    }
#endif

    float mskUsed;
#if SOFT_MASK
    {
        mskUsed = smoothstep(SOFT_MASK_MIN, SOFT_MASK_MAX, distAlphaMask);
        baseColor.a *= mskUsed;
    }
#else
    {
        mskUsed = distAlphaMask >= 0.5 ? 1.0 : 0.0;
#if DETAILTEXTURE
        baseColor.a *= mskUsed;
#else
        baseColor.a = mskUsed;
#endif
    }
#endif

#if OUTER_GLOW
    {
#if DISTANCEALPHAFROMDETAIL
        vec4 glowTexel = texture(DetailSampler, i_detailTexCoord.xy + GLOW_UV_OFFSET);
#else
        vec4 glowTexel = texture(BaseTextureSampler, i_baseTexCoord.xy + GLOW_UV_OFFSET);
#endif
        vec4 glowc = OUTER_GLOW_COLOR * smoothstep(OUTER_GLOW_MIN_DVALUE, OUTER_GLOW_MAX_DVALUE, glowTexel.a);
        baseColor = mix(glowc, baseColor, mskUsed);
    }
#endif

#endif  // DISTANCEALPHA

    vec3 specularFactor = vec3(1.0);
    vec4 envmapMaskTexel = vec4(0.0);
    if (bEnvmapMask)
    {
        envmapMaskTexel = texture(EnvmapMaskSampler, i_baseTexCoord.xy);
        specularFactor *= envmapMaskTexel.xyz;
    }

    if (bBaseAlphaEnvmapMask)
    {
        specularFactor *= 1.0 - baseColor.a; // this blows!
    }

    vec3 diffuseLighting = vec3(1.0, 1.0, 1.0);
    if (bDiffuseLighting || bVertexColor && !(bVertexColor && bDiffuseLighting))
    {
        diffuseLighting = vs_Color.rgb;
    }

    vec3 albedo = baseColor.rgb;

    if (bBlendTintByBaseAlpha)
    {
        vec3 tintedColor = albedo * g_DiffuseModulation.rgb;
        tintedColor = mix(tintedColor, g_DiffuseModulation.rgb, g_EnvmapTint_TintReplaceFactor.w);
        albedo = mix(albedo, tintedColor, baseColor.a);
    }
    else
    {
        albedo = albedo * g_DiffuseModulation.rgb;
    }

    float alpha = g_DiffuseModulation.a;
    if (!bBaseAlphaEnvmapMask && !bSelfIllum && !bBlendTintByBaseAlpha)
    {
        alpha *= baseColor.a;
    }

    if (bFlashlight)
    {
        int nShadowSampleLevel = 0;
        bool bDoShadows = false;
// On ps_2_b, we can do shadow mapping
#if FLASHLIGHTSHADOWS
        nShadowSampleLevel = FLASHLIGHTDEPTHFILTERMODE;
        bDoShadows = true;
#endif

        vec4 flashlightSpacePosition = g_FlashlightWorldToTexture * vec4(vs_WorldPos_ProjPosZ.xyz, 1.0);

        // We want the N.L to happen on the flashlight pass, but can't afford it on ps20
        bool bUseWorldNormal = true;
        vec3 flashlightColor = DoFlashlight(g_FlashlightPos, vs_WorldPos_ProjPosZ.xyz, flashlightSpacePosition,
            vs_WorldSpaceNormal, g_FlashlightAttenuationFactors.xyz,
            g_FlashlightAttenuationFactors.w, FlashlightSampler, ShadowDepthSampler, ShadowDepthSamplerRaw,
            RandRotSampler, nShadowSampleLevel, bDoShadows, false, vs_ProjPos.xy / vs_ProjPos.w, false, g_EnvmapContrast_ShadowTweaks, bUseWorldNormal);

        diffuseLighting = flashlightColor;
    }

    if (bVertexColor && bDiffuseLighting)
    {
        albedo *= vs_Color.rgb;
    }

    alpha = mix(alpha, alpha * vs_Color.a, g_fVertexAlpha);

    vec3 diffuseComponent = albedo * diffuseLighting;

#if DETAILTEXTURE
    diffuseComponent =
        TextureCombinePostLighting(diffuseComponent, detailColor, DETAIL_BLEND_MODE, g_DetailBlendFactor);
#endif

    vec3 specularLighting = vec3(0.0, 0.0, 0.0);

#if !FLASHLIGHT
#if SELFILLUM_ENVMAPMASK_ALPHA
    // range of alpha:
    // 0 - 0.125 = lerp(diffuse,selfillum,alpha*8)
    // 0.125-1.0 = selfillum*(1+alpha-0.125)*8 (over bright glows)
    {
        vec3 selfIllumComponent = g_SelfIllumTint * albedo;
        float Adj_Alpha = 8.0 * envmapMaskTexel.a;
        diffuseComponent = (max(0.0, 1.0 - Adj_Alpha) * diffuseComponent) + Adj_Alpha * selfIllumComponent;
    }
#else
    if (bSelfIllum)
    {
        vec3 vSelfIllumMask = texture(SelfIllumMaskSampler, i_baseTexCoord.xy).xyz;
        vSelfIllumMask = mix(baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl);
        diffuseComponent = mix(diffuseComponent, g_SelfIllumTint * albedo, vSelfIllumMask);
    }
#endif

#if CUBEMAP
    if (bCubemap)
    {
#if CUBEMAP_SPHERE_LEGACY
        vec3 reflectVect = normalize(CalcReflectionVectorUnnormalized(vs_WorldSpaceNormal, vs_WorldVertToEyeVector.xyz));

        specularLighting = 0.5 * texture(EnvmapSampler, reflectVect).xyz * g_DiffuseModulation.rgb * diffuseLighting;
#else
        vec3 reflectVect = CalcReflectionVectorUnnormalized(vs_WorldSpaceNormal, vs_WorldVertToEyeVector.xyz);

        specularLighting = ENV_MAP_SCALE * texture(EnvmapSampler, reflectVect).xyz;
        specularLighting *= specularFactor;
        specularLighting *= g_EnvmapTint_TintReplaceFactor.rgb;
        vec3 specularLightingSquared = specularLighting * specularLighting;
        specularLighting = mix(specularLighting, specularLightingSquared, g_EnvmapContrast_ShadowTweaks.xyz);
        vec3 greyScale = vec3(dot(specularLighting, vec3(0.299, 0.587, 0.114)));
        specularLighting = mix(greyScale, specularLighting, g_EnvmapSaturation);
#endif
    }
#endif
#endif

    vec3 result = diffuseComponent + specularLighting;

#if LIGHTING_PREVIEW == 1
    float dotprod = 0.7 + 0.25 * dot(vs_WorldSpaceNormal, normalize(vec3(1, 2, -0.5)));
    fragColor = FinalOutput(vec4(dotprod * albedo.xyz, alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR);
#else

#if (DEPTHBLEND == 1)
    {
        vec2 vScreenPos;
        vScreenPos.x = vs_ProjPos.x;
        vScreenPos.y = -vs_ProjPos.y;
        vScreenPos = (vScreenPos + vs_ProjPos.w) * 0.5;
        alpha *= DepthFeathering(DepthSampler, vScreenPos / vs_ProjPos.w, vs_ProjPos.w - vs_ProjPos.z, vs_ProjPos.w, g_DepthFeatheringConstants);
    }
#endif

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos.xyz, vs_WorldPos_ProjPosZ.xyz, vs_ProjPos.z);
#if (PIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT)
    alpha = mix(alpha, fogFactor, g_fWriteWaterFogToDestAlpha);
#endif
    fragColor = FinalOutput(vec4(result.rgb, alpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, g_fWriteDepthToAlpha != 0.0, vs_ProjPos.z);

#endif
}
