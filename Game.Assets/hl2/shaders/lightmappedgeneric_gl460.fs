#version 460
// STATIC: "BASETEXTURE2"				"0..1"
// STATIC: "DETAILTEXTURE"				"0..1"
// STATIC: "BUMPMAP"					"0..2"
// STATIC: "BUMPMAP2"					"0..1"
// STATIC: "BUMPMASK"					"0..1"
// STATIC: "DIFFUSEBUMPMAP"				"0..1"
// STATIC: "CUBEMAP"					"0..1"
// STATIC: "ENVMAPMASK"					"0..1"
// STATIC: "BASEALPHAENVMAPMASK"		"0..1"
// STATIC: "SELFILLUM"					"0..1"
// STATIC: "NORMALMAPALPHAENVMAPMASK"	"0..1"
// STATIC: "BASETEXTURENOENVMAP"		"0..1"
// STATIC: "BASETEXTURE2NOENVMAP"		"0..1"
// STATIC: "WARPLIGHTING"				"0..1"
// STATIC: "FANCY_BLENDING"				"0..1"
// STATIC: "MASKEDBLENDING"				"0..1"
// STATIC: "SEAMLESS"					"0..1"
// STATIC: "OUTLINE"					"0..1"
// STATIC: "SOFTEDGES"					"0..1"
// STATIC: "DETAIL_BLEND_MODE"			"0..11"
// STATIC: "NORMAL_DECODE_MODE"			"0..2"
// STATIC: "NORMALMASK_DECODE_MODE"		"0..2"

// DYNAMIC: "FASTPATH"					"0..1"
// DYNAMIC: "FASTPATHENVMAPCONTRAST"	"0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..2"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"
// DYNAMIC: "WRITEWATERFOGTODESTALPHA"	"0..1"
// DYNAMIC: "LIGHTING_PREVIEW"			"0..1"

#if SEAMLESS
in vec3 vs_SeamlessTexCoord;						// zy xz
in vec4 vs_DetailOrBumpAndEnvmapMaskTexCoord;		// envmap mask
#else
in vec2 vs_BaseTexCoord;
// detail textures and bumpmaps are mutually exclusive so that we have enough texcoords.
in vec4 vs_DetailOrBumpAndEnvmapMaskTexCoord;
#endif
centroid in vec4 vs_LightmapTexCoord1And2;
centroid in vec4 vs_LightmapTexCoord3;
in vec4 vs_WorldPos_ProjPosZ;
#if CUBEMAP || (LIGHTING_PREVIEW)
in mat3 vs_TangentSpaceTranspose;
#endif
in vec4 vs_Color;
in float vs_VertexBlendX;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

out vec4 fragColor;

#include "common_gl460.fs"
#include "common_lightmappedgeneric_gl460.fs"

#if SEAMLESS
#define USE_FAST_PATH 1
#else
#define USE_FAST_PATH FASTPATH
#endif

#define g_EnvmapTint						ps_const[0]

#if USE_FAST_PATH == 1

#	if FASTPATHENVMAPCONTRAST == 0
const vec3 g_EnvmapContrast = vec3(0.0, 0.0, 0.0);
#	else
const vec3 g_EnvmapContrast = vec3(1.0, 1.0, 1.0);
#	endif
const vec3 g_EnvmapSaturation = vec3(1.0, 1.0, 1.0);
const float g_FresnelReflection = 1.0;
const float g_OneMinusFresnelReflection = 0.0;
const vec4 g_SelfIllumTint = vec4(1.0, 1.0, 1.0, 1.0);
#   if OUTLINE
#define g_OutlineParams						ps_const[2]
#define OUTLINE_MIN_VALUE0 g_OutlineParams.x
#define OUTLINE_MIN_VALUE1 g_OutlineParams.y
#define OUTLINE_MAX_VALUE0 g_OutlineParams.z
#define OUTLINE_MAX_VALUE1 g_OutlineParams.w

#define g_OutlineColor						ps_const[3]
#define OUTLINE_COLOR g_OutlineColor

#   endif
#   if SOFTEDGES
#define g_EdgeSoftnessParms					ps_const[4]
#define SOFT_MASK_MIN g_EdgeSoftnessParms.x
#define SOFT_MASK_MAX g_EdgeSoftnessParms.y
#   endif
#else

