#version 460

// STATIC: "CUBEMAP"					"0..1"
// STATIC: "DIFFUSELIGHTING"			"0..1"
// STATIC: "LIGHTWARPTEXTURE"			"0..1"
// STATIC: "SELFILLUM"					"0..1"
// STATIC: "SELFILLUMFRESNEL"			"0..1"
// STATIC: "NORMALMAPALPHAENVMAPMASK"	"0..1"
// STATIC: "HALFLAMBERT"				"0..1"
// STATIC: "FLASHLIGHT"					"0..1"
// STATIC: "DETAILTEXTURE"				"0..1"
// STATIC: "DETAIL_BLEND_MODE"      	"0..6"
// STATIC: "FLASHLIGHTDEPTHFILTERMODE"	"0..2"
// STATIC: "BLENDTINTBYBASEALPHA"  "0..1"

// DYNAMIC: "NUM_LIGHTS"				"0..4"
// DYNAMIC: "AMBIENT_LIGHT"				"0..1"
// DYNAMIC: "FLASHLIGHTSHADOWS"			"0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..2"

in vec4 vs_BaseTexCoord2_TangentSpaceVertToEyeVectorXY;
in vec3 vs_LightAtten;
in vec4 vs_WorldVertToEyeVectorXYZ_TangentSpaceVertToEyeVectorZ;
in vec3 vs_WorldNormal;		// World-space normal
in vec4 vs_WorldTangent;
in vec4 vs_ProjPos;
in vec4 vs_WorldPos_ProjPosZ;
in vec3 vs_DetailTexCoord_Atten3;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_flashlight_gl460.fs"
#include "common_vertexlitgeneric_gl460.fs"

#define g_EnvmapTint_TintReplaceFactor		ps_const[0]
#define g_DiffuseModulation					ps_const[1]
#define g_EnvmapContrast_ShadowTweaks		ps_const[2]
#define g_EnvmapSaturation					ps_const[3].xyz
#define g_SelfIllumTint_and_BlendFactor		ps_const[4]
#define g_SelfIllumTint						(g_SelfIllumTint_and_BlendFactor.rgb)
#define g_DetailBlendFactor					(g_SelfIllumTint_and_BlendFactor.w)

// 11, 12 not used?
#define g_SelfIllumScaleBiasExpBrightness	ps_const[11]

#define g_ShaderControls					ps_const[12]
#define g_fWriteDepthToAlpha			g_ShaderControls.y
#define g_fWriteWaterFogToDestAlpha		g_ShaderControls.z

#define g_EyePos							ps_const[20]
#define g_FogParams							ps_const[21]

#define g_FlashlightAttenuationFactors		ps_const[22]
#define g_FlashlightPos						ps_const[23].xyz
#define g_FlashlightWorldToTexture			mat4(ps_const[24], ps_const[25], ps_const[26], ps_const[27]) // through c27

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform samplerCube EnvmapSampler;
layout(binding = 2) uniform sampler2D DetailSampler;
layout(binding = 3) uniform sampler2D BumpmapSampler;
layout(binding = 6) uniform sampler2D RandRotSampler;			// RandomRotation sampler
layout(binding = 7) uniform sampler2D FlashlightSampler;
layout(binding = 8) uniform sampler2DShadow ShadowDepthSampler;	// Flashlight shadow depth map sampler
layout(binding = 8) uniform sampler2D ShadowDepthSamplerRaw;
layout(binding = 9) uniform sampler2D DiffuseWarpSampler;		// Lighting warp sampler (1D texture for diffuse lighting modification)

