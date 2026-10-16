#version 460

layout(location = 0) in vec2 vs_CoordTap0;
layout(location = 1) in vec2 vs_CoordTap1;
layout(location = 2) in vec2 vs_CoordTap2;
layout(location = 3) in vec2 vs_CoordTap3;
layout(location = 4) in vec2 vs_CoordTap1Neg;
layout(location = 5) in vec2 vs_CoordTap2Neg;
layout(location = 6) in vec2 vs_CoordTap3Neg;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D TexSampler;

#define psTapOffs0 ps_const[0].xy
#define psTapOffs1 ps_const[1].xy
#define psTapOffs2 ps_const[2].xy
#define scale_factor ps_const[3].xyz

void main()
{
    vec4 s0, s1, s2, s3, s4, s5, s6, color;

    s0 = texture(TexSampler, vs_CoordTap0);
    s1 = texture(TexSampler, vs_CoordTap1);
    s2 = texture(TexSampler, vs_CoordTap2);
    s3 = texture(TexSampler, vs_CoordTap3);
    s4 = texture(TexSampler, vs_CoordTap1Neg);
    s5 = texture(TexSampler, vs_CoordTap2Neg);
    s6 = texture(TexSampler, vs_CoordTap3Neg);

    color = s0 * 0.2013;
    color += (s1 + s4) * 0.2185;
    color += (s2 + s5) * 0.0821;
    color += (s3 + s6) * 0.0461;

    vec2 coordTap4 = vs_CoordTap0 + psTapOffs0;
    vec2 coordTap5 = vs_CoordTap0 + psTapOffs1;
    vec2 coordTap6 = vs_CoordTap0 + psTapOffs2;
    vec2 coordTap4Neg = vs_CoordTap0 - psTapOffs0;
    vec2 coordTap5Neg = vs_CoordTap0 - psTapOffs1;
    vec2 coordTap6Neg = vs_CoordTap0 - psTapOffs2;

    s1 = texture(TexSampler, coordTap4);
    s2 = texture(TexSampler, coordTap5);
    s3 = texture(TexSampler, coordTap6);
    s4 = texture(TexSampler, coordTap4Neg);
    s5 = texture(TexSampler, coordTap5Neg);
    s6 = texture(TexSampler, coordTap6Neg);

    color += (s1 + s4) * 0.0262;
    color += (s2 + s5) * 0.0162;
    color += (s3 + s6) * 0.0102;
    color.xyz *= scale_factor.xyz;

    fragColor = color;
}
