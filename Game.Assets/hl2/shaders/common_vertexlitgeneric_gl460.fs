#ifndef COMMON_VERTEXLITGENERIC_GL460_FS
#define COMMON_VERTEXLITGENERIC_GL460_FS

#include "common_gl460.fs"

//  We store four light colors and positions in an
//  array of three of these structures like so:
//
//       x		y		z      w
//    +------+------+------+------+
//    |       L0.rgb       |      |
//    +------+------+------+      |
//    |       L0.pos       |  L3  |
//    +------+------+------+  rgb |
//    |       L1.rgb       |      |
//    +------+------+------+------+
//    |       L1.pos       |      |
//    +------+------+------+      |
//    |       L2.rgb       |  L3  |
//    +------+------+------+  pos |
//    |       L2.pos       |      |
//    +------+------+------+------+
//
struct PixelShaderLightInfo
{
    vec4 color;
    vec4 pos;
};

#define cOverbright 2.0

// Better suited to Pixel shader models, 11 instructions in pixel shader
// ... actually, now only 9: mul, cmp, cmp, mul, mad, mad, mad, mad, mad
vec3 PixelShaderAmbientLight(vec3 worldNormal, vec3 cAmbientCube[6])
{
    vec3 linearColor, nSquared = worldNormal * worldNormal;
    vec3 isNegative = mix(nSquared, vec3(0.0), greaterThanEqual(worldNormal, vec3(0.0)));
    vec3 isPositive = mix(vec3(0.0), nSquared, greaterThanEqual(worldNormal, vec3(0.0)));
    linearColor = isPositive.x * cAmbientCube[0] + isNegative.x * cAmbientCube[1] +
                  isPositive.y * cAmbientCube[2] + isNegative.y * cAmbientCube[3] +
                  isPositive.z * cAmbientCube[4] + isNegative.z * cAmbientCube[5];
    return linearColor;
}

vec3 AmbientLight(vec3 worldNormal, vec3 cAmbientCube[6])
{
    // Pixel shader case
    return PixelShaderAmbientLight(worldNormal, cAmbientCube);
}

//-----------------------------------------------------------------------------
// Purpose: Compute scalar diffuse term with various optional tweaks such as
//          Half Lambert and ambient occlusion
//-----------------------------------------------------------------------------
vec3 DiffuseTerm(bool bHalfLambert, vec3 worldNormal, vec3 lightDir,
                 bool bDoAmbientOcclusion, float fAmbientOcclusion,
                 bool bDoLightingWarp, sampler2D lightWarpSampler)
{
    float fResult;

    float NDotL = dot(worldNormal, lightDir);				// Unsaturated dot (-1 to 1 range)

    if (bHalfLambert)
    {
        fResult = clamp(NDotL * 0.5 + 0.5, 0.0, 1.0);		// Scale and bias to 0 to 1 range

        if (!bDoLightingWarp)
        {
            fResult *= fResult;								// Square
        }
    }
    else
    {
        fResult = clamp(NDotL, 0.0, 1.0);					// Saturate pure Lambertian term
    }

    if (bDoAmbientOcclusion)
    {
        // Raise to higher powers for darker AO values
        fResult *= fAmbientOcclusion;
    }

    vec3 fOut = vec3(fResult, fResult, fResult);
    if (bDoLightingWarp)
    {
        fOut = 2.0 * texture(lightWarpSampler, vec2(fResult, 0.0)).xyz;
    }

    return fOut;
}

vec3 PixelShaderDoGeneralDiffuseLight(float fAtten, vec3 worldPos, vec3 worldNormal,
                                      vec3 vPosition, vec3 vColor, bool bHalfLambert,
                                      bool bDoAmbientOcclusion, float fAmbientOcclusion,
                                      bool bDoLightingWarp, sampler2D lightWarpSampler)
{
    vec3 lightDir = normalize(vPosition - worldPos);
    return vColor * fAtten * DiffuseTerm(bHalfLambert, worldNormal, lightDir, bDoAmbientOcclusion, fAmbientOcclusion, bDoLightingWarp, lightWarpSampler);
}

