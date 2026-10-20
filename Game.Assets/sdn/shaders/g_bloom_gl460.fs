#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D Bloom;

#define LevelR ps_const[0].x
#define LevelG ps_const[1].x
#define LevelB ps_const[2].x
#define ColorMul ps_const[3].x

void main()
{
    vec4 color = texture(Bloom, vs_TexCoord);
    vec3 level = color.xyz * vec3(LevelR, LevelG, LevelB);

    float grey = (level.r + level.g + level.b) * 0.333333343 * (1.0 - ColorMul);

    fragColor = vec4(level * ColorMul + grey, 1.0);
}
