#version 460
// STATIC: "VERTEXCOLOR"			"0..1"
// STATIC: "SRGB"					"0..1"

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Color;
layout(location = 10) in vec4 v_TexCoord0;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

#include "common_gl460.glsl"

layout(location = 0) out vec2 vs_BaseTexCoord;
layout(location = 1) out vec4 vs_Color;
layout(location = 2) out vec4 vs_WorldPos_ProjPosZ;

#include "common_clipplanes_gl460.vs"

void main()
{
    vec3 worldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;

    vec4 projPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = projPos;
    WriteUserClipDistances(gl_Position);

    vs_WorldPos_ProjPosZ = vec4(worldPos, projPos.z);

    vs_Color = vec4(1.0);
#if VERTEXCOLOR
#if SRGB
    vs_Color = vec4(GammaToLinear(v_Color.rgb), v_Color.a);
#else
    vs_Color = v_Color;
#endif
#endif

    vs_BaseTexCoord = v_TexCoord0.xy;
}
