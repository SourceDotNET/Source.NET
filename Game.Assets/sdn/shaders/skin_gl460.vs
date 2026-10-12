#version 460

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
const int SHADER_SPECIFIC_CONST_4 = 52;

#include "common_gl460.vs"
#include "common_lighting_gl460.vs"
#include "common_morph_gl460.vs"
#include "common_skinning_gl460.vs"

#define cBaseTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_0 + 0]
#define cBaseTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_0 + 1]
#define cDetailTexCoordTransform0	vs_const[SHADER_SPECIFIC_CONST_4 + 0]
#define cDetailTexCoordTransform1	vs_const[SHADER_SPECIFIC_CONST_4 + 1]

const bool g_bSkinning	= SKINNING != 0;

out vec4 vs_BaseTexCoord;			// includes detail tex coord
out vec3 vs_LightAtten;
out vec3 vs_WorldVertToEyeVector;
out mat3 vs_TangentSpaceTranspose;
out vec4 vs_WorldPos_Atten3;
out vec4 vs_ProjPos_WrinkleWeight;

//-----------------------------------------------------------------------------
// Main shader entry point
//-----------------------------------------------------------------------------
#include "common_clipplanes_gl460.vs"

void main()
{
    vec4 vPosition = vec4(v_Position, 1.0);
    vec3 vNormal = v_Normal;
    vec4 vTangent = v_UserData;

    float flWrinkleWeight;
    ApplyMorph(v_FlexPosition, v_FlexNormal, vPosition.xyz, vNormal, vTangent.xyz, flWrinkleWeight);

    // Perform skinning
    vec3 worldNormal, worldPos, worldTangentS, worldTangentT;
    SkinPositionNormalAndTangentSpace(g_bSkinning, vPosition, vNormal, vTangent,
        v_BoneIndex, v_BoneWeights, worldPos,
        worldNormal, worldTangentS, worldTangentT);

    // Always normalize since flex path is controlled by runtime
    // constant not a shader combo and will always generate the normalization
    worldNormal   = normalize(worldNormal);
    worldTangentS = normalize(worldTangentS);
    worldTangentT = normalize(worldTangentT);

    // Transform into projection space
    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);

    vs_ProjPos_WrinkleWeight.xyz = vProjPos.xyz;
    vs_ProjPos_WrinkleWeight.w = flWrinkleWeight;

    // Needed for water fog alpha and diffuse lighting
    // FIXME: we shouldn't have to compute this all the time.
    vs_WorldPos_Atten3.xyz = worldPos;

    // Needed for specular
    vs_WorldVertToEyeVector = cEyePos - worldPos;

    InitLightInfo();

    // Compute bumped lighting
    // FIXME: We shouldn't have to compute this for unlit materials
#if !USE_STATIC_CONTROL_FLOW
    vs_LightAtten.xyz = vec3(0, 0, 0);
    vs_WorldPos_Atten3.w = 0.0;
#if (NUM_LIGHTS > 0)
    vs_LightAtten.x = GetVertexAttenForLight(worldPos, 0, false);
#endif
#if (NUM_LIGHTS > 1)
    vs_LightAtten.y = GetVertexAttenForLight(worldPos, 1, false);
#endif
#if (NUM_LIGHTS > 2)
    vs_LightAtten.z = GetVertexAttenForLight(worldPos, 2, false);
#endif
#if (NUM_LIGHTS > 3)
    vs_WorldPos_Atten3.w = GetVertexAttenForLight(worldPos, 3, false);
#endif
#else
    vs_LightAtten.x = GetVertexAttenForLight(worldPos, 0, true);
    vs_LightAtten.y = GetVertexAttenForLight(worldPos, 1, true);
    vs_LightAtten.z = GetVertexAttenForLight(worldPos, 2, true);
    vs_WorldPos_Atten3.w = GetVertexAttenForLight(worldPos, 3, true);
#endif

    // Base texture coordinate transform
    vs_BaseTexCoord.x = dot(v_TexCoord0, cBaseTexCoordTransform0);
    vs_BaseTexCoord.y = dot(v_TexCoord0, cBaseTexCoordTransform1);
    vs_BaseTexCoord.z = dot(v_TexCoord0, cDetailTexCoordTransform0);
    vs_BaseTexCoord.w = dot(v_TexCoord0, cDetailTexCoordTransform1);

    // Tangent space transform
    vs_TangentSpaceTranspose[0] = vec3(worldTangentS.x, worldTangentT.x, worldNormal.x);
    vs_TangentSpaceTranspose[1] = vec3(worldTangentS.y, worldTangentT.y, worldNormal.y);
    vs_TangentSpaceTranspose[2] = vec3(worldTangentS.z, worldTangentT.z, worldNormal.z);
}
