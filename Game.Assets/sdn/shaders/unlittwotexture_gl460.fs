#version 460
// DYNAMIC: "PIXELFOGTYPE"				"0..1"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord;
in vec2 vs_BaseTexCoord2;
in vec4 vs_WorldPos_ProjPosZ;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D BaseTextureSampler2;

#define g_DiffuseModulation		ps_const[1]
#define g_EyePos_SpecExponent	ps_const[11]
#define g_FogParams				ps_const[12]

void main()
{
    vec4 baseColor = texture(BaseTextureSampler, vs_BaseTexCoord);
    vec4 baseColor2 = texture(BaseTextureSampler2, vs_BaseTexCoord2);
    vec4 result = baseColor * baseColor2 * g_DiffuseModulation;
    float alpha = 1.0;

    bool bWriteDepthToAlpha = WRITE_DEPTH_TO_DESTALPHA != 0;

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.z, vs_WorldPos_ProjPosZ.z, vs_WorldPos_ProjPosZ.w);
    fragColor = FinalOutput(vec4(result.rgb, alpha), fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vs_WorldPos_ProjPosZ.w);}
