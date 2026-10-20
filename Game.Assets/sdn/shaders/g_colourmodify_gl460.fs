#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D FullScreen;

#define LevelR ps_const[0].x
#define LevelG ps_const[1].x
#define LevelB ps_const[2].x
#define Brightness ps_const[3].x
#define Contrast ps_const[4].x
#define Colour ps_const[5].x
#define LevelRmp ps_const[6].x
#define LevelGmp ps_const[7].x
#define LevelBmp ps_const[8].x
#define InvertCol ps_const[9].x

void main()
{
    vec4 color = texture(FullScreen, vs_TexCoord);
    vec3 level = color.xyz + vec3(LevelR, LevelG, LevelB);

    float red = level.g - level.r >= 0.0 ? 0.0 : level.r - level.g;
    float green = level.r - level.g >= 0.0 ? 0.0 : level.g - level.r;
    float blue = level.r - level.b >= 0.0 ? 0.0 : level.b - level.r;

    if (level.b - level.r < 0.0)
        red += level.r - level.b;
    if (level.b - level.g < 0.0)
        green += level.g - level.b;
    if (level.g - level.b < 0.0)
        blue += level.b - level.g;

    float grey = (level.r + level.g + level.b) * 0.333333343 * (1.0 - Colour);

    color.xyz = level * Colour + grey;
    color.xyz += vec3(red * red * LevelRmp, green * green * LevelGmp, blue * blue * LevelBmp);
    color.xyz += Brightness;
    color.xyz *= Contrast;

    if (-InvertCol < 0.0)
        color = InvertCol * (color * -2.0 + 1.0) + color;

    fragColor = color;
}
