#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D FullScreen;

#define Darken ps_const[0].x
#define Multiply ps_const[1].x
#define SunSize_C ps_const[2].x
#define SunX ps_const[3].x
#define SunY ps_const[4].x

void main()
{
    vec4 color = texture(FullScreen, vs_TexCoord);
    vec2 sun = vec2(SunX, SunY);

    vec2 delta = vs_TexCoord - sun;
    float dist = 1.0 / inversesqrt(dot(delta, delta));
    vec2 step = delta * inversesqrt(dot(delta, delta)) * min(SunSize_C, dist);
    float fade = 1.0 - dist;

    for (int i = 0; i < 8; i++) {
        vec4 tap = texture(FullScreen, sun + step * (float(i) * 0.125));
        color += clamp(tap - Darken, 0.0, 10.0) * fade * Multiply;
    }

    fragColor = color;
}
