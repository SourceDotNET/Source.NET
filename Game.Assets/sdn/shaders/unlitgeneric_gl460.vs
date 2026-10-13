#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Color;
layout(location = 10) in vec2 v_TexCoord;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

const int VertexColor = 16;
const int VertexAlpha = 32;

uniform int flags;

out vec2 vs_TexCoord;
out vec4 vs_Color;

#include "common_clipplanes_gl460.vs"

void main()
{
	mat4 mvp = projectionMatrix * viewMatrix * modelMatrix;

    gl_Position = mvp * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);
    vs_TexCoord = v_TexCoord;

    vec4 color = vs_const[47];

    if((flags & VertexColor) != 0)
        color.rgb *= v_Color.rgb;

    if((flags & VertexAlpha) != 0)
        color.a *= v_Color.a;

    vs_Color = color;
}