#version 460
//	STATIC: "INTRO"						"0..1"
//  STATIC: "HALFLAMBERT"				"0..1"
//  STATIC: "USE_STATIC_CONTROL_FLOW"	"0..1"

//	DYNAMIC: "SKINNING"					"0..1"
//	DYNAMIC: "DYNAMIC_LIGHT"			"0..1"
//	DYNAMIC: "STATIC_LIGHT"				"0..1"
//  DYNAMIC: "NUM_LIGHTS"				"0..2"

layout(location = 0) in vec3 v_Position;
layout(location = 7) in ivec4 v_BoneIndex;
layout(location = 8) in vec2 v_BoneWeights;
layout(location = 10) in vec4 v_TexCoord0;
layout(location = 14) in vec4 v_FlexPosition;

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
const int SHADER_SPECIFIC_CONST_2 = 50;
const int SHADER_SPECIFIC_CONST_3 = 51;
const int SHADER_SPECIFIC_CONST_4 = 52;
const int SHADER_SPECIFIC_CONST_5 = 53;
const int SHADER_SPECIFIC_CONST_6 = 54;

#include "common_gl460.vs"
#include "common_lighting_gl460.vs"
#include "common_morph_gl460.vs"
#include "common_skinning_gl460.vs"
#include "vortwarp_gl460.vs"

const bool g_bSkinning		= SKINNING != 0;
const bool g_bHalfLambert	= HALFLAMBERT != 0;

#define cEyeOrigin			vs_const[SHADER_SPECIFIC_CONST_0].xyz
#define cHalfEyeballUp		vs_const[SHADER_SPECIFIC_CONST_1].xyz
#define cIrisProjectionU	vs_const[SHADER_SPECIFIC_CONST_2]
#define cIrisProjectionV	vs_const[SHADER_SPECIFIC_CONST_3]
#define cGlintProjectionU	vs_const[SHADER_SPECIFIC_CONST_4]
#define cGlintProjectionV	vs_const[SHADER_SPECIFIC_CONST_5]
#if INTRO
#define const4				vs_const[SHADER_SPECIFIC_CONST_6]
#define g_Time				const4.w
#define modelOrigin			const4.xyz
#endif

out vec2 vs_BaseTexCoord;
out vec2 vs_IrisTexCoord;
out vec2 vs_GlintTexCoord;
out vec3 vs_Color;
out vec4 vs_WorldPos_ProjPosZ;

#include "common_clipplanes_gl460.vs"

void main()
{
    bool bDynamicLight = DYNAMIC_LIGHT != 0;
    bool bStaticLight = STATIC_LIGHT != 0;

    vec4 vPosition = vec4(v_Position, 1.0);
    vec3 dummy = v_Position;

    ApplyMorph(v_FlexPosition.xyz, vPosition.xyz);

    vec3 worldNormal, worldPos;
    SkinPositionAndNormal(
        g_bSkinning,
        vPosition, dummy,
        v_BoneIndex, v_BoneWeights,
        worldPos, worldNormal);

#if INTRO
    WorldSpaceVertexProcess(g_Time, modelOrigin, worldPos, dummy, dummy, dummy);
#endif

    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);
    vs_WorldPos_ProjPosZ = vec4(worldPos.xyz, vProjPos.z);


    worldNormal = worldPos - cEyeOrigin;

    float normalDotUp = -dot(worldNormal, cHalfEyeballUp) * 0.5;
    worldNormal = normalize(normalDotUp * cHalfEyeballUp + worldNormal);

    InitLightInfo();
#if USE_STATIC_CONTROL_FLOW
    vs_Color = DoLighting(worldPos, worldNormal, vec3(0.0, 0.0, 0.0), bStaticLight, bDynamicLight, g_bHalfLambert);
#else
    vs_Color = DoLightingUnrolled(worldPos, worldNormal, vec3(0.0, 0.0, 0.0), bStaticLight, bDynamicLight, g_bHalfLambert, NUM_LIGHTS);
#endif

    vs_BaseTexCoord   = v_TexCoord0.xy;
    vs_IrisTexCoord.x  = dot(cIrisProjectionU,  vec4(worldPos, 1.0));
    vs_IrisTexCoord.y  = dot(cIrisProjectionV,  vec4(worldPos, 1.0));
    vs_GlintTexCoord.x = dot(cGlintProjectionU, vec4(worldPos, 1.0));
    vs_GlintTexCoord.y = dot(cGlintProjectionV, vec4(worldPos, 1.0));
}
