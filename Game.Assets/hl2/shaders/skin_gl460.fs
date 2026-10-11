#version 460
// STATIC: "CUBEMAP"					"0..1"
// STATIC: "SELFILLUM"					"0..1"
// STATIC: "SELFILLUMFRESNEL"			"0..1"
// STATIC: "FLASHLIGHT"					"0..1"
// STATIC: "LIGHTWARPTEXTURE"			"0..1"
// STATIC: "PHONGWARPTEXTURE"			"0..1"
// STATIC: "WRINKLEMAP"					"0..1"
// STATIC: "DETAIL_BLEND_MODE"          "0..6"
// STATIC: "DETAILTEXTURE"				"0..1"
// STATIC: "RIMLIGHT"					"0..1"
// STATIC: "FLASHLIGHTDEPTHFILTERMODE"	"0..2"
// STATIC: "FASTPATH_NOBUMP"            "0..1"
// STATIC: "BLENDTINTBYBASEALPHA"       "0..1"

// DYNAMIC: "WRITEWATERFOGTODESTALPHA"  "0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..2"
// DYNAMIC: "NUM_LIGHTS"				"0..4"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"
// DYNAMIC: "FLASHLIGHTSHADOWS"			"0..1"

in vec4 vs_BaseTexCoord;			// xy=base zw=detail
in vec3 vs_LightAtten;				// Scalar light attenuation factors for FOUR lights
in vec3 vs_WorldVertToEyeVector;
in mat3 vs_TangentSpaceTranspose;
in vec4 vs_WorldPos_Atten3;
in vec4 vs_ProjPos_WrinkleWeight;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_flashlight_gl460.fs"
#include "common_vertexlitgeneric_gl460.fs"

#define g_SelfIllumTint_and_DetailBlendFactor	ps_const[0]
#define g_SelfIllumScaleBiasExpBrightness		ps_const[3]
#define g_DiffuseModulation						ps_const[1]
#define g_EnvmapTint_ShadowTweaks				ps_const[2]		// w controls spec mask
#define g_EnvMapFresnel							ps_const[10]	// x is envmap fresnel ... w is selfillummask control
#define g_EyePos_SpecExponent					ps_const[11]
#define g_FogParams								ps_const[12]
#define g_FlashlightAttenuationFactors_RimMask	ps_const[13]	// On non-flashlight pass, x has rim mask control
#define g_FlashlightPos_RimBoost				ps_const[14]
#define g_FlashlightWorldToTexture				mat4(ps_const[15], ps_const[16], ps_const[17], ps_const[18])
#define g_FresnelSpecParams						ps_const[19]	// xyz are fresnel, w is specular boost
#define g_SpecularRimParams						ps_const[26]	// xyz are specular tint color, w is rim power

// TODO: give this a better name.  For now, I don't want to touch shader_constant_register_map.h since I don't want to trigger a recompile of everything...
#define g_ShaderControls						ps_const[27]	// x is basemap alpgha phong mask, y is 1 - blendtintbybasealpha, z is tint overlay amount, w controls "INVERTPHONGMASK"
#define g_FlashlightPos					g_FlashlightPos_RimBoost.xyz
#define	g_fRimBoost						g_FlashlightPos_RimBoost.w
#define g_FresnelRanges					g_FresnelSpecParams.xyz
#define g_SpecularBoost					g_FresnelSpecParams.w
#define g_SpecularTint					g_SpecularRimParams.xyz
#define g_RimExponent					g_SpecularRimParams.w
#define g_FlashlightAttenuationFactors	g_FlashlightAttenuationFactors_RimMask
#define g_RimMaskControl				g_FlashlightAttenuationFactors_RimMask.x
#define g_SelfIllumMaskControl			g_EnvMapFresnel.w
#define g_fBaseMapAlphaPhongMask		g_ShaderControls.x
#define g_fTintReplacementControl		g_ShaderControls.z
#define g_fInvertPhongMask				g_ShaderControls.w

layout(binding = 0) uniform sampler2D BaseTextureSampler;		// Base map, selfillum in alpha
layout(binding = 1) uniform sampler2D SpecularWarpSampler;		// Specular warp sampler (for iridescence etc)
layout(binding = 2) uniform sampler2D DiffuseWarpSampler;		// Lighting warp sampler (1D texture for diffuse lighting modification)
layout(binding = 3) uniform sampler2D NormalMapSampler;			// Normal map, specular mask in alpha
layout(binding = 4) uniform sampler2DShadow ShadowDepthSampler;	// Flashlight shadow depth map sampler
layout(binding = 4) uniform sampler2D ShadowDepthSamplerRaw;
layout(binding = 5) uniform sampler2D NormalizeRandRotSampler;	// Normalization / RandomRotation samplers
layout(binding = 6) uniform sampler2D FlashlightSampler;		// Flashlight cookie
layout(binding = 7) uniform sampler2D SpecExponentSampler;		// Specular exponent map
layout(binding = 8) uniform samplerCube EnvmapSampler;			// Cubic environment map

