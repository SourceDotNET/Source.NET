#version 460
// STATIC: "DUALSEQUENCE"			"0..1"
// STATIC: "SEQUENCE_BLEND_MODE"	"0..2"
// STATIC: "ADDBASETEXTURE2"		"0..1"
// STATIC: "MAXLUMFRAMEBLEND1"		"0..1"
// STATIC: "MAXLUMFRAMEBLEND2"		"0..1"
// STATIC: "EXTRACTGREENALPHA"		"0..1"
// STATIC: "COLORRAMP"				"0..1"
// STATIC: "ANIMBLEND"				"0..1"
// STATIC: "ADDSELF"				"0..1"

#define COMBINE_MODE_AVERAGE							0
#define COMBINE_MODE_USE_FIRST_AS_ALPHA_MASK_ON_SECOND	1
#define COMBINE_MODE_USE_FIRST_OVER_SECOND				2

layout(location = 0) in vec2 vs_TexCoord0;
layout(location = 1) in vec2 vs_TexCoord1;
layout(location = 2) in vec4 vs_Color;
layout(location = 3) in vec4 vs_BlendFactor0;
layout(location = 4) in vec2 vs_TexCoord2;
layout(location = 5) in vec4 vs_BlendFactor1;
layout(location = 6) in vec2 vs_Seq2TexCoord0;
layout(location = 7) in vec2 vs_Seq2TexCoord1;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

layout(binding = 0) uniform sampler2D BaseTextureSampler;
layout(binding = 1) uniform sampler2D ColorRampSampler;

#define g_Parameters				ps_const[0]
#define fAdditiveBlendWeight		g_Parameters.x
#define fOverbrightFactor			g_Parameters.y
#define fAdditiveSelfBlendWeight	g_Parameters.z

void main()
{
    bool bMaxLumFrameBlend1 = MAXLUMFRAMEBLEND1 != 0;
    bool bMaxLumFrameBlend2 = MAXLUMFRAMEBLEND2 != 0;
    bool bExtractGreenAlpha = EXTRACTGREENALPHA != 0;
    bool bAddBaseTexture2 = ADDBASETEXTURE2 != 0;
    bool bDualSequence = DUALSEQUENCE != 0;
    bool bColorRamp = COLORRAMP != 0;
    int nSequenceBlendMode = SEQUENCE_BLEND_MODE;

    vec4 argbcolor = vs_Color;

    vec4 baseTex0 = texture(BaseTextureSampler, vs_TexCoord0);
    vec4 baseTex1 = texture(BaseTextureSampler, vs_TexCoord1);

#if ANIMBLEND
    vec4 blended_rgb = mix(baseTex0, baseTex1, vs_BlendFactor0.x);
#else
    vec4 blended_rgb = baseTex0;
#endif

    if (bMaxLumFrameBlend1)
    {
        float lum0 = dot(vec3(0.3, 0.59, 0.11), baseTex0.rgb * (1.0 - vs_BlendFactor0.x));
        float lum1 = dot(vec3(0.3, 0.59, 0.11), baseTex1.rgb * vs_BlendFactor0.x);

        if (lum0 > lum1)
            blended_rgb = baseTex0;
        else
            blended_rgb = baseTex1;
    }
    else if (bExtractGreenAlpha)
    {
        blended_rgb = vec4(dot(baseTex0, vs_BlendFactor0) + dot(baseTex1, vs_BlendFactor1));
    }

#if DUALSEQUENCE
    if (bDualSequence)
    {
        baseTex0 = texture(BaseTextureSampler, vs_Seq2TexCoord0);
        baseTex1 = texture(BaseTextureSampler, vs_Seq2TexCoord1);

        vec4 rgb2 = mix(baseTex0, baseTex1, vs_BlendFactor0.z);

        if (bMaxLumFrameBlend2)
        {
            float tlum0 = dot(vec3(0.3, 0.59, 0.11), baseTex0.rgb * (1.0 - vs_BlendFactor0.x));
            float tlum1 = dot(vec3(0.3, 0.59, 0.11), baseTex1.rgb * vs_BlendFactor0.x);

            if (tlum0 > tlum1)
                rgb2 = baseTex0;
            else
                rgb2 = baseTex1;
        }

        if (nSequenceBlendMode == COMBINE_MODE_AVERAGE)
        {
            blended_rgb = 0.5 * (blended_rgb + rgb2);
        }
        else if (nSequenceBlendMode == COMBINE_MODE_USE_FIRST_AS_ALPHA_MASK_ON_SECOND)
        {
            blended_rgb.rgb = rgb2.rgb;
        }
        else if (nSequenceBlendMode == COMBINE_MODE_USE_FIRST_OVER_SECOND)
        {
            blended_rgb.rgb = mix(blended_rgb.rgb, rgb2.rgb, rgb2.a);
        }
    }
#endif

    if (bColorRamp)
    {
        blended_rgb.rgb = texture(ColorRampSampler, vec2(blended_rgb.r, blended_rgb.g)).rgb;
    }

    blended_rgb.rgb *= fOverbrightFactor;

#if ADDBASETEXTURE2
    if (bAddBaseTexture2)
    {
        blended_rgb.a *= argbcolor.a;

        if (!bColorRamp)
        {
            blended_rgb.rgb *= blended_rgb.a;
        }

        if (bExtractGreenAlpha)
        {
            blended_rgb.rgb += fAdditiveBlendWeight * argbcolor.a * blended_rgb.rgb;
        }
        else
        {
            blended_rgb.rgb += fOverbrightFactor * fAdditiveBlendWeight * argbcolor.a * texture(BaseTextureSampler, vs_TexCoord2).rgb;
        }

        blended_rgb.rgb *= argbcolor.rgb;
    }
    else
#endif
    {
#if ADDSELF
        blended_rgb.a *= argbcolor.a;
        blended_rgb.rgb *= blended_rgb.a;
        blended_rgb.rgb += fOverbrightFactor * fAdditiveSelfBlendWeight * argbcolor.a * blended_rgb.rgb;
        blended_rgb.rgb *= argbcolor.rgb;
#else
        blended_rgb *= argbcolor;
#endif
    }

    vec4 result = FinalOutput(blended_rgb, 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR);

    if (isAlphaTesting)
    {
        switch (alphaTestFunc)
        {
            case 0: discard; break;
            case 1: if (result.a >= alphaTestRef) { discard; } break;
            case 2: if (result.a != alphaTestRef) { discard; } break;
            case 3: if (result.a > alphaTestRef) { discard; } break;
            case 4: if (result.a <= alphaTestRef) { discard; } break;
            case 5: if (result.a == alphaTestRef) { discard; } break;
            case 6: if (result.a < alphaTestRef) { discard; } break;
        }
    }

    fragColor = result;
}
