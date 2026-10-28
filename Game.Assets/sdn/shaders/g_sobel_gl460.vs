#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 10) in vec2 v_TexCoord;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(location = 0) out vec2 vs_TexCoord;

#include "common_clipplanes_gl460.vs"

void main()
{
    gl_Position = projectionMatrix * viewMatrix * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);
    vs_TexCoord = vec2(v_TexCoord.x, 1.0 - v_TexCoord.y);
}
