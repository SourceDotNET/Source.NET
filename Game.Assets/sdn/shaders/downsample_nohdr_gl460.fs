#version 460
// STATIC: "CSTRIKE"			"0..1"

in vec2 vs_CoordTap0;
in vec2 vs_CoordTap1;
in vec2 vs_CoordTap2;
in vec2 vs_CoordTap3;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D TexSampler;

#define params ps_const[0]

#if CSTRIKE == 0
vec4 Shape(vec2 uv)
{
    vec4 pixel = texture(TexSampler, uv);

    float lum = dot(pixel.xyz, params.xyz);
    pixel.xyz = pow(pixel.xyz, vec3(params.w)) * lum;

    return pixel;
}

void main()
{
    vec4 s0 = Shape(vs_CoordTap0);
    vec4 s1 = Shape(vs_CoordTap1);
    vec4 s2 = Shape(vs_CoordTap2);
    vec4 s3 = Shape(vs_CoordTap3);

    vec4 avgColor = (s0 + s1 + s2 + s3) * 0.25;
    fragColor = FinalOutput(avgColor, 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
}
#else
vec3 Shape(vec3 s)
{
    float lum = (0.3 * s.x) + (0.59 * s.y) + (0.11 * s.z);
    return lum * s;
}

void main()
{
    vec3 s0 = Shape(GammaToLinear(texture(TexSampler, vs_CoordTap0).rgb));
    vec3 s1 = Shape(GammaToLinear(texture(TexSampler, vs_CoordTap1).rgb));
    vec3 s2 = Shape(GammaToLinear(texture(TexSampler, vs_CoordTap2).rgb));
    vec3 s3 = Shape(GammaToLinear(texture(TexSampler, vs_CoordTap3).rgb));

    vec3 avgColor = (s0 + s1 + s2 + s3) * 0.25;
    fragColor = vec4(avgColor, 1.0);
}
#endif
