#version 460
// STATIC: "BASETEXTURE"				"0..1"
// STATIC: "MULTITEXTURE"				"0..1"
// STATIC: "REFLECT"					"0..1"
// STATIC: "REFRACT"					"0..1"
// STATIC: "ABOVEWATER"					"0..1"
// STATIC: "BLURRY_REFRACT"				"0..1"

// DYNAMIC: "PIXELFOGTYPE"				"0..1"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BumpTexCoord;
in vec3 vs_TangentEyeVect;
in vec4 vs_ReflectXY_RefractYX;
in float vs_W;
in vec4 vs_ProjPos;
#if MULTITEXTURE
in vec4 vs_ExtraBumpTexCoord;
#endif
#if BASETEXTURE
centroid in vec4 vs_LightmapTexCoord1And2;
centroid in vec4 vs_LightmapTexCoord3;
#endif

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D RefractSampler;
#if BASETEXTURE
layout(binding = 1) uniform sampler2D BaseTextureSampler;
#endif
layout(binding = 2) uniform sampler2D ReflectSampler;
#if BASETEXTURE
layout(binding = 3) uniform sampler2D LightmapSampler;
#endif
layout(binding = 4) uniform sampler2D NormalSampler;

#define vRefractTint			ps_const[1]
#define vReflectTint			ps_const[4]
#define g_ReflectRefractScale	ps_const[5]
#define g_WaterFogColor			ps_const[6]
#define g_WaterFogParams		ps_const[7]
#define g_PixelFogParams		ps_const[8]

#define g_WaterFogStart			g_WaterFogParams.x
#define g_WaterFogEndMinusStart	g_WaterFogParams.y
#define g_Reflect_OverBright	g_WaterFogParams.z

