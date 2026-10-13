#ifndef COMMON_LIGHTMAPPEDGENERIC_GL460_FS
#define COMMON_LIGHTMAPPEDGENERIC_GL460_FS

void GetBaseTextureAndNormal(sampler2D base, sampler2D base2, sampler2D bump, bool bBase2, bool bBump, vec3 coords, vec3 vWeights,
                             out vec4 vResultBase, out vec4 vResultBase2, out vec4 vResultBump)
{
    vResultBase = vec4(0.0);
    vResultBase2 = vec4(0.0);
    vResultBump = vec4(0.0);

    if (!bBump)
    {
        vResultBump = vec4(0, 0, 1, 1);
    }

#if SEAMLESS

    vResultBase  += vWeights.x * texture(base, coords.zy);
    if (bBase2)
    {
        vResultBase2 += vWeights.x * texture(base2, coords.zy);
    }
    if (bBump)
    {
        vResultBump  += vWeights.x * texture(bump, coords.zy);
    }

    vResultBase  += vWeights.y * texture(base, coords.xz);
    if (bBase2)
    {
        vResultBase2 += vWeights.y * texture(base2, coords.xz);
    }
    if (bBump)
    {
        vResultBump  += vWeights.y * texture(bump, coords.xz);
    }

    vResultBase  += vWeights.z * texture(base, coords.xy);
    if (bBase2)
    {
        vResultBase2 += vWeights.z * texture(base2, coords.xy);
    }
    if (bBump)
    {
        vResultBump  += vWeights.z * texture(bump, coords.xy);
    }

#else  // not seamless

    vResultBase  = texture(base, coords.xy);
    if (bBase2)
    {
        vResultBase2 = texture(base2, coords.xy);
    }
    if (bBump)
    {
        vResultBump  = texture(bump, coords.xy);
    }
#endif

}

// misyl:
// Bicubic lightmap code lovingly taken and adapted from Godot
// ( https://github.com/godotengine/godot/pull/89919 )
// Licensed under MIT.

float w0(float a) {
    return (1.0 / 6.0) * (a * (a * (-a + 3.0) - 3.0) + 1.0);
}

float w1(float a) {
    return (1.0 / 6.0) * (a * a * (3.0 * a - 6.0) + 4.0);
}

float w2(float a) {
    return (1.0 / 6.0) * (a * (a * (-3.0 * a + 3.0) + 3.0) + 1.0);
}

float w3(float a) {
    return (1.0 / 6.0) * (a * a * a);
}

// g0 and g1 are the two amplitude functions
float g0(float a) {
    return w0(a) + w1(a);
}

float g1(float a) {
    return w2(a) + w3(a);
}

// h0 and h1 are the two offset functions
float h0(float a) {
    return -1.0 + w1(a) / (w0(a) + w1(a));
}

float h1(float a) {
    return 1.0 + w3(a) / (w2(a) + w3(a));
}

#ifndef BICUBIC_LIGHTMAP
#define BICUBIC_LIGHTMAP 0
#endif

vec3 LightMapSample(sampler2D LightmapSampler, vec2 vTexCoord)
{
#if BICUBIC_LIGHTMAP
    float flLightmapPageWidth = 1024;
    float flLightmapPageHeight = 512;

    const vec2 vTextureSize = vec2(flLightmapPageWidth, flLightmapPageHeight);
    const vec2 vTexelSize = vec2(1.0, 1.0) / vTextureSize;

    vTexCoord.xy = vTexCoord.xy * vTextureSize + vec2(0.5, 0.5);

    vec2 iuv = floor(vTexCoord.xy);
    vec2 fuv = fract(vTexCoord.xy);

    float g0x = g0(fuv.x);
    float g1x = g1(fuv.x);
    float h0x = h0(fuv.x);
    float h1x = h1(fuv.x);
    float h0y = h0(fuv.y);
    float h1y = h1(fuv.y);

    vec2 p0 = (vec2(iuv.x + h0x, iuv.y + h0y) - vec2(0.5, 0.5)) * vTexelSize;
    vec2 p1 = (vec2(iuv.x + h1x, iuv.y + h0y) - vec2(0.5, 0.5)) * vTexelSize;
    vec2 p2 = (vec2(iuv.x + h0x, iuv.y + h1y) - vec2(0.5, 0.5)) * vTexelSize;
    vec2 p3 = (vec2(iuv.x + h1x, iuv.y + h1y) - vec2(0.5, 0.5)) * vTexelSize;

    vec3 samp =
        (g0(fuv.y) * (g0x * texture(LightmapSampler, p0).rgb + g1x * texture(LightmapSampler, p1).rgb)) +
        (g1(fuv.y) * (g0x * texture(LightmapSampler, p2).rgb + g1x * texture(LightmapSampler, p3).rgb));
#else
    vec3 samp = texture(LightmapSampler, vTexCoord).rgb;
#endif

    return samp;
}

#endif // COMMON_LIGHTMAPPEDGENERIC_GL460_FS
