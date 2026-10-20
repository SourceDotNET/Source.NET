#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D BaseTextureSampler;

#define g_darken ps_const[0].x
#define g_multiply ps_const[1].x
#define g_sizex ps_const[2].x
#define g_sizey ps_const[3].x

void main()
{
    vec3 color = texture(BaseTextureSampler, vs_TexCoord).xyz * 2.0;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(g_sizex, -g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(g_sizex, g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(-g_sizex, -g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(-g_sizex, g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(0.0, -g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(0.0, g_sizey)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(g_sizex, 0.0)).xyz;
    color += texture(BaseTextureSampler, vs_TexCoord + vec2(-g_sizex, 0.0)).xyz;

    color = max(color * 0.100000001 - g_darken, 0.0) * g_multiply;

    fragColor = vec4(color, 1.0);
}
