#version 460
// STATIC: "VERTEXCOLOR"			"0..1"
// STATIC: "CONSTANTCOLOR"			"0..1"
// STATIC: "SRGB"					"0..1"
// DYNAMIC: "HDRENABLED"			"0..1"
// DYNAMIC: "PIXELFOGTYPE"			"0..2"

layout(location = 0) in vec2 vs_BaseTexCoord;
layout(location = 1) in vec4 vs_Color;
layout(location = 2) in vec4 vs_WorldPos_ProjPosZ;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D TexSampler;

#define g_Color					ps_const[0]
#define g_HDRColorScale			ps_const[1].x
#define g_EyePos_SpecExponent	ps_const[11]
#define g_FogParams				ps_const[12]

void main()
{
    vec4 result;
    vec4 color = texture(TexSampler, vs_BaseTexCoord);

#if VERTEXCOLOR
    color *= vs_Color;
#endif

#if CONSTANTCOLOR
    color *= g_Color;
#endif

#if HDRENABLED
    color.xyz *= g_HDRColorScale;
#endif

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);
#if SRGB
    result = FinalOutput(color, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR);
#else
    result = FinalOutput(color, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_GAMMA);
#endif

    fragColor = result;
}