#if WRINKLEMAP
layout(binding = 9) uniform sampler2D WrinkleSampler;			// Compression base
layout(binding = 10) uniform sampler2D StretchSampler;			// Expansion base
layout(binding = 11) uniform sampler2D NormalWrinkleSampler;	// Compression base
layout(binding = 12) uniform sampler2D NormalStretchSampler;	// Expansion base
#endif

#if DETAILTEXTURE
layout(binding = 13) uniform sampler2D DetailSampler;			// detail texture
#endif

layout(binding = 14) uniform sampler2D SelfIllumMaskSampler;	// selfillummask

void main()
{
    bool bWrinkleMap = WRINKLEMAP != 0;
    bool bDoDiffuseWarp = LIGHTWARPTEXTURE != 0;
    bool bDoSpecularWarp = PHONGWARPTEXTURE != 0;
    bool bDoAmbientOcclusion = false;
    bool bFlashlight = FLASHLIGHT != 0;
    bool bSelfIllum = SELFILLUM != 0;
    bool bDoRimLighting = RIMLIGHT != 0;
    bool bCubemap = CUBEMAP != 0;
    bool bBlendTintByBaseAlpha = BLENDTINTBYBASEALPHA != 0;
    int nNumLights = NUM_LIGHTS;

    vec3 cAmbientCube[6] = vec3[6](ps_const[4].xyz, ps_const[5].xyz, ps_const[6].xyz,
                                   ps_const[7].xyz, ps_const[8].xyz, ps_const[9].xyz);

    // 2 registers each - 6 registers total (4th light spread across w's)
    PixelShaderLightInfo cLightInfo[3] = PixelShaderLightInfo[3](
        PixelShaderLightInfo(ps_const[20], ps_const[21]),
        PixelShaderLightInfo(ps_const[22], ps_const[23]),
        PixelShaderLightInfo(ps_const[24], ps_const[25]));

    // Unpacking for convenience
    float fWrinkleWeight = vs_ProjPos_WrinkleWeight.w;
    vec3 vProjPos = vs_ProjPos_WrinkleWeight.xyz;
    vec3 vWorldPos = vs_WorldPos_Atten3.xyz;
    float atten3 = vs_WorldPos_Atten3.w;

    vec4 vLightAtten = vec4(vs_LightAtten, atten3);

#if WRINKLEMAP
    float flWrinkleAmount = clamp(-fWrinkleWeight, 0.0, 1.0);				// One of these two is zero
    float flStretchAmount = clamp( fWrinkleWeight, 0.0, 1.0);				// while the other is in the 0..1 range

    float flTextureAmount = 1.0 - flWrinkleAmount - flStretchAmount;			// These should sum to one
#endif

    vec4 baseColor = texture(BaseTextureSampler, vs_BaseTexCoord.xy);
#if WRINKLEMAP
    vec4 wrinkleColor = texture(WrinkleSampler, vs_BaseTexCoord.xy);
    vec4 stretchColor = texture(StretchSampler, vs_BaseTexCoord.xy);

    // Apply wrinkle blend to only RGB.  Alpha comes from the base texture
    baseColor.rgb = (flTextureAmount * baseColor + flWrinkleAmount * wrinkleColor + flStretchAmount * stretchColor).rgb;
#endif

#if DETAILTEXTURE
    vec4 detailColor = texture(DetailSampler, vs_BaseTexCoord.zw);
    baseColor = TextureCombine(baseColor, detailColor, DETAIL_BLEND_MODE, g_SelfIllumTint_and_DetailBlendFactor.w);
#endif

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.xyz, vWorldPos.xyz, vProjPos.z);

    vec3 vEyeDir = normalize(vs_WorldVertToEyeVector.xyz);
    vec3 vRimAmbientCubeColor = PixelShaderAmbientLight(vEyeDir, cAmbientCube);

    vec3 worldSpaceNormal, tangentSpaceNormal;
    float fSpecMask = 1.0;
    vec4 normalTexel = texture(NormalMapSampler, vs_BaseTexCoord.xy);

