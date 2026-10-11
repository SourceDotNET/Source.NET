#version 460
//  STATIC: "VERTEXCOLOR"				"0..1"
//	STATIC: "CUBEMAP"					"0..1"
//  STATIC: "HALFLAMBERT"				"0..1"
//  STATIC: "FLASHLIGHT"				"0..1"
//  STATIC: "SEAMLESS_BASE"         	"0..1"
//  STATIC: "SEAMLESS_DETAIL"       	"0..1"
//  STATIC: "SEPARATE_DETAIL_UVS"   	"0..1"
//  STATIC: "USE_STATIC_CONTROL_FLOW"	"0..1"
//  STATIC: "DONT_GAMMA_CONVERT_VERTEX_COLOR" "0..1"
//	DYNAMIC: "DYNAMIC_LIGHT"			"0..1"
//	DYNAMIC: "STATIC_LIGHT"				"0..1"
//	DYNAMIC: "SKINNING"					"0..1"
//  DYNAMIC: "LIGHTING_PREVIEW"			"0..1"
//  DYNAMIC: "NUM_LIGHTS"				"0..2"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Color;
layout(location = 3) in vec4 v_Specular;
layout(location = 7) in ivec4 v_BoneIndex;
layout(location = 8) in vec2 v_BoneWeights;
layout(location = 10) in vec4 v_TexCoord0;
layout(location = 11) in vec4 v_TexCoord1;
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
const int SHADER_SPECIFIC_CONST_2 = 50;
const int SHADER_SPECIFIC_CONST_4 = 52;

#include "common_gl460.vs"
#include "common_lighting_gl460.vs"
#include "common_morph_gl460.vs"
#include "common_skinning_gl460.vs"

#define cBaseTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_0 + 0]
#define cBaseTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_0 + 1]
#define cSeamlessScale				vs_const[SHADER_SPECIFIC_CONST_2]
#define SEAMLESS_SCALE				cSeamlessScale.x
#define cDetailTexCoordTransform0	vs_const[SHADER_SPECIFIC_CONST_4 + 0]
#define cDetailTexCoordTransform1	vs_const[SHADER_SPECIFIC_CONST_4 + 1]

const bool g_bSkinning		= SKINNING != 0;
const bool g_bVertexColor	= VERTEXCOLOR != 0;
const bool g_bHalfLambert	= HALFLAMBERT != 0;

#if SEAMLESS_BASE
out vec3 vs_SeamlessTexCoord;		// Base texture x/y/z (indexed by swizzle)
#else
out vec2 vs_BaseTexCoord;			// Base texture coordinate
#endif
#if SEAMLESS_DETAIL
out vec3 vs_SeamlessDetailTexCoord;	// Detail texture coordinate
#else
out vec2 vs_DetailTexCoord;			// Detail texture coordinate
#endif
out vec4 vs_Color;					// Vertex color (from lighting or unlit)

#if CUBEMAP
out vec3 vs_WorldVertToEyeVector;	// Necessary for cubemaps
#endif

out vec3 vs_WorldSpaceNormal;		// Necessary for cubemaps and flashlight

out vec4 vs_ProjPos;
out vec4 vs_WorldPos_ProjPosZ;
#if SEAMLESS_DETAIL || SEAMLESS_BASE
out vec3 vs_SeamlessWeights;		// x y z projection weights
#endif

#include "common_clipplanes_gl460.vs"

void main()
{
    bool bDynamicLight = DYNAMIC_LIGHT != 0;
    bool bStaticLight = STATIC_LIGHT != 0;
    bool bDoLighting = !g_bVertexColor && (bDynamicLight || bStaticLight);

    vec4 vPosition = vec4(v_Position, 1.0);
    vec3 vNormal = v_Normal;

    ApplyMorph(v_FlexPosition.xyz, v_FlexNormal, vPosition.xyz, vNormal);

#if SEAMLESS_BASE || SEAMLESS_DETAIL
    // compute blend weights in rgb
    vec3 NNormal = normalize(vNormal);
    vs_SeamlessWeights.xyz = NNormal * NNormal;				// sums to 1.
#endif

    // Perform skinning
    vec3 worldNormal, worldPos;
    SkinPositionAndNormal(
        g_bSkinning,
        vPosition, vNormal,
        v_BoneIndex, v_BoneWeights,
        worldPos, worldNormal);

    if (!g_bVertexColor)
    {
        worldNormal = normalize(worldNormal);
    }

    vs_WorldSpaceNormal = worldNormal;

    // Transform into projection space
    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);

    vs_ProjPos = vProjPos;
    vs_WorldPos_ProjPosZ.xyz = worldPos.xyz;
    vs_WorldPos_ProjPosZ.w = vProjPos.z;

    // Needed for cubemaps
#if CUBEMAP
    vs_WorldVertToEyeVector.xyz = cEyePos - worldPos;
#endif

#if FLASHLIGHT
    vs_Color = vec4(0.0, 0.0, 0.0, 0.0);
#else
    if (g_bVertexColor)
    {
        // Assume that this is unlitgeneric if you are using vertex color.
        vs_Color.rgb = (DONT_GAMMA_CONVERT_VERTEX_COLOR != 0) ? v_Color.rgb : GammaToLinear(v_Color.rgb);
        vs_Color.a = v_Color.a;
    }
    else
    {
        InitLightInfo();
#if USE_STATIC_CONTROL_FLOW
        {
            vs_Color.xyz = DoLighting(worldPos, worldNormal, v_Specular.rgb, bStaticLight, bDynamicLight, g_bHalfLambert);
        }
#else
        {
            vs_Color.xyz = DoLightingUnrolled(worldPos, worldNormal, v_Specular.rgb, bStaticLight, bDynamicLight, g_bHalfLambert, NUM_LIGHTS);
        }
#endif
    }
#endif

#if SEAMLESS_BASE
    vs_SeamlessTexCoord.xyz = SEAMLESS_SCALE * v_Position.xyz;
#else
    // Base texture coordinates
    vs_BaseTexCoord.x = dot(v_TexCoord0, cBaseTexCoordTransform0);
    vs_BaseTexCoord.y = dot(v_TexCoord0, cBaseTexCoordTransform1);
#endif

#if SEAMLESS_DETAIL
    // FIXME: detail texcoord as a 2d xform doesn't make much sense here, so I just do enough so
    // that scale works. More smartness could allow 3d xform.
    vs_SeamlessDetailTexCoord.xyz = (SEAMLESS_SCALE * cDetailTexCoordTransform0.x) * v_Position.xyz;
#else
    // Detail texture coordinates
    // FIXME: This shouldn't have to be computed all the time.
    vs_DetailTexCoord.x = dot(v_TexCoord0, cDetailTexCoordTransform0);
    vs_DetailTexCoord.y = dot(v_TexCoord0, cDetailTexCoordTransform1);
#endif

#if SEPARATE_DETAIL_UVS
    vs_DetailTexCoord.xy = v_TexCoord1.xy;
#endif

#if LIGHTING_PREVIEW
    float d = (0.5 + 0.5 * worldNormal * vec3(0.7071, 0.7071, 0)).x;
    vs_Color.xyz = vec3(d, d, d);
#endif
}
