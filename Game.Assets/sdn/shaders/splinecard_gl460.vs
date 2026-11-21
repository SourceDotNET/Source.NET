#version 460

layout(location = 0) in vec3 v_Parms;
layout(location = 2) in vec4 v_Tint;
layout(location = 10) in vec4 v_SplinePt0;
layout(location = 11) in vec4 v_SplinePt1;
layout(location = 12) in vec4 v_SplinePt2;
layout(location = 13) in vec4 v_SplinePt3;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#include "common_gl460.vs"

layout(location = 0) out vec2 vs_TexCoord0;
layout(location = 1) out vec2 vs_TexCoord1;
layout(location = 2) out vec4 vs_Color;
layout(location = 3) out vec4 vs_BlendFactor0;
layout(location = 4) out vec2 vs_TexCoord2;
layout(location = 5) out vec4 vs_BlendFactor1;
layout(location = 6) out vec2 vs_Seq2TexCoord0;
layout(location = 7) out vec2 vs_Seq2TexCoord1;

#include "common_clipplanes_gl460.vs"

vec3 DCatmullRomSpline3(vec3 a, vec3 b, vec3 c, vec3 d, float t)
{
    return 0.5 * (c - a + t * (2.0 * a - 5.0 * b + 4.0 * c - d + t * (3.0 * b - a - 3.0 * c + d))
                + t * (2.0 * a - 5.0 * b + 4.0 * c - d + 2.0 * (t * (3.0 * b - a - 3.0 * c + d))));
}

vec4 CatmullRomSpline(vec4 a, vec4 b, vec4 c, vec4 d, float t)
{
    return b + 0.5 * t * (c - a + t * (2.0 * a - 5.0 * b + 4.0 * c - d + t * (-a + 3.0 * b - 3.0 * c + d)));
}

void main()
{
    vec4 posrad = CatmullRomSpline(v_SplinePt0, v_SplinePt1, v_SplinePt2, v_SplinePt3, v_Parms.x);
    vec3 v2p = posrad.xyz - cEyePos;
    vec3 tangent = DCatmullRomSpline3(v_SplinePt0.xyz, v_SplinePt1.xyz, v_SplinePt2.xyz, v_SplinePt3.xyz, v_Parms.x);
    vec3 ofs = normalize(cross(v2p, normalize(tangent)));
    posrad.xyz += ofs * (posrad.w * (v_Parms.z - 0.5));

    gl_Position = projectionMatrix * viewMatrix * vec4(posrad.xyz, 1.0);
    WriteUserClipDistances(gl_Position);

    vs_TexCoord0 = vec2(1.0 - v_Parms.z, v_Parms.y);
    vs_TexCoord1 = vs_TexCoord0;
    vs_Color = v_Tint;
    vs_BlendFactor0 = vec4(0.0);
    vs_TexCoord2 = vec2(0.0);
    vs_BlendFactor1 = vec4(0.0);
    vs_Seq2TexCoord0 = vec2(0.0);
    vs_Seq2TexCoord1 = vec2(0.0);
}
