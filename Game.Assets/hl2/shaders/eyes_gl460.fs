#version 460
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"
// DYNAMIC: "PIXELFOGTYPE"				"0..2"

in vec2 vs_BaseTexCoord;
in vec2 vs_IrisTexCoord;
in vec2 vs_GlintTexCoord;
in vec3 vs_Color;
in vec4 vs_WorldPos_ProjPosZ;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D IrisSampler;
layout(binding = 2) uniform sampler2D GlintSampler;

#define cEyeScalars				ps_const[0]
#define g_EyePos_SpecExponent	ps_const[11]
#define g_FogParams				ps_const[12]

#define fGlintDamping	cEyeScalars.y

void main()
{
    vec4 baseSample  = texture(BaseTextureSampler, vs_BaseTexCoord);
    vec4 glintSample = texture(GlintSampler,       vs_GlintTexCoord);

    vec4 irisSample = texture(IrisSampler, vs_IrisTexCoord);

    vec4 result;
    result.rgb = mix(baseSample.rgb, irisSample.rgb, irisSample.a);
    result.rgb *= vs_Color;
    result.rgb += glintSample.rgb * fGlintDamping;
    result.a = baseSample.a;

    bool bWriteDepthToAlpha = WRITE_DEPTH_TO_DESTALPHA != 0;

    float fogFactor = CalcPixelFogFactor(PIXELFOGTYPE, g_FogParams, g_EyePos_SpecExponent.xyz, vs_WorldPos_ProjPosZ.xyz, vs_WorldPos_ProjPosZ.w);
    fragColor = FinalOutput(result, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_LINEAR, bWriteDepthToAlpha, vs_WorldPos_ProjPosZ.w);
}
