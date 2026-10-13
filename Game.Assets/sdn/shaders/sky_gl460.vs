#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 10) in vec2 v_TexCoord0;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#define g_vTextureSizeInfo				vs_const[48]
#define g_mBaseTexCoordTransform0		vs_const[49]
#define g_mBaseTexCoordTransform1		vs_const[50]

#define TEXEL_XINCR (g_vTextureSizeInfo.x)
#define TEXEL_YINCR (g_vTextureSizeInfo.y)
#define U_TO_PIXEL_COORD_SCALE (g_vTextureSizeInfo.z)
#define V_TO_PIXEL_COORD_SCALE (g_vTextureSizeInfo.w)

out vec2 vs_BaseTexCoord00;
out vec2 vs_BaseTexCoord01;
out vec2 vs_BaseTexCoord10;
out vec2 vs_BaseTexCoord11;
out vec2 vs_BaseTexCoord_In_Pixels;

#include "common_clipplanes_gl460.vs"

void main()
{
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);

    vec4 vTexCoordInput = vec4(v_TexCoord0.x, v_TexCoord0.y, 0.0, 1.0);
    vec2 vTexCoord;
    vTexCoord.x = dot(vTexCoordInput, g_mBaseTexCoordTransform0);
    vTexCoord.y = dot(vTexCoordInput, g_mBaseTexCoordTransform1);

    vs_BaseTexCoord00.x = vTexCoord.x - TEXEL_XINCR;
    vs_BaseTexCoord00.y = vTexCoord.y - TEXEL_YINCR;
    vs_BaseTexCoord10.x = vTexCoord.x + TEXEL_XINCR;
    vs_BaseTexCoord10.y = vTexCoord.y - TEXEL_YINCR;

    vs_BaseTexCoord01.x = vTexCoord.x - TEXEL_XINCR;
    vs_BaseTexCoord01.y = vTexCoord.y + TEXEL_YINCR;
    vs_BaseTexCoord11.x = vTexCoord.x + TEXEL_XINCR;
    vs_BaseTexCoord11.y = vTexCoord.y + TEXEL_YINCR;

    vs_BaseTexCoord_In_Pixels = vs_BaseTexCoord00;
    vs_BaseTexCoord_In_Pixels.x *= U_TO_PIXEL_COORD_SCALE;
    vs_BaseTexCoord_In_Pixels.y *= V_TO_PIXEL_COORD_SCALE;
}
