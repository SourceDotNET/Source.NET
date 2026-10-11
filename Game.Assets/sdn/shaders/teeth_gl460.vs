#version 460
//	STATIC: "INTRO"						"0..1"
//  STATIC: "USE_STATIC_CONTROL_FLOW"	"0..1"

//	DYNAMIC: "SKINNING"					"0..1"
//	DYNAMIC: "DYNAMIC_LIGHT"			"0..1"
//	DYNAMIC: "STATIC_LIGHT"				"0..1"
//  DYNAMIC: "NUM_LIGHTS"				"0..2"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 7) in ivec4 v_BoneIndex;
layout(location = 8) in vec2 v_BoneWeights;
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
out vec3 vs_VertAtten;
out vec4 vs_WorldPos_ProjPosZ;

#include "common_clipplanes_gl460.vs"

void main()
{
    bool bDynamicLight = DYNAMIC_LIGHT != 0;
    bool bStaticLight = STATIC_LIGHT != 0;

    vec4 vPosition = vec4(v_Position, 1.0);
    vec3 vNormal = v_Normal;

    ApplyMorph(v_FlexPosition.xyz, v_FlexNormal, vPosition.xyz, vNormal);

    vNormal = normalize(vNormal);

    vec3 worldPos, worldNormal;
    SkinPositionAndNormal(g_bSkinning, vPosition, vNormal, v_BoneIndex, v_BoneWeights, worldPos, worldNormal);

#if INTRO
    vec3 dummy = vec3(0.0, 0.0, 0.0);
    WorldSpaceVertexProcess(g_Time, modelOrigin, worldPos, worldNormal, dummy, dummy);
#endif

    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);

    vs_WorldPos_ProjPosZ = vec4(worldPos.xyz, vProjPos.z);

    InitLightInfo();
#if USE_STATIC_CONTROL_FLOW
    vec3 linearColor = DoLighting(worldPos, worldNormal, vec3(0.0, 0.0, 0.0), bStaticLight, bDynamicLight, false);
#else
    vec3 linearColor = DoLightingUnrolled(worldPos, worldNormal, vec3(0.0, 0.0, 0.0), bStaticLight, bDynamicLight, false, NUM_LIGHTS);
#endif

    vec3 vForward = cTeethLighting.xyz;
    float fIllumFactor = cTeethLighting.w;

    linearColor *= fIllumFactor * clamp(dot(worldNormal, vForward), 0.0, 1.0);

    vs_VertAtten = linearColor;
    vs_BaseTexCoord = v_TexCoord0.xy;
}
