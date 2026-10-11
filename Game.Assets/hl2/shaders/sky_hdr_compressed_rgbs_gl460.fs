#version 460
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord00;
in vec2 vs_BaseTexCoord01;
in vec2 vs_BaseTexCoord10;
in vec2 vs_BaseTexCoord11;
in vec2 vs_BaseTexCoord_In_Pixels;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D RGBSTextureSampler;

#define InputScale ps_const[0]

void main()
{
    vec4 s00 = texture(RGBSTextureSampler, vs_BaseTexCoord00);
    vec4 s10 = texture(RGBSTextureSampler, vs_BaseTexCoord10);
    vec4 s01 = texture(RGBSTextureSampler, vs_BaseTexCoord01);
    vec4 s11 = texture(RGBSTextureSampler, vs_BaseTexCoord11);

    vec2 fracCoord = fract(vs_BaseTexCoord_In_Pixels);

    s00.rgb *= s00.a;
    s10.rgb *= s10.a;

    s00.xyz = mix(s00, s10, fracCoord.x).xyz;

    s01.rgb *= s01.a;
    s11.rgb *= s11.a;
    s01.xyz = mix(s01, s11, fracCoord.x).xyz;

    vec3 result = mix(s00, s01, fracCoord.y).xyz;

    fragColor = FinalOutput(vec4(InputScale.rgb * result, 1.0), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR, WRITE_DEPTH_TO_DESTALPHA != 0, 1e20);
}
