#version 460

layout(location = 0) in vec2 vs_TexCoord;

out vec4 fragColor;

layout(binding = 0) uniform sampler2D RT;

void main()
{
    fragColor = texture(RT, vs_TexCoord);
}