vec3 PixelShaderGetLightVector(vec3 worldPos, PixelShaderLightInfo cLightInfo[3], int nLightIndex)
{
    if (nLightIndex == 3)
    {
        // Unpack light 3 from w components...
        vec3 vLight3Pos = vec3(cLightInfo[1].pos.w, cLightInfo[2].color.w, cLightInfo[2].pos.w);
        return normalize(vLight3Pos - worldPos);
    }
    else
    {
        vec4 world4Pos = vec4(worldPos.x, worldPos.y, worldPos.z, 0.0);
        return normalize(cLightInfo[nLightIndex].pos - world4Pos).xyz;
    }
}

vec3 PixelShaderGetLightColor(PixelShaderLightInfo cLightInfo[3], int nLightIndex)
{
    if (nLightIndex == 3)
    {
        // Unpack light 3 from w components...
        return vec3(cLightInfo[0].color.w, cLightInfo[0].pos.w, cLightInfo[1].color.w);
    }
    else
    {
        return cLightInfo[nLightIndex].color.rgb;
    }
}

void SpecularAndRimTerms(vec3 vWorldNormal, vec3 vLightDir, float fSpecularExponent,
                         vec3 vEyeDir, bool bDoAmbientOcclusion, float fAmbientOcclusion,
                         bool bDoSpecularWarp, sampler2D specularWarpSampler, float fFresnel,
                         vec3 color, bool bDoRimLighting, float fRimExponent,

                         // Outputs
                         out vec3 specularLighting, out vec3 rimLighting)
{
    rimLighting = vec3(0.0, 0.0, 0.0);

    vec3 vReflect = 2.0 * vWorldNormal * dot(vWorldNormal, vEyeDir) - vEyeDir; // Reflect view through normal
    float LdotR = clamp(dot(vReflect, vLightDir), 0.0, 1.0);				// L.R	(use half-angle instead?)
    specularLighting = vec3(pow(LdotR, fSpecularExponent));					// Raise to specular exponent

    // Optionally warp as function of scalar specular and fresnel
    if (bDoSpecularWarp)
        specularLighting *= texture(specularWarpSampler, vec2(specularLighting.x, fFresnel)).xyz; // Sample at { (L.R)^k, fresnel }

    specularLighting *= clamp(dot(vWorldNormal, vLightDir), 0.0, 1.0);		// Mask with N.L
    specularLighting *= color;												// Modulate with light color

    if (bDoAmbientOcclusion)												// Optionally modulate with ambient occlusion
        specularLighting *= fAmbientOcclusion;

    if (bDoRimLighting)														// Optionally do rim lighting
    {
        rimLighting = vec3(pow(LdotR, fRimExponent));						// Raise to rim exponent
        rimLighting *= clamp(dot(vWorldNormal, vLightDir), 0.0, 1.0);		// Mask with N.L
        rimLighting *= color;												// Modulate with light color
    }
}

// Traditional fresnel term approximation
float Fresnel(vec3 vNormal, vec3 vEyeDir)
{
    float fresnel = clamp(1.0 - dot(vNormal, vEyeDir), 0.0, 1.0);		// 1-(N.V) for Fresnel term
    return fresnel * fresnel;											// Square for a more subtle look
}

// Traditional fresnel term approximation which uses 4th power (square twice)
float Fresnel4(vec3 vNormal, vec3 vEyeDir)
{
    float fresnel = clamp(1.0 - dot(vNormal, vEyeDir), 0.0, 1.0);		// 1-(N.V) for Fresnel term
    fresnel = fresnel * fresnel;										// Square
    return fresnel * fresnel;											// Square again for a more subtle look
}

//
// Custom Fresnel with low, mid and high parameters defining a piecewise continuous function
// with traditional fresnel (0 to 1 range) as input.  The 0 to 0.5 range blends between
// low and mid while the 0.5 to 1 range blends between mid and high
//
//    |
//    |    .  M . . . H
//    | .
//    L
//    |
//    +----------------
//    0               1
//
float Fresnel(vec3 vNormal, vec3 vEyeDir, vec3 vRanges)
{
    // note: vRanges is now encoded as ((mid-min)*2, mid, (max-mid)*2) to optimize math
    float f = clamp(1.0 - dot(vNormal, vEyeDir), 0.0, 1.0);
    f = f * f - 0.5;
    return vRanges.y + (f >= 0.0 ? vRanges.z : vRanges.x) * f;
}

