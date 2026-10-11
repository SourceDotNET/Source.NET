#version 460
// DYNAMIC: "WRITE_DEPTH_TO_DESTALPHA"	"0..1"

in vec2 vs_BaseTexCoord00;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;

#define InputScale ps_const[0]

void main()
{
    vec4 color = texture(BaseTextureSampler, vs_BaseTexCoord00);
    color.rgb *= InputScale.rgb;

    fragColor = FinalOutput(color, 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR, WRITE_DEPTH_TO_DESTALPHA != 0, 1e20);
}