#define g_EnvmapContrast					ps_const[2].xyz
#define g_EnvmapSaturation					ps_const[3].xyz
#define g_FresnelReflectionReg				ps_const[4]
#define g_FresnelReflection g_FresnelReflectionReg.a
#define g_OneMinusFresnelReflection g_FresnelReflectionReg.b
#define g_SelfIllumTint						ps_const[7]
#endif

#define g_DetailTint_and_BlendFactor		ps_const[8]
#define g_DetailTint (g_DetailTint_and_BlendFactor.rgb)
#define g_DetailBlendFactor (g_DetailTint_and_BlendFactor.w)

#define g_EyePos							ps_const[10].xyz
#define g_FogParams							ps_const[11]
#define g_TintValuesAndLightmapScale		ps_const[12]

#define g_flAlpha2 g_TintValuesAndLightmapScale.w

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D LightmapSampler;
layout(binding = 2) uniform samplerCube EnvmapSampler;
#if FANCY_BLENDING
layout(binding = 3) uniform sampler2D BlendModulationSampler;
#endif

#if DETAILTEXTURE
layout(binding = 12) uniform sampler2D DetailSampler;
#endif

layout(binding = 4) uniform sampler2D BumpmapSampler;
#if NORMAL_DECODE_MODE == NORM_DECODE_ATI2N_ALPHA
layout(binding = 9) uniform sampler2D AlphaMapSampler;	// alpha
#else
#define AlphaMapSampler		BumpmapSampler
#endif

#if BUMPMAP2 == 1
layout(binding = 5) uniform sampler2D BumpmapSampler2;
#if NORMAL_DECODE_MODE == NORM_DECODE_ATI2N_ALPHA
layout(binding = 10) uniform sampler2D AlphaMapSampler2;	// alpha
#else
#define AlphaMapSampler2		BumpmapSampler2
#endif
#else
layout(binding = 5) uniform sampler2D EnvmapMaskSampler;
#endif


#if WARPLIGHTING
layout(binding = 6) uniform sampler2D WarpLightingSampler;
#endif
layout(binding = 7) uniform sampler2D BaseTextureSampler2;

#if BUMPMASK == 1
layout(binding = 8) uniform sampler2D BumpMaskSampler;
#if NORMALMASK_DECODE_MODE == NORM_DECODE_ATI2N_ALPHA
layout(binding = 11) uniform sampler2D AlphaMaskSampler;	// alpha
#else
#define AlphaMaskSampler		BumpMaskSampler
#endif
#endif

