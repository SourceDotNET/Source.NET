#version 460

layout(location = 0) in vec3 v_Position;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

#include "common_clipplanes_gl460.vs"

void main()
{
	mat4 mvp = projectionMatrix * viewMatrix * modelMatrix;
    gl_Position = mvp * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);
}