#version 460
// DYNAMIC: "PIXELFOGTYPE"				"0..2"
// DYNAMIC: "NUM_LIGHTS"				"0..4"
// DYNAMIC: "AMBIENT_LIGHT"				"0..1"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord;
in vec4 vs_WorldVertToEyeVector_Darkening;
in mat3 vs_TangentSpace;
in vec4 vs_WorldPos_ProjPosZ;
in vec2 vs_LightAtten01;
in vec2 vs_LightAtten23;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_vertexlitgeneric_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D BumpTextureSampler;

#define g_EyePos_SpecExponent	ps_const[11]
#define g_FogParams				ps_const[12]

#define worldVertToEyeVector	vs_WorldVertToEyeVector_Darkening.xyz
#define fDarkening				vs_WorldVertToEyeVector_Darkening.w

void main()
{
    bool bAmbientLight = AMBIENT_LIGHT != 0;
    int nNumLights = NUM_LIGHTS;

    vec3 cAmbientCube[6] = vec3[6](ps_const[4].xyz, ps_const[5].xyz, ps_const[6].xyz,
                                   ps_const[7].xyz, ps_const[8].xyz, ps_const[9].xyz);

    PixelShaderLightInfo cLightInfo[3] = PixelShaderLightInfo[3](
        PixelShaderLightInfo(ps_const[20], ps_const[21]),
        PixelShaderLightInfo(ps_const[22], ps_const[23]),
        PixelShaderLightInfo(ps_const[24], ps_const[25]));

    vec4 vLightAtten = vec4(vs_LightAtten01, vs_LightAtten23);
    vec4 baseSample = texture(BaseTextureSampler, vs_BaseTexCoord);

    vec3 worldSpaceNormal, tangentSpaceNormal = vec3(0.0, 0.0, 1.0);
    float fSpecExp = g_EyePos_SpecExponent.w;

    vec4 normalTexel = texture(BumpTextureSampler, vs_BaseTexCoord);
    tangentSpaceNormal = 2.0 * normalTexel.xyz - 1.0;
    worldSpaceNormal = normalize(vs_TangentSpace * tangentSpaceNormal);

    if (fSpecExp == 0.0)
        fSpecExp = 1.0 * (1.0 - normalTexel.w) + 150.0 * normalTexel.w;

    vec3 diffuseLighting = PixelShaderDoLighting(vs_WorldPos_ProjPosZ.xyz, worldSpaceNormal,
                                                 vec3(0.0, 0.0, 0.0), false,
                                                 bAmbientLight, vLightAtten,
                                                 cAmbientCube, nNumLights, cLightInfo, true,
                                                 false, 0.0, false, BaseTextureSampler);

    vec3 vDummy, specularLighting;

    PixelShaderDoSpecularLighting(vs_WorldPos_ProjPosZ.xyz, worldSpaceNormal, fSpecExp, normalize(worldVertToEyeVector),
                                  vLightAtten, nNumLights, cLightInfo,
                                  false, 1.0, false, BaseTextureSampler, 1.0, false, 1.0,
                                  specularLighting, vDummy);

    vec3 result = (specularLighting * baseSample.a + baseSample.rgb * diffuseLighting) * fDarkening;

    bool bWriteDepthToAlpha = WRITE_DEPTH_TO_DESTALPHA != 0;

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);
    fragColor = FinalOutput(vec4(result, 1.0), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vs_WorldPos_ProjPosZ.w);
}
