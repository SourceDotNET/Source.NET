#version 460
// DYNAMIC: "PIXELFOGTYPE"				"0..2"
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord;
in vec3 vs_VertAtten;
in vec4 vs_WorldPos_ProjPosZ;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;

#define g_EyePos_SpecExponent	ps_const[11]
#define g_FogParams				ps_const[12]

void main()
{
    vec4 baseSample = texture(BaseTextureSampler, vs_BaseTexCoord);

    vec4 result;
    result.xyz = baseSample.xyz * vs_VertAtten;
    result.a = baseSample.a;

    bool bWriteDepthToAlpha = WRITE_DEPTH_TO_DESTALPHA != 0;

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);
    fragColor = FinalOutput(result, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vs_WorldPos_ProjPosZ.w);
}
