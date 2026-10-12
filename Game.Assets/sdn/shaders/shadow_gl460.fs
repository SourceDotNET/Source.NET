#version 460

centroid in vec2 vs_TexCoord0;
centroid in vec2 vs_TexCoord1;
centroid in vec2 vs_TexCoord2;
centroid in vec2 vs_TexCoord3;
centroid in vec2 vs_TexCoord4;
in vec4 vs_ShadowColor;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

#define g_ShadowColor ps_const[1]

layout(binding = 0) uniform sampler2D basetexture;

out vec4 fragColor;

void main()
{
    vec4 samples[5];
    samples[0] = texture(basetexture, vs_TexCoord0);
    samples[1] = texture(basetexture, vs_TexCoord1);
    samples[2] = texture(basetexture, vs_TexCoord2);
    samples[3] = texture(basetexture, vs_TexCoord3);
    samples[4] = texture(basetexture, vs_TexCoord4);

    float shadowCoverage = (samples[0].a + samples[1].a + samples[2].a + samples[3].a + samples[4].a) * 0.2;

    shadowCoverage = clamp(shadowCoverage - vs_ShadowColor.a, 0.0, 1.0);

    vec4 result = shadowCoverage * g_ShadowColor - shadowCoverage;
    result = 1.0 + result;

    float alpha = 1.0;

    // fog todo

    fragColor = vec4(result.rgb, alpha);
}