#if WRINKLEMAP
    vec4 wrinkleNormal = texture(NormalWrinkleSampler, vs_BaseTexCoord.xy);
    vec4 stretchNormal = texture(NormalStretchSampler, vs_BaseTexCoord.xy);
    normalTexel = flTextureAmount * normalTexel + flWrinkleAmount * wrinkleNormal + flStretchAmount * stretchNormal;
#endif

#if (FASTPATH_NOBUMP == 0)
    tangentSpaceNormal = mix(2.0 * normalTexel.xyz - 1.0, vec3(0, 0, 1), g_fBaseMapAlphaPhongMask);
    fSpecMask = mix(normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask);
#else
    tangentSpaceNormal = vec3(0, 0, 1);
    fSpecMask = baseColor.a;
#endif

    // We need a normal if we're doing any lighting
    worldSpaceNormal = normalize(tangentSpaceNormal * vs_TangentSpaceTranspose);

    float fFresnelRanges = Fresnel(worldSpaceNormal, vEyeDir, g_FresnelRanges);
    float fRimFresnel = Fresnel4(worldSpaceNormal, vEyeDir);

    // Break down reflect so that we can share dot(worldSpaceNormal,vEyeDir) with fresnel terms
    vec3 vReflect = 2.0 * worldSpaceNormal * dot(worldSpaceNormal, vEyeDir) - vEyeDir;

    vec3 diffuseLighting = vec3(1.0, 1.0, 1.0);
    vec3 envMapColor = vec3(0.0, 0.0, 0.0);
    if (!bFlashlight)
    {
        // Summation of diffuse illumination from all local lights
        diffuseLighting = PixelShaderDoLighting(vWorldPos, worldSpaceNormal,
            vec3(0.0, 0.0, 0.0), false, true, vLightAtten,
            cAmbientCube, nNumLights, cLightInfo, true,

            // These parameters aren't passed by generic shaders:
            false, 1.0,
            bDoDiffuseWarp, DiffuseWarpSampler);

        if (bCubemap)
        {
            // Mask is either normal map alpha or base map alpha
#if (SELFILLUMFRESNEL == 1) // This is to match the 2.0 version of vertexlitgeneric
            float fEnvMapMask = mix(baseColor.a, g_fInvertPhongMask, g_EnvmapTint_ShadowTweaks.w);
#else
            float fEnvMapMask = mix(baseColor.a, fSpecMask, g_EnvmapTint_ShadowTweaks.w);
#endif

            envMapColor = (ENV_MAP_SCALE *
                           mix(1.0, fFresnelRanges, g_EnvMapFresnel.x) *
                           mix(fEnvMapMask, 1.0 - fEnvMapMask, g_fInvertPhongMask)) *
                           texture(EnvmapSampler, vReflect).xyz *
                           g_EnvmapTint_ShadowTweaks.xyz;
        }
    }

    vec3 specularLighting = vec3(0.0, 0.0, 0.0);
    vec3 rimLighting = vec3(0.0, 0.0, 0.0);

    vec3 vSpecularTint = vec3(1.0);
    float fRimMask = 0.0;
    float fSpecExp = 1.0;

#if (FASTPATH_NOBUMP == 0)
    vec4 vSpecExpMap = texture(SpecExponentSampler, vs_BaseTexCoord.xy);

    if (!bFlashlight)
    {
        fRimMask = mix(1.0, vSpecExpMap.a, g_RimMaskControl);						// Select rim mask
    }

    // If the exponent passed in as a constant is zero, use the value from the map as the exponent
    fSpecExp = (g_EyePos_SpecExponent.w >= 0.0) ? g_EyePos_SpecExponent.w : (1.0 + 149.0 * vSpecExpMap.r);

    // If constant tint is negative, tint with albedo, based upon scalar tint map
    vSpecularTint = mix(vec3(1.0, 1.0, 1.0), baseColor.rgb, vSpecExpMap.g);
    vSpecularTint = (g_SpecularTint.r >= 0.0) ? g_SpecularTint.rgb : vSpecularTint;

#else
    fSpecExp = max(g_EyePos_SpecExponent.w, 0.0);
