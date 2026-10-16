#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 10) in vec2 v_TexCoord0;
layout(location = 11) in vec2 v_TexCoord1;
layout(location = 12) in vec3 v_TexCoord2;

layout(location = 0) out vec2 vs_TexCoord;
layout(location = 1) out vec2 vs_GlintCenter;
layout(location = 2) out vec3 vs_GlintColor;

#include "common_clipplanes_gl460.vs"

void main()
{
    gl_Position = vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);
    vs_TexCoord = v_TexCoord0;
    vs_GlintCenter = v_TexCoord1;
    vs_GlintColor = v_TexCoord2;
}