void main()
{
    bool bReflect = REFLECT != 0;
    bool bRefract = REFRACT != 0;

#if MULTITEXTURE
    vec4 vNormal  = texture(NormalSampler, vs_BumpTexCoord);
    vec4 vNormal1 = texture(NormalSampler, vs_ExtraBumpTexCoord.xy);
    vec4 vNormal2 = texture(NormalSampler, vs_ExtraBumpTexCoord.zw);
    vNormal = 0.33 * (vNormal + vNormal1 + vNormal2);

    vNormal.xyz = 2.0 * vNormal.xyz - 1.0;

#else
    vec4 vNormal = DecompressNormal(NormalSampler, vs_BumpTexCoord, NORM_DECODE_NONE);
#endif

    float ooW = 1.0 / vs_W;

    vec2 unwarpedRefractTexCoord = vs_ReflectXY_RefractYX.wz * ooW;

#if ABOVEWATER
    float waterFogDepthValue = texture(RefractSampler, unwarpedRefractTexCoord).a;
#else
    float waterFogDepthValue = 1.0;
#endif
    vec4 reflectRefractScale = g_ReflectRefractScale;
#if !BASETEXTURE
#if ( BLURRY_REFRACT == 0 )
    reflectRefractScale *= waterFogDepthValue;
#endif
#endif

    vec2 vReflectTexCoord;
    vec2 vRefractTexCoord;

    vec4 vN;
    vN.x = vNormal.x;
    vN.y = -vNormal.y;
    vN.w = vNormal.x;
    vN.z = -vNormal.y;
    vec4 vDependentTexCoords = vN * vNormal.a * reflectRefractScale;

    vDependentTexCoords += (vs_ReflectXY_RefractYX * ooW);
    vReflectTexCoord = vDependentTexCoords.xy;
    vRefractTexCoord = vDependentTexCoords.wz;

    vec4 vReflectColor = texture(ReflectSampler, vReflectTexCoord);
#if BLURRY_REFRACT
    vec2 ddx1 = vec2(0.005, 0.0);
    vec2 ddy1 = vec2(0.0, 0.005);
    vec4 vRefractColor = vec4(0.0);

    for (int ix = -2; ix <= 2; ix++)
    {
        for (int iy = -2; iy <= 2; iy++)
        {
            vRefractColor += texture(RefractSampler, vRefractTexCoord + ix * ddx1 + iy * ddy1);
        }
    }
    float sumweights = 25.0;

    vRefractColor *= (1.0 / sumweights);
    vReflectColor *= g_Reflect_OverBright;
    vReflectColor *= vReflectTint;
    vRefractColor *= vRefractTint;
#	if ABOVEWATER
    waterFogDepthValue = vRefractColor.a;
#	endif
#else
    vReflectColor *= vReflectTint;
    vec4 vRefractColor = texture(RefractSampler, vRefractTexCoord);
#	if ABOVEWATER
    waterFogDepthValue = texture(RefractSampler, vRefractTexCoord).a;
#	endif
#endif

    vec3 vEyeVect = normalize(vs_TangentEyeVect);

    float fNdotV = clamp(dot(vEyeVect, vNormal.xyz), 0.0, 1.0);
    float fFresnel = pow(1.0 - fNdotV, 5.0);

#if !BASETEXTURE
    fFresnel *= clamp((waterFogDepthValue - 0.05) * 20.0, 0.0, 1.0);
#endif

#if ABOVEWATER
    vRefractColor = mix(vRefractColor, g_WaterFogColor * LINEAR_LIGHT_SCALE, clamp(waterFogDepthValue - 0.05, 0.0, 1.0));
#else
    float waterFogFactor = clamp((vs_ProjPos.z - g_WaterFogStart) / g_WaterFogEndMinusStart, 0.0, 1.0);
    vRefractColor = mix(vRefractColor, g_WaterFogColor * LINEAR_LIGHT_SCALE, waterFogFactor);
#endif

#if BASETEXTURE
    vec4 baseSample = texture(BaseTextureSampler, vs_BumpTexCoord.xy);
    vec2 bumpCoord1;
    vec2 bumpCoord2;
    vec2 bumpCoord3;
    ComputeBumpedLightmapCoordinates(vs_LightmapTexCoord1And2, vs_LightmapTexCoord3.xy,
        bumpCoord1, bumpCoord2, bumpCoord3);

    vec4 lightmapSample1 = texture(LightmapSampler, bumpCoord1);
    vec3 lightmapColor1 = lightmapSample1.rgb;
    vec3 lightmapColor2 = texture(LightmapSampler, bumpCoord2).rgb;
    vec3 lightmapColor3 = texture(LightmapSampler, bumpCoord3).rgb;

    vec3 dp;
    dp.x = clamp(dot(vNormal.xyz, bumpBasis[0]), 0.0, 1.0);
    dp.y = clamp(dot(vNormal.xyz, bumpBasis[1]), 0.0, 1.0);
    dp.z = clamp(dot(vNormal.xyz, bumpBasis[2]), 0.0, 1.0);
    dp *= dp;

    vec3 diffuseLighting = dp.x * lightmapColor1 +
        dp.y * lightmapColor2 +
        dp.z * lightmapColor3;
    float sum = dot(dp, vec3(1.0, 1.0, 1.0));
    diffuseLighting *= LIGHT_MAP_SCALE / sum;
    vec3 diffuseComponent = baseSample.rgb * diffuseLighting;
#endif

    vec4 result;
    if (bReflect && bRefract)
    {
        result = mix(vRefractColor, vReflectColor, fFresnel);
    }
    else if (bReflect)
    {
#if BASETEXTURE
        result = vec4(diffuseComponent, 1.0) + vReflectColor * fFresnel * baseSample.a;
#else
        result = vReflectColor;
#endif
    }
    else if (bRefract)
    {
        result = vRefractColor;
    }
    else
    {
        result = vec4(0.0, 0.0, 0.0, 0.0);
    }

#if (PIXELFOGTYPE == PIXEL_FOG_TYPE_RANGE)
    float fogFactor = CalcRangeFog(vs_ProjPos.z, g_PixelFogParams.x, g_PixelFogParams.z, g_PixelFogParams.w);
#else
    float fogFactor = 0.0;
#endif

    fragColor = FinalOutput(vec4(result.rgb, 1.0), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_NONE, WRITE_DEPTH_TO_DESTALPHA != 0, vs_ProjPos.z);
}
