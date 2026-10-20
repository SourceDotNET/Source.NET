#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D NormBuf;
layout(binding = 1) uniform sampler2D BackBuf;

#define RefractAmount ps_const[0].x

void main()
{
    vec2 normal = texture(NormBuf, vs_TexCoord).xy * 2.0;
    normal += texture(NormBuf, vs_TexCoord + 0.00100000005).xy;
    normal += texture(NormBuf, vs_TexCoord - 0.00100000005).xy;
    normal += texture(NormBuf, vs_TexCoord + vec2(0.00100000005, -0.00100000005)).xy;
    normal += texture(NormBuf, vs_TexCoord + vec2(-0.00100000005, 0.00100000005)).xy;

    normal = normal * 0.166666672 - 0.501960814;

    fragColor = texture(BackBuf, vs_TexCoord + normal * (RefractAmount * 0.25));
}
