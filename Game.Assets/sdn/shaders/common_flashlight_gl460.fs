#ifndef COMMON_FLASHLIGHT_GL460_FS
#define COMMON_FLASHLIGHT_GL460_FS

#include "common_gl460.fs"

float DoShadowPoisson16Sample(sampler2DShadow DepthSampler, sampler2D DepthSamplerRaw, sampler2D RandomRotationSampler, vec3 vProjCoords, vec2 vScreenPos, vec4 vShadowTweaks, bool bNvidiaHardwarePCF, bool bFetch4)
{
    vec2 vPoissonOffset[8] = vec2[8]( vec2(  0.3475,  0.0042 ),
                                      vec2(  0.8806,  0.3430 ),
                                      vec2( -0.0041, -0.6197 ),
                                      vec2(  0.0472,  0.4964 ),
                                      vec2( -0.3730,  0.0874 ),
                                      vec2( -0.9217, -0.3177 ),
                                      vec2( -0.6289,  0.7388 ),
                                      vec2(  0.5744, -0.7741 ) );

    float flScaleOverMapSize = vShadowTweaks.x * 2.0;	// Tweak parameters to shader
    vec2 vNoiseOffset = vShadowTweaks.zw;
    vec4 vLightDepths = vec4(0.0), accum = vec4(0.0);
    vec2 rotOffset = vec2(0.0);

    vec2 shadowMapCenter = vProjCoords.xy;				// Center of shadow filter
    float objDepth = min(vProjCoords.z, 0.99999);		// Object depth in shadow space

    // 2D Rotation Matrix setup
    vec3 RMatTop = vec3(0.0), RMatBottom = vec3(0.0);
    RMatTop.xy = texture(RandomRotationSampler, cFlashlightScreenScale.xy * (vScreenPos * 0.5 + 0.5) + vNoiseOffset).xy * 2.0 - 1.0;
    RMatBottom.xy = vec2(-1.0, 1.0) * RMatTop.yx;	// 2x2 rotation matrix in 4-tuple

    RMatTop *= flScaleOverMapSize;				// Scale up kernel while accounting for texture resolution
    RMatBottom *= flScaleOverMapSize;

    RMatTop.z = shadowMapCenter.x;				// To be added in d2adds generated below
    RMatBottom.z = shadowMapCenter.y;

    float fResult = 0.0;

    if (bNvidiaHardwarePCF)
    {
        for (int i = 0; i < 8; i++)
        {
            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[i].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[i].xy) + RMatBottom.z;
            vLightDepths[i & 3] += texture(DepthSampler, vec3(rotOffset, objDepth));
        }

        fResult = dot(vLightDepths, vec4(0.25, 0.25, 0.25, 0.25));
    }
    else if (bFetch4)
    {
        for (int i = 0; i < 8; i++)
        {
            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[i].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[i].xy) + RMatBottom.z;
            vLightDepths = texture(DepthSamplerRaw, rotOffset.xy);
            accum += vec4(greaterThan(vLightDepths, vec4(objDepth)));
        }

        fResult = dot(accum, vec4(1.0 / 32.0, 1.0 / 32.0, 1.0 / 32.0, 1.0 / 32.0));
    }
    else	// ATI vanilla hardware shadow mapping
    {
        for (int i = 0; i < 2; i++)
        {
            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[4 * i + 0].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[4 * i + 0].xy) + RMatBottom.z;
            vLightDepths.x = texture(DepthSamplerRaw, rotOffset.xy).x;

            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[4 * i + 1].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[4 * i + 1].xy) + RMatBottom.z;
            vLightDepths.y = texture(DepthSamplerRaw, rotOffset.xy).x;

            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[4 * i + 2].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[4 * i + 2].xy) + RMatBottom.z;
            vLightDepths.z = texture(DepthSamplerRaw, rotOffset.xy).x;

            rotOffset.x = dot(RMatTop.xy,    vPoissonOffset[4 * i + 3].xy) + RMatTop.z;
            rotOffset.y = dot(RMatBottom.xy, vPoissonOffset[4 * i + 3].xy) + RMatBottom.z;
            vLightDepths.w = texture(DepthSamplerRaw, rotOffset.xy).x;

            accum += vec4(greaterThan(vLightDepths, vec4(objDepth)));
        }

        fResult = dot(accum, vec4(0.125, 0.125, 0.125, 0.125));
    }

    return fResult;
}

