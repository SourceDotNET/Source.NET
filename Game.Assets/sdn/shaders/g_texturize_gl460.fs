#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D FullScreen;
layout(binding = 1) uniform sampler2D SourceTexture;

#define scalex ps_const[0].x
#define scaley ps_const[1].x

float SignedFrac(float sign, float value)
{
    return sign >= 0.0 ? fract(abs(value)) : -fract(abs(value));
}

void main()
{
    vec3 color = texture(FullScreen, vs_TexCoord).xyz;
    float brightness = (color.r + color.g + color.b) * 2.66666675;
    float band = min(floor(brightness) * 0.125, 0.875);

    float y = vs_TexCoord.y * scaley;
    float x = vs_TexCoord.x * scalex;

    vec2 uv = vec2(SignedFrac(x, x), SignedFrac(y, y * 8.0) * 0.125 + band);

    fragColor = vec4(texture(SourceTexture, uv).xyz, 1.0);
}
