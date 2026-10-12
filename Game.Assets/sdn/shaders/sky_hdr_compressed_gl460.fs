#version 460
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord00;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D ExposureTextureSampler0;
layout(binding = 1) uniform sampler2D ExposureTextureSampler1;
layout(binding = 2) uniform sampler2D ExposureTextureSampler2;

void main()
{
    vec3 color0 = 0.25 * texture(ExposureTextureSampler0, vs_BaseTexCoord00).rgb;
    vec3 color1 = 2.0 * texture(ExposureTextureSampler1, vs_BaseTexCoord00).rgb;
    vec3 color2 = 16.0 * texture(ExposureTextureSampler2, vs_BaseTexCoord00).rgb;

    fragColor = FinalOutput(vec4(1.0, 0.0, 0.0, 1.0), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR, WRITE_DEPTH_TO_DESTALPHA != 0, 1e20);
}
