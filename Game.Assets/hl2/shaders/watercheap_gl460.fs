#version 460
// STATIC: "MULTITEXTURE"				"0..1"
// STATIC: "FRESNEL"					"0..1"
// STATIC: "BLEND"						"0..1"
// STATIC: "REFRACTALPHA"				"0..1"
// STATIC: "HDRTYPE"					"0..2"

// DYNAMIC: "HDRENABLED"				"0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..1"

in vec2 vs_NormalMapTexCoord;
in vec3 vs_WorldVertToEyeVector;
in mat3 vs_TangentSpaceTranspose;
in vec4 vs_Refract_W_ProjZ;
#if MULTITEXTURE
in vec4 vs_ExtraBumpTexCoord;
#endif

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform samplerCube EnvmapSampler;
layout(binding = 1) uniform sampler2D NormalMapSampler;
#if REFRACTALPHA
layout(binding = 2) uniform sampler2D RefractSampler;
#endif

#define g_WaterFogColor				ps_const[0].rgb
#define g_CheapWaterParams			ps_const[1]
#define g_ReflectTint				ps_const[2]
#define g_PixelFogParams			ps_const[3]

#define g_CheapWaterDeltaRecip		g_CheapWaterParams.z
#define g_CheapWaterStartDivDelta	g_CheapWaterParams.w

void main()
{
    bool bBlend = BLEND != 0;

#if MULTITEXTURE
    vec3 vNormal  = texture(NormalMapSampler, vs_NormalMapTexCoord).xyz;
    vec3 vNormal1 = texture(NormalMapSampler, vs_ExtraBumpTexCoord.xy).xyz;
    vec3 vNormal2 = texture(NormalMapSampler, vs_ExtraBumpTexCoord.zw).xyz;
    vNormal = 0.33 * (vNormal + vNormal1 + vNormal2);

    vNormal = 2.0 * vNormal - 1.0;

#else
    vec3 vNormal = DecompressNormal(NormalMapSampler, vs_NormalMapTexCoord, NORM_DECODE_NONE).xyz;
#endif

    vec3 worldSpaceNormal = vs_TangentSpaceTranspose * vNormal;
    vec3 worldSpaceEye;

    float flWorldSpaceDist = 1.0;

    if (bBlend)
    {
        worldSpaceEye = vs_WorldVertToEyeVector;
        flWorldSpaceDist = length(worldSpaceEye);
        worldSpaceEye /= flWorldSpaceDist;
    }
    else
    {
        worldSpaceEye = normalize(vs_WorldVertToEyeVector);
    }

    vec3 reflectVect = CalcReflectionVectorUnnormalized(worldSpaceNormal, worldSpaceEye);
    vec3 specularLighting = ENV_MAP_SCALE * texture(EnvmapSampler, reflectVect).rgb;
    specularLighting *= g_ReflectTint.rgb;

#if FRESNEL
    float flDotResult = dot(worldSpaceEye, worldSpaceNormal);
    flDotResult = 1.0 - max(0.0, flDotResult);

    float flFresnelFactor = flDotResult * flDotResult;
    flFresnelFactor *= flFresnelFactor;
    flFresnelFactor *= flDotResult;
#else
    float flFresnelFactor = g_ReflectTint.a;
#endif

    float flAlpha;
    if (bBlend)
    {
        float flReflectAmount = clamp(flWorldSpaceDist * g_CheapWaterDeltaRecip - g_CheapWaterStartDivDelta, 0.0, 1.0);
        flAlpha = clamp(flFresnelFactor + flReflectAmount, 0.0, 1.0);

#if REFRACTALPHA
        float ooW = 1.0 / vs_Refract_W_ProjZ.z;
        vec2 unwarpedRefractTexCoord = vs_Refract_W_ProjZ.xy * ooW;
        float fogDepthValue = texture(RefractSampler, unwarpedRefractTexCoord).a;
        flAlpha *= clamp((fogDepthValue - 0.05) * 20.0, 0.0, 1.0);
#endif
    }
    else
    {
        flAlpha = 1.0;
#if HDRTYPE == 0 || HDRENABLED == 0
        specularLighting = mix(g_WaterFogColor, specularLighting, flFresnelFactor);
#else
        specularLighting = mix(GammaToLinear(g_WaterFogColor), specularLighting, flFresnelFactor);
#endif
    }

#if (PIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE)
    float fogFactor = CalcRangeFog(vs_Refract_W_ProjZ.w, g_PixelFogParams.x, g_PixelFogParams.z, g_PixelFogParams.w);
#else
    float fogFactor = 0.0;
#endif

    fragColor = FinalOutput(vec4(specularLighting, flAlpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR);
}