#endif

    vec3 albedo = baseColor.rgb;

    if (!bFlashlight)
    {
        // Summation of specular from all local lights besides the flashlight
        PixelShaderDoSpecularLighting(vWorldPos, worldSpaceNormal,
            fSpecExp, vEyeDir, vLightAtten,
            nNumLights, cLightInfo, false, 1.0, bDoSpecularWarp,
            SpecularWarpSampler, fFresnelRanges, bDoRimLighting, g_RimExponent,

            // Outputs
            specularLighting, rimLighting);
    }
    else
    {
        vec4 flashlightSpacePosition = g_FlashlightWorldToTexture * vec4(vWorldPos, 1.0);

        DoSpecularFlashlight(g_FlashlightPos, vWorldPos, flashlightSpacePosition, worldSpaceNormal,
            g_FlashlightAttenuationFactors.xyz, g_FlashlightAttenuationFactors.w,
            FlashlightSampler, ShadowDepthSampler, ShadowDepthSamplerRaw, NormalizeRandRotSampler, FLASHLIGHTDEPTHFILTERMODE, FLASHLIGHTSHADOWS != 0, true, vProjPos.xy / vProjPos.z,
            fSpecExp, vEyeDir, bDoSpecularWarp, SpecularWarpSampler, fFresnelRanges, g_EnvmapTint_ShadowTweaks,

            // These two values are output
            diffuseLighting, specularLighting);
    }

    // If we didn't already apply Fresnel to specular warp, modulate the specular
    if (!bDoSpecularWarp)
        fSpecMask *= fFresnelRanges;

    // Modulate with spec mask, boost and tint
    specularLighting *= fSpecMask * g_SpecularBoost;

    if (bBlendTintByBaseAlpha)
    {
        vec3 tintedColor = albedo * g_DiffuseModulation.rgb;
        tintedColor = mix(tintedColor, g_DiffuseModulation.rgb, g_fTintReplacementControl);
        albedo = mix(albedo, tintedColor, baseColor.a);
    }
    else
    {
        albedo = albedo * g_DiffuseModulation.rgb;
    }

    vec3 diffuseComponent = albedo * diffuseLighting;
    if (bSelfIllum && !bFlashlight)
    {
#if (SELFILLUMFRESNEL == 1) // To free up the constant register...see top of file
        // This will apply a Fresnel term based on the vertex normal (not the per-pixel normal!) to help fake and internal glow look
        vec3 vVertexNormal = normalize(vec3(vs_TangentSpaceTranspose[0].z, vs_TangentSpaceTranspose[1].z, vs_TangentSpaceTranspose[2].z));
        float flSelfIllumFresnel = (pow(clamp(dot(vVertexNormal.xyz, vEyeDir.xyz), 0.0, 1.0), g_SelfIllumScaleBiasExpBrightness.z) * g_SelfIllumScaleBiasExpBrightness.x) + g_SelfIllumScaleBiasExpBrightness.y;
        diffuseComponent = mix(diffuseComponent, g_SelfIllumTint_and_DetailBlendFactor.rgb * albedo * g_SelfIllumScaleBiasExpBrightness.w, baseColor.a * clamp(flSelfIllumFresnel, 0.0, 1.0));
#else
        vec3 vSelfIllumMask = texture(SelfIllumMaskSampler, vs_BaseTexCoord.xy).xyz;
        vSelfIllumMask = mix(baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl);
        diffuseComponent = mix(diffuseComponent, g_SelfIllumTint_and_DetailBlendFactor.rgb * albedo, vSelfIllumMask);
#endif

        diffuseComponent = max(vec3(0.0), diffuseComponent);
    }

#if DETAILTEXTURE
    diffuseComponent = TextureCombinePostLighting(diffuseComponent, detailColor,
        DETAIL_BLEND_MODE, g_SelfIllumTint_and_DetailBlendFactor.w);
#endif

    if (bDoRimLighting && !bFlashlight)
    {
        float fRimMultiply = fRimMask * fRimFresnel; // both unit range: [0, 1]

        // Add in rim light modulated with tint, mask and traditional Fresnel (not using Fresnel ranges)
        rimLighting *= fRimMultiply;

        // Fold rim lighting into specular term by using the max so that we don't really add light twice...
        specularLighting = max(specularLighting, rimLighting);

        // Add in view-ray lookup from ambient cube
        specularLighting += (vRimAmbientCubeColor * g_fRimBoost) * clamp(fRimMultiply * worldSpaceNormal.z, 0.0, 1.0);
    }

    vec3 result = specularLighting * vSpecularTint + envMapColor + diffuseComponent;

#if WRITEWATERFOGTODESTALPHA && (PIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT)
    float alpha = fogFactor;
#else
    float alpha = g_DiffuseModulation.a;
    if (!bSelfIllum && !bBlendTintByBaseAlpha)
    {
        alpha = mix(baseColor.a * alpha, alpha, g_fBaseMapAlphaPhongMask);
    }
#endif

    bool bWriteDepthToAlpha = (WRITE_DEPTH_TO_DESTALPHA != 0) && (WRITEWATERFOGTODESTALPHA == 0);

    //FIXME: need to take dowaterfog into consideration
    fragColor = FinalOutput(vec4(result, alpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vProjPos.z);

}
