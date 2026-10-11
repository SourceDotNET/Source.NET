#version 460

layout(location = 0) in vec3 v_Position;
layout(location = 2) in vec4 v_Color;
layout(location = 10) in vec2 v_TexCoord;

layout(std140, binding = 0) uniform source_matrices {
    mat4 viewMatrix;
    mat4 projectionMatrix;
    mat4 modelMatrix;
};

layout(std140, binding = 5) uniform source_vs_constants {
    vec4 vs_const[256];
};

#define cBaseTexCoordTransform0 vs_const[48]
#define cBaseTexCoordTransform1 vs_const[49]
#define cTextureJitter0 vs_const[50]
#define cTextureJitter1 vs_const[51]

centroid out vec2 vs_TexCoord0;
centroid out vec2 vs_TexCoord1;
centroid out vec2 vs_TexCoord2;
centroid out vec2 vs_TexCoord3;
centroid out vec2 vs_TexCoord4;
out vec4 vs_ShadowColor;

#include "common_clipplanes_gl460.vs"

void main()
{
	mat4 mvp = projectionMatrix * viewMatrix * modelMatrix;

    gl_Position = mvp * vec4(v_Position, 1.0);
    WriteUserClipDistances(gl_Position);

    vs_ShadowColor = v_Color;

    vec4 texCoordIn = vec4(v_TexCoord, 0.0, 1.0);
    vec2 texCoord;
    texCoord.x = dot(texCoordIn, cBaseTexCoordTransform0);
    texCoord.y = dot(texCoordIn, cBaseTexCoordTransform1);

    vs_TexCoord0 = texCoord;
    vs_TexCoord1 = texCoord + cTextureJitter0.xy;
    vs_TexCoord2 = texCoord - cTextureJitter0.xy;
    vs_TexCoord3 = texCoord + cTextureJitter1.xy;
    vs_TexCoord4 = texCoord - cTextureJitter1.xy;
}
