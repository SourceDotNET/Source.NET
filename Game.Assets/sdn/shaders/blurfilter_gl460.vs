#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 10) in vec2 v_TexCoord;

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#define vsTapOffs0 vs_const[48].xy
#define vsTapOffs1 vs_const[49].xy
#define vsTapOffs2 vs_const[50].xy

out vec2 vs_CoordTap0;
out vec2 vs_CoordTap1;
out vec2 vs_CoordTap2;
out vec2 vs_CoordTap3;
out vec2 vs_CoordTap1Neg;
out vec2 vs_CoordTap2Neg;
out vec2 vs_CoordTap3Neg;

vec2 FlipV(vec2 uv)
{
    return vec2(uv.x, 1.0 - uv.y);
}

#include "common_clipplanes_gl460.vs"

void main()
{
    gl_Position = vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);

    vs_CoordTap0    = FlipV(v_TexCoord);
    vs_CoordTap1    = FlipV(v_TexCoord + vsTapOffs0);
    vs_CoordTap2    = FlipV(v_TexCoord + vsTapOffs1);
    vs_CoordTap3    = FlipV(v_TexCoord + vsTapOffs2);
    vs_CoordTap1Neg = FlipV(v_TexCoord - vsTapOffs0);
    vs_CoordTap2Neg = FlipV(v_TexCoord - vsTapOffs1);
    vs_CoordTap3Neg = FlipV(v_TexCoord - vsTapOffs2);
}
