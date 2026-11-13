#version 460
// STATIC: "ZOOM_ANIMATE_SEQ2"		"0..1"
// STATIC: "DUALSEQUENCE"			"0..1"
// STATIC: "EXTRACTGREENALPHA"		"0..1"
// DYNAMIC: "ORIENTATION"			"0..2"

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Tint;
layout(location = 10) in vec4 v_TexCoord0;
layout(location = 11) in vec4 v_TexCoord1;
layout(location = 12) in vec4 v_Parms;
layout(location = 13) in vec2 v_CornerID;
layout(location = 14) in vec4 v_TexCoord2;
#if DUALSEQUENCE
layout(location = 15) in vec4 v_Seq2TexCoord0;
layout(location = 6) in vec4 v_Seq2TexCoord1;
layout(location = 3) in vec4 v_Parms1;
#endif

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#include "common_gl460.vs"

const int SHADER_SPECIFIC_CONST_7 = 55;
const int SHADER_SPECIFIC_CONST_8 = 56;
const int SHADER_SPECIFIC_CONST_9 = 57;

#define ScaleParms vs_const[SHADER_SPECIFIC_CONST_7]
#define OLDFRM_SCALE_START (ScaleParms.x)
#define OLDFRM_SCALE_END (ScaleParms.y)

#define SizeParms vs_const[SHADER_SPECIFIC_CONST_8]
#define SizeParms2 vs_const[SHADER_SPECIFIC_CONST_9]

#define MINIMUM_SIZE_FACTOR (SizeParms.x)
#define MAXIMUM_SIZE_FACTOR (SizeParms.y)
#define START_FADE_SIZE_FACTOR (SizeParms.z)
#define END_FADE_SIZE_FACTOR (SizeParms.w)
#define START_FAR_FADE (SizeParms2.x)
#define FAR_FADE_FACTOR (SizeParms2.y)

#define BLENDFACTOR v_Parms.x
#define ROTATION v_Parms.y
#define RADIUS v_Parms.z
#define YAW v_Parms.w

layout(location = 0) out vec2 vs_TexCoord0;
layout(location = 1) out vec2 vs_TexCoord1;
layout(location = 2) out vec4 vs_Color;
layout(location = 3) out vec4 vs_BlendFactor0;
layout(location = 4) out vec2 vs_TexCoord2;
layout(location = 5) out vec4 vs_BlendFactor1;
layout(location = 6) out vec2 vs_Seq2TexCoord0;
layout(location = 7) out vec2 vs_Seq2TexCoord1;

#include "common_clipplanes_gl460.vs"

#if ZOOM_ANIMATE_SEQ2
float getlerpscaled(float l_in, float s0, float s1, float ts)
{
    l_in = 2.0 * (l_in - 0.5);
    l_in *= mix(s0, s1, ts);
    return 0.5 + 0.5 * l_in;
}

float getlerpscale_for_old_frame(float l_in, float ts)
{
    return getlerpscaled(l_in, OLDFRM_SCALE_START, OLDFRM_SCALE_END, ts);
}

float getlerpscale_for_new_frame(float l_in, float ts)
{
    return getlerpscaled(l_in, 1.0, OLDFRM_SCALE_START, ts);
}
#endif

