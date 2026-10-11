#version 460
//	STATIC: "VERTEXCOLOR"		"0..1"
//	STATIC: "TRANSFORMVERTS"	"0..1"

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Color;
layout(location = 10) in vec2 v_TexCoord;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

out vec2 vs_BaseTexCoord;
out vec4 vs_TexCoord0;
out vec4 vs_TexCoord1;
out vec4 vs_TexCoord2;
out vec4 vs_Color0;

#include "common_clipplanes_gl460.vs"

void main()
{
#if TRANSFORMVERTS
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(v_Position, 1.0);
#else
    gl_Position = vec4(v_Position, 1.0);
#endif
    WriteUserClipDistances(gl_Position);
    vs_BaseTexCoord = vec2(v_TexCoord.x, 1.0 - v_TexCoord.y);
    vs_TexCoord0 = vec4(vs_BaseTexCoord, 0.0, 1.0);
    vs_TexCoord1 = vec4(0.0, 0.0, 0.0, 1.0);
    vs_TexCoord2 = vs_TexCoord0;
#if VERTEXCOLOR
    vs_Color0 = v_Color;
#else
    vs_Color0 = vec4(1.0);
#endif
}
