#version 460
//	STATIC: "ENVMAP_MASK"				"0..1"
//	STATIC: "TANGENTSPACE"				"0..1"
//  STATIC: "BUMPMAP"					"0..1"
//  STATIC: "DIFFUSEBUMPMAP"			"0..1"
//  STATIC: "VERTEXCOLOR"				"0..1"
//  STATIC: "VERTEXALPHATEXBLENDFACTOR"	"0..1"
//  STATIC: "SEAMLESS"                  "0..1"
//  STATIC: "BUMPMASK"                  "0..1"

//  DYNAMIC: "FASTPATH"					"0..1"
//  DYNAMIC: "LIGHTING_PREVIEW"			"0..1"

layout(location = 0) in vec3 v_Position;
layout(location = 1) in vec3 v_Normal;
layout(location = 2) in vec4 v_Color;
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

const int VERTEX_SHADER_MODULATION_COLOR = 47;
const int SHADER_SPECIFIC_CONST_0 = 48;
const int SHADER_SPECIFIC_CONST_2 = 50;
const int SHADER_SPECIFIC_CONST_4 = 52;
const int SHADER_SPECIFIC_CONST_10 = 58;

#include "common_gl460.vs"

#define cModulationColor			vs_const[VERTEX_SHADER_MODULATION_COLOR]

#if SEAMLESS
#define SeamlessScale				vs_const[SHADER_SPECIFIC_CONST_0]
#define SEAMLESS_SCALE				(SeamlessScale.x)
#else
#define cBaseTexCoordTransform0				vs_const[SHADER_SPECIFIC_CONST_0 + 0]
#define cBaseTexCoordTransform1				vs_const[SHADER_SPECIFIC_CONST_0 + 1]
#define cDetailOrBumpTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_2 + 0]
#define cDetailOrBumpTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_2 + 1]
#endif
// This should be identity if we are bump mapping, otherwise we'll screw up the lightmapTexCoordOffset.
#define cEnvmapMaskTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_4 + 0]
#define cEnvmapMaskTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_4 + 1]
#define cBlendMaskTexCoordTransform0		vs_const[SHADER_SPECIFIC_CONST_10 + 0]	// not contiguous with the rest!
#define cBlendMaskTexCoordTransform1		vs_const[SHADER_SPECIFIC_CONST_10 + 1]

const bool g_UseSeparateEnvmapMask			= ENVMAP_MASK != 0;
const bool g_bBumpmap						= BUMPMAP != 0;
const bool g_bBumpmapDiffuseLighting		= DIFFUSEBUMPMAP != 0;
const bool g_bVertexColor					= VERTEXCOLOR != 0;
const bool g_bVertexAlphaTexBlendFactor		= VERTEXALPHATEXBLENDFACTOR != 0;
const bool g_BumpMask						= BUMPMASK != 0;

#if SEAMLESS
out vec3 vs_SeamlessTexCoord;						// x y z
out vec4 vs_DetailOrBumpAndEnvmapMaskTexCoord;		// envmap mask
#else
out vec2 vs_BaseTexCoord;
// detail textures and bumpmaps are mutually exclusive so that we have enough texcoords.
out vec4 vs_DetailOrBumpAndEnvmapMaskTexCoord;
#endif
centroid out vec4 vs_LightmapTexCoord1And2;
centroid out vec4 vs_LightmapTexCoord3;						// and basetexcoord*mask_scale
out vec4 vs_WorldPos_ProjPosZ;

#if TANGENTSPACE || (LIGHTING_PREVIEW)
out mat3 vs_TangentSpaceTranspose;
#endif

out vec4 vs_Color;									// in seamless, r g b = blend weights
out float vs_VertexBlendX;

#include "common_clipplanes_gl460.vs"