void PixelShaderDoSpecularLight(vec3 vWorldPos, vec3 vWorldNormal, float fSpecularExponent, vec3 vEyeDir,
                                float fAtten, vec3 vLightColor, vec3 vLightDir,
                                bool bDoAmbientOcclusion, float fAmbientOcclusion,
                                bool bDoSpecularWarp, sampler2D specularWarpSampler, float fFresnel,
                                bool bDoRimLighting, float fRimExponent,

                                // Outputs
                                out vec3 specularLighting, out vec3 rimLighting)
{
    // Compute Specular and rim terms
    SpecularAndRimTerms(vWorldNormal, vLightDir, fSpecularExponent,
                        vEyeDir, bDoAmbientOcclusion, fAmbientOcclusion,
                        bDoSpecularWarp, specularWarpSampler, fFresnel, vLightColor * fAtten,
                        bDoRimLighting, fRimExponent, specularLighting, rimLighting);
}

vec3 PixelShaderDoLightingLinear(vec3 worldPos, vec3 worldNormal,
                                 vec3 staticLightingColor, bool bStaticLight,
                                 bool bAmbientLight, vec4 lightAtten, vec3 cAmbientCube[6],
                                 int nNumLights, PixelShaderLightInfo cLightInfo[3],
                                 bool bHalfLambert, bool bDoAmbientOcclusion, float fAmbientOcclusion,
                                 bool bDoLightingWarp, sampler2D lightWarpSampler)
{
    vec3 linearColor = vec3(0.0);

    if (bStaticLight)
    {
        // The static lighting comes in in gamma space and has also been premultiplied by $cOOOverbright
        // need to get it into
        // linear space so that we can do adds.
        linearColor += GammaToLinear(staticLightingColor * cOverbright);
    }

    if (bAmbientLight)
    {
        vec3 ambient = AmbientLight(worldNormal, cAmbientCube);

        if (bDoAmbientOcclusion)
            ambient *= fAmbientOcclusion * fAmbientOcclusion;	// Note squaring...

        linearColor += ambient;
    }

    if (nNumLights > 0)
    {
        linearColor += PixelShaderDoGeneralDiffuseLight(lightAtten.x, worldPos, worldNormal,
                                                        cLightInfo[0].pos.xyz, cLightInfo[0].color.xyz, bHalfLambert,
                                                        bDoAmbientOcclusion, fAmbientOcclusion,
                                                        bDoLightingWarp, lightWarpSampler);
        if (nNumLights > 1)
        {
            linearColor += PixelShaderDoGeneralDiffuseLight(lightAtten.y, worldPos, worldNormal,
                                                            cLightInfo[1].pos.xyz, cLightInfo[1].color.xyz, bHalfLambert,
                                                            bDoAmbientOcclusion, fAmbientOcclusion,
                                                            bDoLightingWarp, lightWarpSampler);
            if (nNumLights > 2)
            {
                linearColor += PixelShaderDoGeneralDiffuseLight(lightAtten.z, worldPos, worldNormal,
                                                                cLightInfo[2].pos.xyz, cLightInfo[2].color.xyz, bHalfLambert,
                                                                bDoAmbientOcclusion, fAmbientOcclusion,
                                                                bDoLightingWarp, lightWarpSampler);
                if (nNumLights > 3)
                {
                    // Unpack the 4th light's data from tight constant packing
                    vec3 vLight3Color = vec3(cLightInfo[0].color.w, cLightInfo[0].pos.w, cLightInfo[1].color.w);
                    vec3 vLight3Pos = vec3(cLightInfo[1].pos.w, cLightInfo[2].color.w, cLightInfo[2].pos.w);
                    linearColor += PixelShaderDoGeneralDiffuseLight(lightAtten.w, worldPos, worldNormal,
                                                                    vLight3Pos, vLight3Color, bHalfLambert,
                                                                    bDoAmbientOcclusion, fAmbientOcclusion,
                                                                    bDoLightingWarp, lightWarpSampler);
                }
            }
        }
    }

    return linearColor;
}