void main()
{
    bool bBaseTexture2 = BASETEXTURE2 != 0;
    bool bDetailTexture = DETAILTEXTURE != 0;
    bool bBumpmap = BUMPMAP != 0;
    bool bDiffuseBumpmap = DIFFUSEBUMPMAP != 0;
    bool bCubemap = CUBEMAP != 0;
    bool bEnvmapMask = ENVMAPMASK != 0;
    bool bBaseAlphaEnvmapMask = BASEALPHAENVMAPMASK != 0;
    bool bSelfIllum = SELFILLUM != 0;
    bool bNormalMapAlphaEnvmapMask = NORMALMAPALPHAENVMAPMASK != 0;
    bool bBaseTextureNoEnvmap = BASETEXTURENOENVMAP != 0;
    bool bBaseTexture2NoEnvmap = BASETEXTURE2NOENVMAP != 0;

    vec4 baseColor = vec4(0.0);
    vec4 baseColor2 = vec4(0.0);
    vec4 vNormal = vec4(0, 0, 1, 1);
    vec3 baseTexCoords = vec3(0, 0, 0);

#if SEAMLESS
    baseTexCoords = vs_SeamlessTexCoord.xyz;
#else
    baseTexCoords.xy = vs_BaseTexCoord.xy;
#endif

    GetBaseTextureAndNormal(BaseTextureSampler, BaseTextureSampler2, BumpmapSampler, bBaseTexture2, bBumpmap || bNormalMapAlphaEnvmapMask,
        baseTexCoords, vs_Color.rgb, baseColor, baseColor2, vNormal);

#if BUMPMAP == 1	// not ssbump
    vNormal.xyz = vNormal.xyz * 2.0 - 1.0;					// make signed if we're not ssbump
#endif

    vec3 lightmapColor1 = vec3(1.0, 1.0, 1.0);
    vec3 lightmapColor2 = vec3(1.0, 1.0, 1.0);
    vec3 lightmapColor3 = vec3(1.0, 1.0, 1.0);
#if LIGHTING_PREVIEW == 0
    if (bBumpmap && bDiffuseBumpmap)
    {
        vec2 bumpCoord1;
        vec2 bumpCoord2;
        vec2 bumpCoord3;
        ComputeBumpedLightmapCoordinates(vs_LightmapTexCoord1And2, vs_LightmapTexCoord3.xy,
            bumpCoord1, bumpCoord2, bumpCoord3);

        lightmapColor1 = LightMapSample(LightmapSampler, bumpCoord1);
        lightmapColor2 = LightMapSample(LightmapSampler, bumpCoord2);
        lightmapColor3 = LightMapSample(LightmapSampler, bumpCoord3);
    }
    else
    {
        vec2 bumpCoord1 = ComputeLightmapCoordinates(vs_LightmapTexCoord1And2, vs_LightmapTexCoord3.xy);
        lightmapColor1 = LightMapSample(LightmapSampler, bumpCoord1);
    }
#endif

    #if ( DETAILTEXTURE == 1 )
        vec2 detailTexCoord = vs_DetailOrBumpAndEnvmapMaskTexCoord.xy;
        vec2 bumpmapTexCoord = vs_BaseTexCoord.xy;
    #elif ( BUMPMASK == 1 )
        vec2 detailTexCoord = vec2(0.0);
        vec2 bumpmapTexCoord = vs_DetailOrBumpAndEnvmapMaskTexCoord.xy;
        vec2 bumpmap2TexCoord = vs_DetailOrBumpAndEnvmapMaskTexCoord.wz;
    #else
        vec2 detailTexCoord = vec2(0.0);
        vec2 bumpmapTexCoord = vs_DetailOrBumpAndEnvmapMaskTexCoord.xy;
    #endif

    vec2 envmapMaskTexCoord = vs_DetailOrBumpAndEnvmapMaskTexCoord.wz;

    vec4 detailColor = vec4(1.0, 1.0, 1.0, 1.0);
#if DETAILTEXTURE

    detailColor = vec4(g_DetailTint, 1.0) * texture(DetailSampler, detailTexCoord);

#endif

#if ( OUTLINE || SOFTEDGES )
    float distAlphaMask = baseColor.a;

#   if OUTLINE
    if ((distAlphaMask >= OUTLINE_MIN_VALUE0) &&
        (distAlphaMask <= OUTLINE_MAX_VALUE1))
    {
        float oFactor = 1.0;
        if (distAlphaMask <= OUTLINE_MIN_VALUE1)
        {
            oFactor = smoothstep(OUTLINE_MIN_VALUE0, OUTLINE_MIN_VALUE1, distAlphaMask);
        }
        else
        {
            oFactor = smoothstep(OUTLINE_MAX_VALUE1, OUTLINE_MAX_VALUE0, distAlphaMask);
        }
        baseColor = mix(baseColor, OUTLINE_COLOR, oFactor);
    }
#   endif
#   if SOFTEDGES
    baseColor.a *= smoothstep(SOFT_MASK_MAX, SOFT_MASK_MIN, distAlphaMask);
#   else
    baseColor.a *= float(distAlphaMask >= 0.5);
#   endif
#endif

    float blendedAlpha = baseColor.a;

#if MASKEDBLENDING
    float blendfactor = 0.5;
#else
    float blendfactor = vs_VertexBlendX;
#endif

    if (bBaseTexture2)
    {
#if (SELFILLUM == 0) && (PIXELFOGTYPE != PIXEL_FOG_TYPE_HEIGHT) && (FANCY_BLENDING)
        vec4 modt = texture(BlendModulationSampler, vs_LightmapTexCoord3.zw);
#if MASKEDBLENDING
        // FXC is unable to optimize this, despite blendfactor=0.5 above
        //float minb=modt.g-modt.r;
        //float maxb=modt.g+modt.r;
        //blendfactor=smoothstep(minb,maxb,blendfactor);
        blendfactor = modt.g;
#else
        float minb = clamp(modt.g - modt.r, 0.0, 1.0);
        float maxb = clamp(modt.g + modt.r, 0.0, 1.0);
        blendfactor = smoothstep(minb, maxb, blendfactor);
#endif
#endif
        baseColor.rgb = mix(baseColor.rgb, baseColor2.rgb, blendfactor);
        blendedAlpha = mix(baseColor.a, baseColor2.a, blendfactor);
    }

    vec3 specularFactor = vec3(1.0);
    vec4 vNormalMask = vec4(0, 0, 1, 1);
    if (bBumpmap)
    {
        if (bBaseTextureNoEnvmap)
        {
            vNormal.a = 0.0;
        }

#if ( BUMPMAP2 == 1 )
        {
    #if ( BUMPMASK == 1 )
            vec2 b2TexCoord = bumpmap2TexCoord;
    #else
            vec2 b2TexCoord = bumpmapTexCoord;
    #endif

            vec4 vNormal2;
            if (BUMPMAP == 2)
                vNormal2 = texture(BumpmapSampler2, b2TexCoord);
            else
                vNormal2 = DecompressNormal(BumpmapSampler2, b2TexCoord, NORMAL_DECODE_MODE, AlphaMapSampler2);		// Bump 2 coords

            if (bBaseTexture2NoEnvmap)
            {
                vNormal2.a = 0.0;
            }

    #if ( BUMPMASK == 1 )
            vec3 vNormal1 = DecompressNormal(BumpmapSampler, vs_DetailOrBumpAndEnvmapMaskTexCoord.xy, NORMALMASK_DECODE_MODE, AlphaMapSampler).xyz;

            vNormal.xyz = normalize(vNormal1.xyz + vNormal2.xyz);

            // Third normal map...same coords as base
            vNormalMask = DecompressNormal(BumpMaskSampler, vs_BaseTexCoord.xy, NORMALMASK_DECODE_MODE, AlphaMaskSampler);

            vNormal.xyz = mix(vNormalMask.xyz, vNormal.xyz, vNormalMask.a);		// Mask out normals from vNormal
            specularFactor = vec3(vNormalMask.a);
    #else // BUMPMASK == 0
            if (FANCY_BLENDING != 0 && bNormalMapAlphaEnvmapMask)
            {
                vNormal = mix(vNormal, vNormal2, blendfactor);
            }
            else
            {
                vNormal.xyz = mix(vNormal.xyz, vNormal2.xyz, blendfactor);
            }

    #endif

        }

#endif // BUMPMAP2 == 1

        if (bNormalMapAlphaEnvmapMask)
        {
            specularFactor *= vNormal.a;
        }
    }
    else if (bNormalMapAlphaEnvmapMask)
    {
        specularFactor *= vNormal.a;
    }

#if ( BUMPMAP2 == 0 )
    if (bEnvmapMask)
    {
        specularFactor *= texture(EnvmapMaskSampler, envmapMaskTexCoord).xyz;
    }
#endif

    if (bBaseAlphaEnvmapMask)
    {
        specularFactor *= 1.0 - blendedAlpha; // Reversing alpha blows!
    }
    vec4 albedo = vec4(1.0, 1.0, 1.0, 1.0);
    float alpha = 1.0;
    albedo *= baseColor;
    if (!bBaseAlphaEnvmapMask && !bSelfIllum)
    {
        alpha *= baseColor.a;
    }

    if (bDetailTexture)
    {
        albedo = TextureCombine(albedo, detailColor, DETAIL_BLEND_MODE, g_DetailBlendFactor);
    }

    // The vertex color contains the modulation color + vertex color combined
#if ( SEAMLESS == 0 )
    albedo.xyz *= vs_Color.rgb;
#endif
    alpha *= vs_Color.a * g_flAlpha2; // not sure about this one

    // Save this off for single-pass flashlight, since we'll still need the SSBump vector, not a real normal
    vec3 vSSBumpVector = vNormal.xyz;

    vec3 diffuseLighting;
    if (bBumpmap && bDiffuseBumpmap)
    {

// ssbump
#if ( BUMPMAP == 2 )
        diffuseLighting = vNormal.x * lightmapColor1 +
                          vNormal.y * lightmapColor2 +
                          vNormal.z * lightmapColor3;
        diffuseLighting *= g_TintValuesAndLightmapScale.rgb;

        // now, calculate vNormal for reflection purposes. if vNormal isn't needed, hopefully
        // the compiler will eliminate these calculations
        vNormal.xyz = normalize(bumpBasis[0] * vNormal.x + bumpBasis[1] * vNormal.y + bumpBasis[2] * vNormal.z);
#else
        vec3 dp;
        dp.x = clamp(dot(vNormal.xyz, bumpBasis[0]), 0.0, 1.0);
        dp.y = clamp(dot(vNormal.xyz, bumpBasis[1]), 0.0, 1.0);
        dp.z = clamp(dot(vNormal.xyz, bumpBasis[2]), 0.0, 1.0);
        dp *= dp;

#if ( DETAIL_BLEND_MODE == TCOMBINE_SSBUMP_BUMP )
        dp *= 2.0 * detailColor.rgb;
#endif
        diffuseLighting = dp.x * lightmapColor1 +
                          dp.y * lightmapColor2 +
                          dp.z * lightmapColor3;
        float sum = dot(dp, vec3(1.0, 1.0, 1.0));
        diffuseLighting *= g_TintValuesAndLightmapScale.rgb / sum;
#endif
    }
    else
    {
        diffuseLighting = lightmapColor1 * g_TintValuesAndLightmapScale.rgb;
    }

#if WARPLIGHTING && ( SEAMLESS == 0 )
    float len = 0.5 * length(diffuseLighting);
    // FIXME: 8-bit lookup textures like this need a "nice filtering" VTF option, which converts
    //        them to 16-bit on load or does filtering in the shader (since most hardware - 360
    //        included - interpolates 8-bit textures at 8-bit precision, which causes banding)
    diffuseLighting *= 2.0 * texture(WarpLightingSampler, vec2(len, 0)).rgb;
#endif

#if CUBEMAP || LIGHTING_PREVIEW
    vec3 worldSpaceNormal = vs_TangentSpaceTranspose * vNormal.xyz;
#endif

    vec3 diffuseComponent = albedo.xyz * diffuseLighting;

    if (bSelfIllum)
    {
        vec3 selfIllumComponent = g_SelfIllumTint.xyz * albedo.xyz;
        diffuseComponent = mix(diffuseComponent, selfIllumComponent, baseColor.a);
    }

    vec3 specularLighting = vec3(0.0, 0.0, 0.0);
#if CUBEMAP
    if (bCubemap)
    {
        vec3 worldVertToEyeVector = g_EyePos - vs_WorldPos_ProjPosZ.xyz;
        vec3 reflectVect = CalcReflectionVectorUnnormalized(worldSpaceNormal, worldVertToEyeVector);

        // Calc Fresnel factor
        vec3 eyeVect = normalize(worldVertToEyeVector);
        float fresnel = 1.0 - dot(worldSpaceNormal, eyeVect);
        fresnel = pow(fresnel, 5.0);
        fresnel = fresnel * g_OneMinusFresnelReflection + g_FresnelReflection;

        specularLighting = ENV_MAP_SCALE * texture(EnvmapSampler, reflectVect).rgb;
        specularLighting *= specularFactor;

        specularLighting *= g_EnvmapTint.rgb;
#if FANCY_BLENDING == 0
        vec3 specularLightingSquared = specularLighting * specularLighting;
        specularLighting = mix(specularLighting, specularLightingSquared, g_EnvmapContrast);
        vec3 greyScale = vec3(dot(specularLighting, vec3(0.299, 0.587, 0.114)));
        specularLighting = mix(greyScale, specularLighting, g_EnvmapSaturation);
#endif
        specularLighting *= fresnel;
    }
#endif

    vec3 result = diffuseComponent + specularLighting;

#if LIGHTING_PREVIEW
    worldSpaceNormal = vs_TangentSpaceTranspose * vNormal.xyz;
    float dotprod = 0.7 + 0.25 * dot(worldSpaceNormal, normalize(vec3(1, 2, -0.5)));
    fragColor = FinalOutput(vec4(dotprod * albedo.xyz, alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#else // == end LIGHTING_PREVIEW ==

    bool bWriteDepthToAlpha = false;

    // ps_2_b and beyond
    bWriteDepthToAlpha = (WRITE_DEPTH_TO_DESTALPHA != 0) && (WRITEWATERFOGTODESTALPHA == 0);

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);

#if WRITEWATERFOGTODESTALPHA && (PIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT)
    alpha = fogFactor;
#endif

    if(isAlphaTesting){
        switch(alphaTestFunc){
            case 0: discard; break;
            case 1: if(alpha >= alphaTestRef){ discard; } break;
            case 2: if(alpha != alphaTestRef){ discard; } break;
            case 3: if(alpha > alphaTestRef){ discard; } break;
            case 4: if(alpha <= alphaTestRef){ discard; } break;
            case 5: if(alpha == alphaTestRef){ discard; } break;
            case 6: if(alpha < alphaTestRef){ discard; } break;
        }
    }

    fragColor = FinalOutput(vec4(result.rgb, alpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vs_WorldPos_ProjPosZ.w);

#endif
}
