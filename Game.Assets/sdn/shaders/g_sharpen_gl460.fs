#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D FullScreen;

#define Contrast ps_const[0].x
#define Distance ps_const[1].x

void main()
{
    vec4 color = texture(FullScreen, vs_TexCoord);
    color += texture(FullScreen, vs_TexCoord + Distance) * Contrast;
    color -= texture(FullScreen, vs_TexCoord - Distance) * Contrast;

    fragColor = color;
}