float DoFlashlightShadow(sampler2DShadow DepthSampler, sampler2D DepthSamplerRaw, sampler2D RandomRotationSampler, vec3 vProjCoords, vec2 vScreenPos, int nShadowLevel, vec4 vShadowTweaks, bool bAllowHighQuality)
{
    float flShadow = 1.0;

    if (nShadowLevel == NVIDIA_PCF_POISSON)
        flShadow = DoShadowPoisson16Sample(DepthSampler, DepthSamplerRaw, RandomRotationSampler, vProjCoords, vScreenPos, vShadowTweaks, true, false);
    else if (nShadowLevel == ATI_NOPCF)
        flShadow = DoShadowPoisson16Sample(DepthSampler, DepthSamplerRaw, RandomRotationSampler, vProjCoords, vScreenPos, vShadowTweaks, false, false);
    else if (nShadowLevel == ATI_NO_PCF_FETCH4)
        flShadow = DoShadowPoisson16Sample(DepthSampler, DepthSamplerRaw, RandomRotationSampler, vProjCoords, vScreenPos, vShadowTweaks, false, true);

    return flShadow;
}

vec3 SpecularLight(vec3 vWorldNormal, vec3 vLightDir, float fSpecularExponent,
                   vec3 vEyeDir, bool bDoSpecularWarp, sampler2D specularWarpSampler, float fFresnel)
{
    vec3 result = vec3(0.0, 0.0, 0.0);

    vec3 vReflect = 2.0 * vWorldNormal * dot(vWorldNormal, vEyeDir) - vEyeDir; // Reflect view through normal
    vec3 vSpecular = vec3(clamp(dot(vReflect, vLightDir), 0.0, 1.0));		// L.R	(use half-angle instead?)
    vSpecular = vec3(pow(vSpecular.x, fSpecularExponent));					// Raise to specular power

    // Optionally warp as function of scalar specular and fresnel
    if (bDoSpecularWarp)
        vSpecular *= texture(specularWarpSampler, vec2(vSpecular.x, fFresnel)).xyz; // Sample at { (L.R)^k, fresnel }

    return vSpecular;
}

void DoSpecularFlashlight(vec3 flashlightPos, vec3 worldPos, vec4 flashlightSpacePosition, vec3 worldNormal,
                    vec3 attenuationFactors, float farZ, sampler2D FlashlightSampler, sampler2DShadow FlashlightDepthSampler, sampler2D FlashlightDepthSamplerRaw, sampler2D RandomRotationSampler,
                    int nShadowLevel, bool bDoShadows, bool bAllowHighQuality, vec2 vScreenPos, float fSpecularExponent, vec3 vEyeDir,
                    bool bDoSpecularWarp, sampler2D specularWarpSampler, float fFresnel, vec4 vShadowTweaks,

                    // Outputs of this shader...separate shadowed diffuse and specular from the flashlight
                    out vec3 diffuseLighting, out vec3 specularLighting)
{
    vec3 vProjCoords = flashlightSpacePosition.xyz / flashlightSpacePosition.w;
    vec3 flashlightColor = texture(FlashlightSampler, vProjCoords.xy).xyz;

    flashlightColor *= cFlashlightColor.xyz;						// Flashlight color

    vec3 delta = flashlightPos - worldPos;
    vec3 L = normalize(delta);
    float distSquared = dot(delta, delta);
    float dist = sqrt(distSquared);

    float endFalloffFactor = RemapValClamped(dist, farZ, 0.6 * farZ, 0.0, 1.0);

    // Attenuation for light and to fade out shadow over distance
    float fAtten = clamp(dot(attenuationFactors, vec3(1.0, 1.0 / dist, 1.0 / distSquared)), 0.0, 1.0);

    // Shadowing and coloring terms
    if (bDoShadows)
    {
        float flShadow = DoFlashlightShadow(FlashlightDepthSampler, FlashlightDepthSamplerRaw, RandomRotationSampler, vProjCoords, vScreenPos, nShadowLevel, vShadowTweaks, bAllowHighQuality);
        float flAttenuated = mix(flShadow, 1.0, vShadowTweaks.y);			// Blend between fully attenuated and not attenuated
        flShadow = clamp(mix(flAttenuated, flShadow, fAtten), 0.0, 1.0);	// Blend between shadow and above, according to light attenuation
        flashlightColor *= flShadow;										// Shadow term
    }

    diffuseLighting = vec3(fAtten);
    diffuseLighting *= clamp(dot(L.xyz, worldNormal.xyz) + flFlashlightNoLambertValue, 0.0, 1.0); // Lambertian term
    diffuseLighting *= flashlightColor;
    diffuseLighting *= endFalloffFactor;

    // Specular term (masked by diffuse)
    specularLighting = diffuseLighting * SpecularLight(worldNormal, L, fSpecularExponent, vEyeDir, bDoSpecularWarp, specularWarpSampler, fFresnel);
}

