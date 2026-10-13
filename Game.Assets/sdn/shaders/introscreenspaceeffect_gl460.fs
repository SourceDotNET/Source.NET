#version 460
// DYNAMIC: "MODE"				"0..9"

in vec2 vs_BaseTexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D BaseTextureSampler2;

#define g_Alpha ps_const[0].x

vec3 RGBtoHSV(vec3 rgb)
{
    vec3 hsv;
    float fmin, fmax, delta;
    fmin = min(min(rgb.r, rgb.g), rgb.b);
    fmax = max(max(rgb.r, rgb.g), rgb.b);
    hsv.b = fmax;
    delta = fmax - fmin;
    if (delta != 0.0)
    {
        hsv.g = delta / fmax;
        if (rgb.r == fmax)
            hsv.r = (rgb.g - rgb.b) / delta;
        else if (rgb.g == fmax)
            hsv.r = 2.0 + (rgb.b - rgb.r) / delta;
        else
            hsv.r = 4.0 + (rgb.r - rgb.g) / delta;
        hsv.r *= 60.0;
        if (hsv.r < 0.0)
            hsv.r += 360.0;
    }
    else
    {
        hsv.g = 0.0;
        hsv.r = -1.0;
    }
    return hsv;
}

vec3 HSVtoRGB(vec3 hsv)
{
    int i;
    vec3 rgb;
    float h = hsv.r;
    float s = hsv.g;
    float v = hsv.b;
    float f, p, q, t;
    if (s == 0.0)
    {
        rgb.rgb = vec3(v);
    }
    else
    {
        h /= 60.0;
        i = int(floor(h));
        f = h - float(i);
        p = v * (1.0 - s);
        q = v * (1.0 - s * f);
        t = v * (1.0 - s * (1.0 - f));
        if (i == 0)
        {
            rgb.r = v;
            rgb.g = t;
            rgb.b = p;
        }
        else if (i == 1)
        {
            rgb.r = q;
            rgb.g = v;
            rgb.b = p;
        }
        else if (i == 2)
        {
            rgb.r = p;
            rgb.g = v;
            rgb.b = t;
        }
        else if (i == 3)
        {
            rgb.r = p;
            rgb.g = q;
            rgb.b = v;
        }
        else if (i == 4)
        {
            rgb.r = t;
            rgb.g = p;
            rgb.b = v;
        }
        else
        {
            rgb.r = v;
            rgb.g = p;
            rgb.b = q;
        }
    }
    return rgb;
}

vec3 SampleTexture(sampler2D texSampler, vec2 tc)
{
    return texture(texSampler, tc).xyz;
}

vec3 OutputColor(vec3 result)
{
    return result;
}

void main()
{
    vec3 result;
#if MODE == 0
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);

    float scale = 1.0 / 3.0;
    scene.xyz = vec3(dot(vec3(scale, scale, scale), scene.xyz));
    scene = vec3(1.0, 1.0, 1.0) - scene;

    fragColor = FinalOutput(vec4(OutputColor(scene * gman), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 1
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    float scale = 1.0 / 3.0;
    scene.xyz = vec3(dot(vec3(scale, scale, scale), scene.xyz));

    float gmanLum = dot(vec3(scale, scale, scale), gman);
    if (gmanLum < 0.3)
    {
        result = OutputColor(vec3(1.0, 1.0, 1.0) - gman);
        fragColor = FinalOutput(vec4(result, g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
    }
    else
    {
        result = OutputColor((vec3(1.0, 1.0, 1.0) - gman) * scene);
        fragColor = FinalOutput(vec4(result, g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
    }
#endif

#if MODE == 2
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);

    float scale = 1.0 / 3.0;
    float gmanLum = dot(vec3(scale, scale, scale), gman);

    result = OutputColor(min(vec3(gmanLum), scene));
    fragColor = FinalOutput(vec4(result, g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 3
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    float scale = 1.0 / 3.0;
    float gmanLum = dot(vec3(scale, scale, scale), gman);

    float a = 0.0;
    float b = 0.4;
    float c = 0.7;
    float d = 1.0;

    float blend;
    if (gmanLum < b)
        blend = (gmanLum - a) / (b - a);
    else if (gmanLum > c)
        blend = 1.0 - ((gmanLum - c) / (d - c));
    else
        blend = 1.0;

    blend = clamp(blend, 0.0, 1.0);

    result = OutputColor(vec3(gmanLum) * (vec3(1.0, 1.0, 1.0) - vec3(blend)) + scene * vec3(blend));
    fragColor = FinalOutput(vec4(result, g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 4
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    float scale = 1.0 / 3.0;
    float gmanLum = dot(vec3(scale, scale, scale), gman);

    float a = 0.0;
    float b = 0.4;
    float c = 0.7;
    float d = 1.0;

    float blend;
    if (gmanLum < b)
        blend = (gmanLum - a) / (b - a);
    else if (gmanLum > c)
        blend = 1.0 - ((gmanLum - c) / (d - c));
    else
        blend = 1.0;

    blend = clamp(blend, 0.0, 1.0);

    result = OutputColor(gman * (vec3(1.0, 1.0, 1.0) - vec3(blend)) + scene * vec3(blend));
    fragColor = FinalOutput(vec4(result, g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 5
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    float sceneLum = scene.r;

    if (sceneLum > 0.0)
    {
        fragColor = FinalOutput(vec4(OutputColor(scene), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
    }
    else
    {
        vec3 hsv = RGBtoHSV(gman);

        float blend = hsv.b - 0.5;

        hsv.b *= 1.0 + blend;
        hsv.g *= 1.0 - blend;
        fragColor = FinalOutput(vec4(OutputColor(HSVtoRGB(hsv)), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
    }
#endif

#if MODE == 6
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    fragColor = FinalOutput(vec4(OutputColor(scene + gman), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 7
    vec3 scene = SampleTexture(BaseTextureSampler, vs_BaseTexCoord);
    fragColor = FinalOutput(vec4(OutputColor(scene), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 8
    vec3 gman = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord);
    fragColor = FinalOutput(vec4(OutputColor(gman), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif

#if MODE == 9
    vec3 cLayer1 = SampleTexture(BaseTextureSampler, vs_BaseTexCoord.xy);
    vec3 cLayer2 = SampleTexture(BaseTextureSampler2, vs_BaseTexCoord.xy);

    float flLayer1Brightness = clamp(dot(cLayer1.rgb, vec3(0.333, 0.334, 0.333)), 0.0, 1.0);

    cLayer1.rgb = clamp(cLayer1.rgb * cLayer1.rgb * 2.0, 0.0, 1.0);
    vec3 cLinearOverlayResult = cLayer1.rgb + cLayer2.rgb * clamp(1.0 - flLayer1Brightness * 2.0, 0.0, 1.0);

    fragColor = FinalOutput(vec4(OutputColor(cLinearOverlayResult.rgb), g_Alpha), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);
#endif
}
