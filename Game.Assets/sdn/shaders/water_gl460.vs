#version 460
// STATIC: "BASETEXTURE"				"0..1"
// STATIC: "MULTITEXTURE"				"0..1"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 4) in vec3 v_TangentS;
layout(location = 5) in vec3 v_TangentT;
layout(location = 10) in vec2 v_TexCoord0;
layout(location = 11) in vec2 v_TexCoord1;
layout(location = 12) in vec2 v_TexCoord2;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

const int SHADER_SPECIFIC_CONST_1 = 49;
const int SHADER_SPECIFIC_CONST_3 = 51;

#include "common_gl460.vs"

#define cBumpTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_1 + 0]
#define cBumpTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_1 + 1]
#define TexOffsets					vs_const[SHADER_SPECIFIC_CONST_3]

out vec2 vs_BumpTexCoord;
out vec3 vs_TangentEyeVect;
out vec4 vs_ReflectXY_RefractYX;
out float vs_W;
out vec4 vs_ProjPos;
#if MULTITEXTURE
out vec4 vs_ExtraBumpTexCoord;
#endif
#if BASETEXTURE
centroid out vec4 vs_LightmapTexCoord1And2;
centroid out vec4 vs_LightmapTexCoord3;
#endif

#include "common_clipplanes_gl460.vs"

void main()
{
    vec3 vObjNormal = v_Normal;

    vec3 vWorldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;
    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(vWorldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);
    vs_ProjPos = vProjPos;

    vec2 vReflectPos;
    vReflectPos.x = vProjPos.x;
    vReflectPos.y = -vProjPos.y;
    vReflectPos = (vReflectPos + vProjPos.w) * 0.5;

    vec2 vRefractPos = (vProjPos.xy + vProjPos.w) * 0.5;

    vs_ReflectXY_RefractYX = vec4(vReflectPos.x, vReflectPos.y, vRefractPos.y, vRefractPos.x);
    vs_W = vProjPos.w;

    vec3 vWorldEyeVect = cEyePos - vWorldPos;
    vs_TangentEyeVect.x = dot(vWorldEyeVect, v_TangentS);
    vs_TangentEyeVect.y = dot(vWorldEyeVect, v_TangentT);
    vs_TangentEyeVect.z = dot(vWorldEyeVect, vObjNormal);

    vs_BumpTexCoord.x = dot(v_TexCoord0, cBumpTexCoordTransform0.xy) + cBumpTexCoordTransform0.w;
    vs_BumpTexCoord.y = dot(v_TexCoord0, cBumpTexCoordTransform1.xy) + cBumpTexCoordTransform1.w;
    float f45x = v_TexCoord0.x + v_TexCoord0.y;
    float f45y = v_TexCoord0.y - v_TexCoord0.x;
#if MULTITEXTURE
    vs_ExtraBumpTexCoord.x = f45x * 0.1 + TexOffsets.x;
    vs_ExtraBumpTexCoord.y = f45y * 0.1 + TexOffsets.y;
    vs_ExtraBumpTexCoord.z = v_TexCoord0.y * 0.45 + TexOffsets.z;
    vs_ExtraBumpTexCoord.w = v_TexCoord0.x * 0.45 + TexOffsets.w;
#endif

#if BASETEXTURE
    vs_LightmapTexCoord1And2.xy = v_TexCoord1 + v_TexCoord2;

    vec2 lightmapTexCoord2 = vs_LightmapTexCoord1And2.xy + v_TexCoord2;
    vec2 lightmapTexCoord3 = lightmapTexCoord2 + v_TexCoord2;

    vs_LightmapTexCoord1And2.w = lightmapTexCoord2.x;
    vs_LightmapTexCoord1And2.z = lightmapTexCoord2.y;

    vs_LightmapTexCoord3 = vec4(lightmapTexCoord3, 0.0, 0.0);
#endif
}