// Diffuse only version
vec3 DoFlashlight(vec3 flashlightPos, vec3 worldPos, vec4 flashlightSpacePosition, vec3 worldNormal,
                  vec3 attenuationFactors, float farZ, sampler2D FlashlightSampler, sampler2DShadow FlashlightDepthSampler, sampler2D FlashlightDepthSamplerRaw,
                  sampler2D RandomRotationSampler, int nShadowLevel, bool bDoShadows, bool bAllowHighQuality,
                  vec2 vScreenPos, bool bClip, vec4 vShadowTweaks, bool bHasNormal)
{
    vec3 vProjCoords = flashlightSpacePosition.xyz / flashlightSpacePosition.w;
    vec3 flashlightColor = texture(FlashlightSampler, vProjCoords.xy).xyz;

    flashlightColor *= cFlashlightColor.xyz;						// Flashlight color

    vec3 delta = flashlightPos - worldPos;
    vec3 L = normalize(delta);
    float distSquared = dot(delta, delta);
    float dist = sqrt(distSquared);

    float endFalloffFactor = RemapValClamped(dist, farZ, 0.6 * farZ, 0.0, 1.0);

    // Attenuation for light and to fade out shadow over distance
    float fAtten = clamp(dot(attenuationFactors, vec3(1.0, 1.0 / dist, 1.0 / distSquared)), 0.0, 1.0);

    // Shadowing and coloring terms
    if (bDoShadows)
    {
        float flShadow = DoFlashlightShadow(FlashlightDepthSampler, FlashlightDepthSamplerRaw, RandomRotationSampler, vProjCoords, vScreenPos, nShadowLevel, vShadowTweaks, bAllowHighQuality);
        float flAttenuated = mix(flShadow, 1.0, vShadowTweaks.y);			// Blend between fully attenuated and not attenuated
        flShadow = clamp(mix(flAttenuated, flShadow, fAtten), 0.0, 1.0);	// Blend between shadow and above, according to light attenuation
        flashlightColor *= flShadow;										// Shadow term
    }

    vec3 diffuseLighting = vec3(fAtten);

    float flLDotWorldNormal;
    if (bHasNormal)
    {
        flLDotWorldNormal = dot(L.xyz, worldNormal.xyz);
    }
    else
    {
        flLDotWorldNormal = 1.0;
    }

    diffuseLighting *= clamp(flLDotWorldNormal + flFlashlightNoLambertValue, 0.0, 1.0); // Lambertian term

    diffuseLighting *= flashlightColor;
    diffuseLighting *= endFalloffFactor;

    return diffuseLighting;
}

#endif // COMMON_FLASHLIGHT_GL460_FS
