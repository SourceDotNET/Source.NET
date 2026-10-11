#ifndef COMMON_LIGHTING_GL460_VS
#define COMMON_LIGHTING_GL460_VS

#include "common_gl460.glsl"
#include "common_vertex_shared_gl460.vs"

#define cOverbright			2.0

const int VERTEX_SHADER_AMBIENT_LIGHT = 21;
const int VERTEX_SHADER_LIGHT_INFO = 27;

#define g_bLightEnabled		lightEnabled
#define g_nLightCount		lightCount

struct LightInfo
{
    vec4 color;
    vec4 dir;
    vec4 pos;
    vec4 spotParams;
    vec4 atten;
};

// Four lights x 5 constants each = 20 constants
LightInfo cLightInfo[4];

void InitLightInfo()
{
    for (int i = 0; i < 4; ++i)
    {
        cLightInfo[i].color      = vs_const[VERTEX_SHADER_LIGHT_INFO + i * 5 + 0];
        cLightInfo[i].dir        = vs_const[VERTEX_SHADER_LIGHT_INFO + i * 5 + 1];
        cLightInfo[i].pos        = vs_const[VERTEX_SHADER_LIGHT_INFO + i * 5 + 2];
        cLightInfo[i].spotParams = vs_const[VERTEX_SHADER_LIGHT_INFO + i * 5 + 3];
        cLightInfo[i].atten      = vs_const[VERTEX_SHADER_LIGHT_INFO + i * 5 + 4];
    }
}

vec3 AmbientLight(vec3 worldNormal)
{
    vec3 nSquared = worldNormal * worldNormal;
    ivec3 isNegative = ivec3(lessThan(worldNormal, vec3(0.0)));
    vec3 color;
    color  = nSquared.x * vs_const[VERTEX_SHADER_AMBIENT_LIGHT + isNegative.x].rgb;
    color += nSquared.y * vs_const[VERTEX_SHADER_AMBIENT_LIGHT + 2 + isNegative.y].rgb;
    color += nSquared.z * vs_const[VERTEX_SHADER_AMBIENT_LIGHT + 4 + isNegative.z].rgb;
    return color;
}

// The following "internal" routines are called "privately" by other routines in this file which
// handle the particular flavor of vs20 control flow appropriate to the original caller
float VertexAttenInternal(vec3 worldPos, int lightNum)
{
    float result = 0.0;

    // Get light direction
    vec3 lightDir = cLightInfo[lightNum].pos.xyz - worldPos;

    // Get light distance squared.
    float lightDistSquared = dot(lightDir, lightDir);

    // Get 1/lightDistance
    float ooLightDist = inversesqrt(lightDistSquared);

    // Normalize light direction
    lightDir *= ooLightDist;

    vec3 vDist = vec3(1.0, lightDistSquared * ooLightDist, lightDistSquared);

    float flDistanceAtten = 1.0 / dot(cLightInfo[lightNum].atten.xyz, vDist);

    // Spot attenuation
    float flCosTheta = dot(cLightInfo[lightNum].dir.xyz, -lightDir);
    float flSpotAtten = (flCosTheta - cLightInfo[lightNum].spotParams.z) * cLightInfo[lightNum].spotParams.w;
    flSpotAtten = max(0.0001, flSpotAtten);
    flSpotAtten = pow(flSpotAtten, cLightInfo[lightNum].spotParams.x);
    flSpotAtten = clamp(flSpotAtten, 0.0, 1.0);

    // Select between point and spot
    float flAtten = mix(flDistanceAtten, flDistanceAtten * flSpotAtten, cLightInfo[lightNum].dir.w);

    // Select between above and directional (no attenuation)
    result = mix(flAtten, 1.0, cLightInfo[lightNum].color.w);

    return result;
}

float CosineTermInternal(vec3 worldPos, vec3 worldNormal, int lightNum, bool bHalfLambert)
{
    // Calculate light direction assuming this is a point or spot
    vec3 lightDir = normalize(cLightInfo[lightNum].pos.xyz - worldPos);

    vec4 dirInv = -cLightInfo[lightNum].dir;
    vec4 color = cLightInfo[lightNum].color;

    // Select the above direction or the one in the structure, based upon light type
    lightDir = mix(lightDir, dirInv.xyz, color.www);

    // compute N dot L
    float NDotL = dot(worldNormal, lightDir);

    if (!bHalfLambert)
    {
        NDotL = max(0.0, NDotL);
    }
    else	// Half-Lambert
    {
        NDotL = NDotL * 0.5 + 0.5;
        NDotL = NDotL * NDotL;
    }
    return NDotL;
}

float GetVertexAttenForLight(vec3 worldPos, int lightNum, bool useStaticControlFlow)
{
    float result = 0.0;

    if (useStaticControlFlow)
    {
        if (g_bLightEnabled[lightNum] != 0.0)
            result = VertexAttenInternal(worldPos, lightNum);
    }
    else
        result = VertexAttenInternal(worldPos, lightNum);

    return result;
}

vec3 DoLightInternal(vec3 worldPos, vec3 worldNormal, int lightNum, bool bHalfLambert)
{
    return cLightInfo[lightNum].color.xyz *
        CosineTermInternal(worldPos, worldNormal, lightNum, bHalfLambert) *
        VertexAttenInternal(worldPos, lightNum);
}

vec3 DoLighting(vec3 worldPos, vec3 worldNormal,
                vec3 staticLightingColor, bool bStaticLight,
                bool bDynamicLight, bool bHalfLambert)
{
    vec3 linearColor = vec3(0.0, 0.0, 0.0);

    if (bStaticLight)			// Static light
    {
        vec3 col = staticLightingColor * cOverbright;
        linearColor += GammaToLinear(col);
    }

    if (bDynamicLight)			// Dynamic light
    {
        for (int i = 0; i < g_nLightCount; i++)
        {
            linearColor += DoLightInternal(worldPos, worldNormal, i, bHalfLambert);
        }
    }

    if (bDynamicLight)
    {
        linearColor += AmbientLight(worldNormal); //ambient light is already remapped
    }

    return linearColor;
}

vec3 DoLightingUnrolled(vec3 worldPos, vec3 worldNormal,
                        vec3 staticLightingColor, bool bStaticLight,
                        bool bDynamicLight, bool bHalfLambert, int nNumLights)
{
    vec3 linearColor = vec3(0.0, 0.0, 0.0);

    if (bStaticLight)			// Static light
        linearColor += GammaToLinear(staticLightingColor * cOverbright);

    if (bDynamicLight)			// Ambient light
    {
        if (nNumLights >= 1)
            linearColor += DoLightInternal(worldPos, worldNormal, 0, bHalfLambert);
        if (nNumLights >= 2)
            linearColor += DoLightInternal(worldPos, worldNormal, 1, bHalfLambert);
        if (nNumLights >= 3)
            linearColor += DoLightInternal(worldPos, worldNormal, 2, bHalfLambert);
        if (nNumLights >= 4)
            linearColor += DoLightInternal(worldPos, worldNormal, 3, bHalfLambert);
    }

    if (bDynamicLight)
        linearColor += AmbientLight(worldNormal); //ambient light is already remapped

    return linearColor;
}

#endif