void main()
{
    vec4 tint = vec4(GammaToLinear(v_Tint.rgb), v_Tint.a);

    vec2 sc_yaw = vec2(cos(YAW), sin(YAW));
    vec2 sc = vec2(cos(ROTATION), sin(ROTATION));

    vec2 ix = 2.0 * v_CornerID.xy - 1.0;
    float x1 = dot(ix, sc);
    float y1 = sc.x * ix.y - sc.y * ix.x;

    vec4 projPos;
    vec3 worldPos = (modelMatrix * vec4(v_Position, 1.0)).xyz;

    float rad = RADIUS;
    vec3 v2p = worldPos - cEyePos;
    float l = length(v2p);
    rad = max(rad, MINIMUM_SIZE_FACTOR * l);

    if (rad > START_FADE_SIZE_FACTOR * l)
    {
        if (rad > END_FADE_SIZE_FACTOR * l)
        {
            tint = vec4(0.0);
            rad = 0.0;
        }
        else
        {
            tint *= 1.0 - (rad - START_FADE_SIZE_FACTOR * l) / (END_FADE_SIZE_FACTOR * l - START_FADE_SIZE_FACTOR * l);
        }
    }

    float tscale = 1.0 - min(1.0, max(0.0, (l - START_FAR_FADE) * FAR_FADE_FACTOR));
    tint *= tscale;

    if (tscale <= 0.0)
        rad = 0.0;

    rad = min(rad, MAXIMUM_SIZE_FACTOR * l);

#if ORIENTATION == 0
    vec3 viewPos = (viewMatrix * vec4(worldPos, 1.0)).xyz;

    vec3 disp = vec3(-x1, y1, 0.0);
    float tmpx = disp.x * sc_yaw.x + disp.z * sc_yaw.y;
    disp.z = disp.z * sc_yaw.x - disp.x * sc_yaw.y;
    disp.x = tmpx;

    viewPos.xyz += disp * rad;

    projPos = projectionMatrix * vec4(viewPos, 1.0);
#endif

#if ORIENTATION == 1
    if (l > rad / 2.0)
    {
        vec3 up = vec3(0.0, 0.0, 1.0);
        vec3 right = normalize(cross(up, v2p));
        float tmpx = right.x * sc_yaw.x + right.y * sc_yaw.y;
        right.y = right.y * sc_yaw.x - right.x * sc_yaw.y;
        right.x = tmpx;

        worldPos += (x1 * rad) * right;
        worldPos.z += (y1 * rad) * up.z;

        if (l < rad * 2.0)
        {
            tint *= smoothstep(rad / 2.0, rad, l);
        }
    }
    projPos = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
#endif

#if ORIENTATION == 2
    vec3 wpos = v_Position + RADIUS * vec3(y1, x1, 0.0);
    projPos = projectionMatrix * viewMatrix * modelMatrix * vec4(wpos, 1.0);
#endif

    vs_BlendFactor0 = vec4(BLENDFACTOR, 0.0, 0.0, 0.0);
    gl_Position = projPos;
    WriteUserClipDistances(gl_Position);

    vs_TexCoord0 = mix(v_TexCoord0.zw, v_TexCoord0.xy, v_CornerID.xy);
    vs_TexCoord1 = mix(v_TexCoord1.zw, v_TexCoord1.xy, v_CornerID.xy);
    vs_TexCoord2 = mix(v_TexCoord2.zw, v_TexCoord2.xy, v_CornerID.xy);

    vs_Seq2TexCoord0 = vec2(0.0);
    vs_Seq2TexCoord1 = vec2(0.0);

#if DUALSEQUENCE
    vec2 lerpold = v_CornerID.xy;
    vec2 lerpnew = v_CornerID.xy;

#if ZOOM_ANIMATE_SEQ2
    lerpold.x = getlerpscale_for_old_frame(v_CornerID.x, v_Parms1.x);
    lerpold.y = getlerpscale_for_old_frame(v_CornerID.y, v_Parms1.x);
    lerpnew.x = getlerpscale_for_new_frame(v_CornerID.x, v_Parms1.x);
    lerpnew.y = getlerpscale_for_new_frame(v_CornerID.y, v_Parms1.x);
#endif

    vs_Seq2TexCoord0 = mix(v_Seq2TexCoord0.zw, v_Seq2TexCoord0.xy, lerpold);
    vs_Seq2TexCoord1 = mix(v_Seq2TexCoord1.zw, v_Seq2TexCoord1.xy, lerpnew);

    vs_BlendFactor0.z = v_Parms1.x;
#endif

    vs_BlendFactor1 = vec4(0.0);

#if EXTRACTGREENALPHA
    if (BLENDFACTOR < 0.25)
    {
        vs_BlendFactor0.a = BLENDFACTOR * 2.0 + 0.5;
        vs_BlendFactor0.g = 1.0 - vs_BlendFactor0.a;
    }
    else if (BLENDFACTOR < 0.75)
    {
        vs_BlendFactor1.g = BLENDFACTOR * 2.0 - 0.5;
        vs_BlendFactor0.a = 1.0 - vs_BlendFactor1.g;
    }
    else
    {
        vs_BlendFactor1.a = BLENDFACTOR * 2.0 - 1.5;
        vs_BlendFactor1.g = 1.0 - vs_BlendFactor1.a;
    }
#endif

    vs_Color = tint;
}
