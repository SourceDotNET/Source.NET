#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Color;
layout(location = 10) in vec2 v_TexCoord0;
layout(location = 11) in vec2 v_TexCoord1;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

out vec2 vs_TexCoord0;
centroid out vec2 vs_TexCoord1;
out vec4 vs_Color;

#include "common_clipplanes_gl460.vs"

void main()
{
	mat4 mvp = projectionMatrix * viewMatrix * modelMatrix;

    gl_Position  = mvp * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);
    vs_TexCoord0 = v_TexCoord0;
    vs_TexCoord1 = v_TexCoord1;
    vs_Color     = v_Color;
}