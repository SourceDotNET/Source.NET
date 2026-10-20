#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D ScreenSampler;

#define Threshold ps_const[2].x

const float texel = 0.00156250002;
const vec3 lumWeights = vec3(0.300000012, 0.589999974, 0.109999999);

float Luminance(vec2 offset)
{
    return dot(texture(ScreenSampler, vs_TexCoord + offset).xyz, lumWeights);
}

void main()
{
    float bottom = Luminance(vec2(0.0, -texel));
    float bottomLeft = Luminance(vec2(-texel, -texel));
    float bottomRight = Luminance(vec2(texel, -texel));
    float topLeft = Luminance(vec2(-texel, texel));
    float top = Luminance(vec2(0.0, texel));
    float topRight = Luminance(vec2(texel, texel));
    float right = Luminance(vec2(texel, 0.00139999995));
    float left = Luminance(vec2(-texel, 0.0));

    float edgeX = bottom * 2.0 + bottomLeft + bottomRight - topLeft - top * 2.0 - topRight;
    float edgeY = bottomRight + topRight + right * 2.0 - topLeft - left * 2.0 - bottomLeft;

    float edge = edgeY * edgeY + edgeX * edgeX - Threshold >= 0.0 ? 0.0 : 1.0;

    fragColor = texture(ScreenSampler, vs_TexCoord) * edge;
}