void main()
{
#if SEAMLESS
    vs_SeamlessTexCoord = vec3(0.0);
    vs_DetailOrBumpAndEnvmapMaskTexCoord = vec4(0.0);
#else
    vs_BaseTexCoord = vec2(0.0);
    vs_DetailOrBumpAndEnvmapMaskTexCoord = vec4(0.0);
#endif
    vs_LightmapTexCoord1And2 = vec4(0.0);
    vs_LightmapTexCoord3 = vec4(0.0);
    vs_Color = vec4(0.0);

    vec3 vObjNormal = v_Normal;

    vec3 worldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;

    vec4 vProjPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    gl_Position = vProjPos;
    WriteUserClipDistances(gl_Position);

    vs_WorldPos_ProjPosZ = vec4(worldPos, vProjPos.z);

    vec3 worldNormal = mat3(modelMatrix) * vObjNormal;

#if TANGENTSPACE || (LIGHTING_PREVIEW)
    vec3 worldTangentS = mat3(modelMatrix) * v_TangentS;
    vec3 worldTangentT = mat3(modelMatrix) * v_TangentT;

    vs_TangentSpaceTranspose[0] = worldTangentS;
    vs_TangentSpaceTranspose[1] = worldTangentT;
    vs_TangentSpaceTranspose[2] = worldNormal;
#endif

#if SEAMLESS
    {
        // we need to fill in the texture coordinate projections
        vs_SeamlessTexCoord = SEAMLESS_SCALE * worldPos;
    }
#else
    {
        if (FASTPATH != 0)
        {
            vs_BaseTexCoord.xy = v_TexCoord0;
        }
        else
        {
            vs_BaseTexCoord.x = dot(v_TexCoord0, cBaseTexCoordTransform0.xy) + cBaseTexCoordTransform0.w;
            vs_BaseTexCoord.y = dot(v_TexCoord0, cBaseTexCoordTransform1.xy) + cBaseTexCoordTransform1.w;
        }
        {
            // calculate detailorbumptexcoord
            if (FASTPATH != 0)
                vs_DetailOrBumpAndEnvmapMaskTexCoord.xy = v_TexCoord0.xy;
            else
            {
                vs_DetailOrBumpAndEnvmapMaskTexCoord.x = dot(v_TexCoord0, cDetailOrBumpTexCoordTransform0.xy) + cDetailOrBumpTexCoordTransform0.w;
                vs_DetailOrBumpAndEnvmapMaskTexCoord.y = dot(v_TexCoord0, cDetailOrBumpTexCoordTransform1.xy) + cDetailOrBumpTexCoordTransform1.w;
            }
        }
    }
#endif
    if (FASTPATH != 0)
    {
        vs_LightmapTexCoord3.zw = v_TexCoord0;
    }
    else
    {
        vs_LightmapTexCoord3.z = dot(v_TexCoord0, cBlendMaskTexCoordTransform0.xy) + cBlendMaskTexCoordTransform0.w;
        vs_LightmapTexCoord3.w = dot(v_TexCoord0, cBlendMaskTexCoordTransform1.xy) + cBlendMaskTexCoordTransform1.w;
    }

    //  compute lightmap coordinates
    if (g_bBumpmap && g_bBumpmapDiffuseLighting)
    {
        vs_LightmapTexCoord1And2.xy = v_TexCoord1 + v_TexCoord2;

        vec2 lightmapTexCoord2 = vs_LightmapTexCoord1And2.xy + v_TexCoord2;
        vec2 lightmapTexCoord3 = lightmapTexCoord2 + v_TexCoord2;

        // reversed component order
        vs_LightmapTexCoord1And2.w = lightmapTexCoord2.x;
        vs_LightmapTexCoord1And2.z = lightmapTexCoord2.y;

        vs_LightmapTexCoord3.xy = lightmapTexCoord3;
    }
    else
    {
        vs_LightmapTexCoord1And2.xy = v_TexCoord1;
    }

    if (g_UseSeparateEnvmapMask || g_BumpMask)
    {
        // reversed component order
#	if FASTPATH
        vs_DetailOrBumpAndEnvmapMaskTexCoord.wz = v_TexCoord0.xy;
#	else
        vs_DetailOrBumpAndEnvmapMaskTexCoord.w = dot(v_TexCoord0, cEnvmapMaskTexCoordTransform0.xy) + cEnvmapMaskTexCoordTransform0.w;
        vs_DetailOrBumpAndEnvmapMaskTexCoord.z = dot(v_TexCoord0, cEnvmapMaskTexCoordTransform1.xy) + cEnvmapMaskTexCoordTransform1.w;
#	endif
    }

    vs_VertexBlendX = 0.0;

    if (!g_bVertexColor)
    {
        vs_Color = vec4(1.0, 1.0, 1.0, cModulationColor.a);
    }
    else
    {
#if FASTPATH
        vs_Color = v_Color;
#else
        if (g_bVertexAlphaTexBlendFactor)
        {
            vs_Color.rgb = v_Color.rgb;
            vs_Color.a = cModulationColor.a;
        }
        else
        {
            vs_Color = v_Color;
            vs_Color.a *= cModulationColor.a;
        }
#endif
    }
#if SEAMLESS
    // compute belnd weights in rgb
    vec3 vNormal = normalize(worldNormal);
    vs_Color.xyz = vNormal * vNormal;           // sums to 1.
#endif

    if (g_bVertexAlphaTexBlendFactor)
    {
        vs_VertexBlendX = v_Color.a;
    }
}
