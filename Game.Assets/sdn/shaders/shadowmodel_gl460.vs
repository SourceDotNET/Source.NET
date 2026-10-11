#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#define cShadowTextureMatrix0 vs_const[48]
#define cShadowTextureMatrix1 vs_const[49]
#define cShadowTextureMatrix2 vs_const[50]
#define cTexOrigin vs_const[51]
#define cTexScale vs_const[52]
#define cShadowConstants vs_const[53]
#define cModulationColor vs_const[47]

#define flShadowFalloffOffset cShadowConstants.x
#define flOneOverShadowDist cShadowConstants.y
#define flShadowScale cShadowConstants.z

out vec3 vs_T0;
out vec3 vs_T1;
out vec3 vs_T2;
out float vs_T3;
out vec4 vs_Color;

#include "common_clipplanes_gl460.vs"

void main()
{
    vec3 worldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;
    vec3 worldNormal = mat3(modelMatrix) * v_Normal;

    gl_Position = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    WriteUserClipDistances(gl_Position);

    vec3 vTexturePos;
    vTexturePos.x = dot(vec4(worldPos, 1.0), cShadowTextureMatrix0);
    vTexturePos.y = dot(vec4(worldPos, 1.0), cShadowTextureMatrix1);
    vTexturePos.z = dot(vec4(worldPos, 1.0), cShadowTextureMatrix2);

    float flShadowFade = (vTexturePos.z - flShadowFalloffOffset) * flOneOverShadowDist;

    vs_T0 = vTexturePos * cTexScale.xyz + cTexOrigin.xyz;

    vs_T1.xyz = vTexturePos.xyz;
    vs_T2.xyz = vec3(1.0, 1.0, 1.0) - vTexturePos.xyz;
    vs_T2.z = 1.0 - flShadowFade;

    vs_T3 = dot(worldNormal, -cShadowTextureMatrix2.xyz);

    vs_Color.xyz = cModulationColor.xyz;
    vs_Color.w = flShadowFade * flShadowScale;
}
