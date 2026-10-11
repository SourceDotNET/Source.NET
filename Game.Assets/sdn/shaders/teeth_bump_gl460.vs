#version 460
//	STATIC: "INTRO"						"0..1"
//  STATIC: "USE_STATIC_CONTROL_FLOW"	"0..1"

//	DYNAMIC: "SKINNING"					"0..1"
//  DYNAMIC: "NUM_LIGHTS"				"0..2"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 7) in ivec4 v_BoneIndex;
layout(location = 8) in vec2 v_BoneWeights;
layout(location = 9) in vec4 v_UserData;
layout(location = 10) in vec4 v_TexCoord0;
layout(location = 14) in vec4 v_FlexPosition;
layout(location = 15) in vec3 v_FlexNormal;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

const int SHADER_SPECIFIC_CONST_0 = 48;
const int SHADER_SPECIFIC_CONST_1 = 49;

#include "common_gl460.vs"
#include "common_lighting_gl460.vs"
#include "common_morph_gl460.vs"
#include "common_skinning_gl460.vs"
#include "vortwarp_gl460.vs"

const bool g_bSkinning	= SKINNING != 0;

#define cTeethLighting		vs_const[SHADER_SPECIFIC_CONST_0]
#if INTRO
#define const4				vs_const[SHADER_SPECIFIC_CONST_1]
#define g_Time				const4.w
#define modelOrigin			const4.xyz
#endif

out vec2 vs_BaseTexCoord;
out vec4 vs_WorldVertToEyeVector_Darkening;
out mat3 vs_TangentSpace;
out vec4 vs_WorldPos_ProjPosZ;
out vec2 vs_LightAtten01;
out vec2 vs_LightAtten23;

#include "common_clipplanes_gl460.vs"

void main()
{
    vec4 vPosition = vec4(v_Position, 1.0);
    vec3 vNormal = v_Normal;
    vec4 vTangent = v_UserData;

    ApplyMorph(v_FlexPosition.xyz, v_FlexNormal, vPosition.xyz, vNormal, vTangent.xyz);

    vec3 worldNormal, worldPos, worldTangentS, worldTangentT;
    SkinPositionNormalAndTangentSpace(g_bSkinning, vPosition, vNormal, vTangent,
        v_BoneIndex, v_BoneWeights, worldPos,
        worldNormal, worldTangentS, worldTangentT);

#if INTRO
    WorldSpaceVertexProcess(g_Time, modelOrigin, worldPos, worldNormal, worldTangentS, worldTangentT);
#endif

    worldNormal   = normalize(worldNormal);
    worldTangentS = normalize(worldTangentS);
    worldTangentT = normalize(worldTangentT);

    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);

    vs_WorldPos_ProjPosZ = vec4(worldPos, vProjPos.z);

    vs_WorldVertToEyeVector_Darkening.xyz = cEyePos - worldPos;

    vs_WorldVertToEyeVector_Darkening.w = cTeethLighting.w * clamp(dot(worldNormal, cTeethLighting.xyz), 0.0, 1.0);

    InitLightInfo();

#if !USE_STATIC_CONTROL_FLOW
    vs_LightAtten01 = vec2(0.0, 0.0);
    vs_LightAtten23 = vec2(0.0, 0.0);
#if (NUM_LIGHTS > 0)
    vs_LightAtten01.x = GetVertexAttenForLight(worldPos, 0, false);
#endif
#if (NUM_LIGHTS > 1)
    vs_LightAtten01.y = GetVertexAttenForLight(worldPos, 1, false);
#endif
#if (NUM_LIGHTS > 2)
    vs_LightAtten23.x = GetVertexAttenForLight(worldPos, 2, false);
#endif
#if (NUM_LIGHTS > 3)
    vs_LightAtten23.y = GetVertexAttenForLight(worldPos, 3, false);
#endif
#else
    vs_LightAtten01.x = GetVertexAttenForLight(worldPos, 0, true);
    vs_LightAtten01.y = GetVertexAttenForLight(worldPos, 1, true);
    vs_LightAtten23.x = GetVertexAttenForLight(worldPos, 2, true);
    vs_LightAtten23.y = GetVertexAttenForLight(worldPos, 3, true);
#endif

    vs_BaseTexCoord = v_TexCoord0.xy;

    vs_TangentSpace = mat3(worldTangentS, worldTangentT, worldNormal);
}