void PixelShaderDoSpecularLighting(vec3 worldPos, vec3 worldNormal, float fSpecularExponent, vec3 vEyeDir,
                                   vec4 lightAtten, int nNumLights, PixelShaderLightInfo cLightInfo[3],
                                   bool bDoAmbientOcclusion, float fAmbientOcclusion,
                                   bool bDoSpecularWarp, sampler2D specularWarpSampler, float fFresnel,
                                   bool bDoRimLighting, float fRimExponent,

                                   // Outputs
                                   out vec3 specularLighting, out vec3 rimLighting)
{
    specularLighting = rimLighting = vec3(0.0, 0.0, 0.0);
    vec3 localSpecularTerm, localRimTerm;

    if (nNumLights > 0)
    {
        PixelShaderDoSpecularLight(worldPos, worldNormal, fSpecularExponent, vEyeDir,
                                   lightAtten.x, PixelShaderGetLightColor(cLightInfo, 0),
                                   PixelShaderGetLightVector(worldPos, cLightInfo, 0),
                                   bDoAmbientOcclusion, fAmbientOcclusion,
                                   bDoSpecularWarp, specularWarpSampler, fFresnel,
                                   bDoRimLighting, fRimExponent,
                                   localSpecularTerm, localRimTerm);

        specularLighting += localSpecularTerm;		// Accumulate specular and rim terms
        rimLighting += localRimTerm;
    }

    if (nNumLights > 1)
    {
        PixelShaderDoSpecularLight(worldPos, worldNormal, fSpecularExponent, vEyeDir,
                                   lightAtten.y, PixelShaderGetLightColor(cLightInfo, 1),
                                   PixelShaderGetLightVector(worldPos, cLightInfo, 1),
                                   bDoAmbientOcclusion, fAmbientOcclusion,
                                   bDoSpecularWarp, specularWarpSampler, fFresnel,
                                   bDoRimLighting, fRimExponent,
                                   localSpecularTerm, localRimTerm);

        specularLighting += localSpecularTerm;		// Accumulate specular and rim terms
        rimLighting += localRimTerm;
    }

    if (nNumLights > 2)
    {
        PixelShaderDoSpecularLight(worldPos, worldNormal, fSpecularExponent, vEyeDir,
                                   lightAtten.z, PixelShaderGetLightColor(cLightInfo, 2),
                                   PixelShaderGetLightVector(worldPos, cLightInfo, 2),
                                   bDoAmbientOcclusion, fAmbientOcclusion,
                                   bDoSpecularWarp, specularWarpSampler, fFresnel,
                                   bDoRimLighting, fRimExponent,
                                   localSpecularTerm, localRimTerm);

        specularLighting += localSpecularTerm;		// Accumulate specular and rim terms
        rimLighting += localRimTerm;
    }

    if (nNumLights > 3)
    {
        PixelShaderDoSpecularLight(worldPos, worldNormal, fSpecularExponent, vEyeDir,
                                   lightAtten.w, PixelShaderGetLightColor(cLightInfo, 3),
                                   PixelShaderGetLightVector(worldPos, cLightInfo, 3),
                                   bDoAmbientOcclusion, fAmbientOcclusion,
                                   bDoSpecularWarp, specularWarpSampler, fFresnel,
                                   bDoRimLighting, fRimExponent,
                                   localSpecularTerm, localRimTerm);

        specularLighting += localSpecularTerm;		// Accumulate specular and rim terms
        rimLighting += localRimTerm;
    }
}

// Called directly by newer shaders or through the following wrapper for older shaders
vec3 PixelShaderDoLighting(vec3 worldPos, vec3 worldNormal,
                           vec3 staticLightingColor, bool bStaticLight,
                           bool bAmbientLight, vec4 lightAtten, vec3 cAmbientCube[6],
                           int nNumLights, PixelShaderLightInfo cLightInfo[3],
                           bool bHalfLambert,

                           // New optional/experimental parameters
                           bool bDoAmbientOcclusion, float fAmbientOcclusion,
                           bool bDoLightingWarp, sampler2D lightWarpSampler)
{
    vec3 linearColor = PixelShaderDoLightingLinear(worldPos, worldNormal, staticLightingColor,
                                                   bStaticLight, bAmbientLight, lightAtten,
                                                   cAmbientCube, nNumLights, cLightInfo, bHalfLambert,
                                                   bDoAmbientOcclusion, fAmbientOcclusion,
                                                   bDoLightingWarp, lightWarpSampler);

    return linearColor;
}

#endif // COMMON_VERTEXLITGENERIC_GL460_FS
