#version 460

layout(location = 0) in vec2 vs_TexCoord;
layout(location = 1) in vec2 vs_GlintCenter;
layout(location = 2) in vec3 vs_GlintColor;

out vec4 fragColor;

float GlintGaussSpotCoefficient(vec2 d)
{
    return clamp(exp(-25.0 * dot(d, d)), 0.0, 1.0);
}

void main()
{
    vec2 uv = vs_TexCoord - vs_GlintCenter;

    float intensity =	GlintGaussSpotCoefficient(uv + vec2(-0.25, -0.25)) +
                        GlintGaussSpotCoefficient(uv + vec2( 0.25, -0.25)) +
                    5.0 * GlintGaussSpotCoefficient(uv) +
                        GlintGaussSpotCoefficient(uv + vec2(-0.25,  0.25)) +
                        GlintGaussSpotCoefficient(uv + vec2( 0.25,  0.25));

    intensity *= 4.0 / 9.0;

    fragColor = vec4(intensity * vs_GlintColor, 1.0);
}
