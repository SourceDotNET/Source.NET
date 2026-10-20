#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D BaseTextureSampler;

#define BlurSizeH ps_const[0].x

const float tapOffsets[13] = float[](-5.0, -6.0, -4.0, -3.0, -2.0, -1.0, 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
const float tapWeights[13] = float[](0.00876399968, 0.00221600011, 0.0269949995, 0.0647590011, 0.120985001, 0.176033005, 0.199470997, 0.176033005, 0.120985001, 0.0647590011, 0.0269949995, 0.00876399968, 0.00221600011);

void main()
{
    vec4 color = vec4(0.0);
    for (int i = 0; i < 13; i++)
        color += texture(BaseTextureSampler, vs_TexCoord + vec2(BlurSizeH * tapOffsets[i], 0.0)) * tapWeights[i];

    fragColor = color;
}