void main()
{
    bool bCubemap = CUBEMAP != 0;
    bool bDiffuseLighting = DIFFUSELIGHTING != 0;
    bool bDoDiffuseWarp = LIGHTWARPTEXTURE != 0;
    bool bSelfIllum = SELFILLUM != 0;
    bool bSelfIllumFresnel = SELFILLUMFRESNEL != 0;
    bool bNormalMapAlphaEnvmapMask = NORMALMAPALPHAENVMAPMASK != 0;
    bool bHalfLambert = HALFLAMBERT != 0;
    bool bFlashlight = FLASHLIGHT != 0;
    bool bAmbientLight = AMBIENT_LIGHT != 0;
    bool bDetailTexture = DETAILTEXTURE != 0;
    bool bBlendTintByBaseAlpha = BLENDTINTBYBASEALPHA != 0;
    int nNumLights = NUM_LIGHTS;

    vec3 cAmbientCube[6] = vec3[6](ps_const[5].xyz, ps_const[6].xyz, ps_const[7].xyz,
                                   ps_const[8].xyz, ps_const[9].xyz, ps_const[10].xyz);

    // 2 registers each - 6 registers total
    PixelShaderLightInfo cLightInfo[3] = PixelShaderLightInfo[3](
        PixelShaderLightInfo(ps_const[13], ps_const[14]),
        PixelShaderLightInfo(ps_const[15], ps_const[16]),
        PixelShaderLightInfo(ps_const[17], ps_const[18]));	// through c18

    vec3 vWorldBinormal = cross(vs_WorldNormal.xyz, vs_WorldTangent.xyz) * vs_WorldTangent.w;

    // Unpack four light attenuations
    vec4 vLightAtten = vec4(vs_LightAtten, vs_DetailTexCoord_Atten3.z);

    vec4 baseColor = vec4(1.0, 1.0, 1.0, 1.0);
    baseColor = texture(BaseTextureSampler, vs_BaseTexCoord2_TangentSpaceVertToEyeVectorXY.xy);

#if DETAILTEXTURE
    vec4 detailColor = texture(DetailSampler, vs_DetailTexCoord_Atten3.xy);
    baseColor = TextureCombine(baseColor, detailColor, DETAIL_BLEND_MODE, g_DetailBlendFactor);
#endif

    float specularFactor = 1.0;
    vec4 normalTexel = texture(BumpmapSampler, vs_BaseTexCoord2_TangentSpaceVertToEyeVectorXY.xy);
    vec3 tangentSpaceNormal = normalTexel.xyz * 2.0 - 1.0;

    if (bNormalMapAlphaEnvmapMask)
        specularFactor = normalTexel.a;

    vec3 diffuseLighting = vec3(1.0, 1.0, 1.0);

    vec3 worldSpaceNormal = vec3(0.0, 0.0, 1.0);
    if (bDiffuseLighting || bFlashlight || bCubemap || bSelfIllumFresnel)
    {
        worldSpaceNormal = Vec3TangentToWorld(tangentSpaceNormal, vs_WorldNormal, vs_WorldTangent.xyz, vWorldBinormal);
        worldSpaceNormal = normalize(worldSpaceNormal);
    }

    if (bDiffuseLighting)
    {
        diffuseLighting = PixelShaderDoLighting(vs_WorldPos_ProjPosZ.xyz, worldSpaceNormal,
                vec3(0.0, 0.0, 0.0), false, bAmbientLight, vLightAtten,
                cAmbientCube, nNumLights, cLightInfo, bHalfLambert,
                false, 1.0, bDoDiffuseWarp, DiffuseWarpSampler);
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
    if (!bSelfIllum && !bBlendTintByBaseAlpha)
    {
        alpha *= baseColor.a;
    }

#if FLASHLIGHT
    if (bFlashlight)
    {
        int nShadowSampleLevel = 0;
        bool bDoShadows = false;
        vec2 vProjPos = vec2(0, 0);
// On ps_2_b, we can do shadow mapping
#if FLASHLIGHTSHADOWS
        nShadowSampleLevel = FLASHLIGHTDEPTHFILTERMODE;
        bDoShadows = FLASHLIGHTSHADOWS != 0;
        vProjPos = vs_ProjPos.xy / vs_ProjPos.w;	// Screen-space position for shadow map noise
#endif

        vec4 flashlightSpacePosition = g_FlashlightWorldToTexture * vec4(vs_WorldPos_ProjPosZ.xyz, 1.0);

        vec3 flashlightColor = DoFlashlight(g_FlashlightPos, vs_WorldPos_ProjPosZ.xyz, flashlightSpacePosition,
            worldSpaceNormal, g_FlashlightAttenuationFactors.xyz,
            g_FlashlightAttenuationFactors.w, FlashlightSampler, ShadowDepthSampler, ShadowDepthSamplerRaw,
            RandRotSampler, nShadowSampleLevel, bDoShadows, false, vProjPos, false, g_EnvmapContrast_ShadowTweaks, true);

        diffuseLighting = flashlightColor;
    }
#endif

    vec3 diffuseComponent = albedo * diffuseLighting;

#if !FLASHLIGHT
    if (bSelfIllum)
    {
#if (SELFILLUMFRESNEL == 1) // To free up the constant register...see top of file
        // This will apply a fresnel term based on the vertex normal (not the per-pixel normal!) to help fake and internal glow look
        {
            vec3 vVertexNormal = normalize(vs_WorldNormal.xyz);
            float flSelfIllumFresnel = (pow(clamp(dot(vVertexNormal.xyz, normalize(vs_WorldVertToEyeVectorXYZ_TangentSpaceVertToEyeVectorZ.xyz)), 0.0, 1.0), g_SelfIllumScaleBiasExpBrightness.z) * g_SelfIllumScaleBiasExpBrightness.x) + g_SelfIllumScaleBiasExpBrightness.y;

            vec3 selfIllumComponent = g_SelfIllumTint * albedo * g_SelfIllumScaleBiasExpBrightness.w;
            diffuseComponent = mix(diffuseComponent, selfIllumComponent, baseColor.a * clamp(flSelfIllumFresnel, 0.0, 1.0));
        }
#else
        {
            vec3 selfIllumComponent = g_SelfIllumTint * albedo;
            diffuseComponent = mix(diffuseComponent, selfIllumComponent, baseColor.a);
        }
#endif
    }
#endif

    vec3 specularLighting = vec3(0.0, 0.0, 0.0);
#if !FLASHLIGHT
    if (bCubemap)
    {
        vec3 reflectVect = CalcReflectionVectorUnnormalized(worldSpaceNormal, vs_WorldVertToEyeVectorXYZ_TangentSpaceVertToEyeVectorZ.xyz);

        specularLighting = ENV_MAP_SCALE * texture(EnvmapSampler, reflectVect).xyz;
        specularLighting *= specularFactor;
        specularLighting *= g_EnvmapTint_TintReplaceFactor.rgb;
        vec3 specularLightingSquared = specularLighting * specularLighting;
        specularLighting = mix(specularLighting, specularLightingSquared, g_EnvmapContrast_ShadowTweaks.xyz);
        vec3 greyScale = vec3(dot(specularLighting, vec3(0.299, 0.587, 0.114)));
        specularLighting = mix(greyScale, specularLighting, g_EnvmapSaturation);
    }
#endif

    vec3 result = diffuseComponent + specularLighting;

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);

#if (PIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT)
    alpha = mix(alpha, fogFactor, g_fWriteWaterFogToDestAlpha);
#endif

    fragColor = FinalOutput(vec4(result.rgb, alpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, g_fWriteDepthToAlpha != 0.0, vs_WorldPos_ProjPosZ.w);
}
