#ifndef COMMON_SKINNING_GL460_VS
#define COMMON_SKINNING_GL460_VS

#include "common_vertex_shared_gl460.vs"

layout(std140, binding = 4) uniform source_bone_matrices {
    mat4 bones[256];
};

void SkinPositionAndNormal(bool bSkinning, vec4 modelPos, vec3 modelNormal,
                           ivec4 boneIndices, vec2 boneWeights,
                           out vec3 worldPos, out vec3 worldNormal)
{
    if (!bSkinning || numBones == 0)
    {
        worldPos = (modelMatrix * modelPos).xyz;
        worldNormal = mat3(modelMatrix) * modelNormal;
    }
    else // skinning - always three bones
    {
        vec3 weights;
        weights[0] = boneWeights.x;
        weights[1] = boneWeights.y;
        weights[2] = 1.0 - (boneWeights.x + boneWeights.y);

        mat4 blendMatrix = bones[boneIndices[0]] * weights[0] +
                           bones[boneIndices[1]] * weights[1] +
                           bones[boneIndices[2]] * weights[2];

        worldPos = (blendMatrix * modelPos).xyz;
        worldNormal = mat3(blendMatrix) * modelNormal;
    }
}

void SkinPositionNormalAndTangentSpace(bool bSkinning, vec4 modelPos, vec3 modelNormal, vec4 modelTangentS,
                                       ivec4 boneIndices, vec2 boneWeights,
                                       out vec3 worldPos, out vec3 worldNormal,
                                       out vec3 worldTangentS, out vec3 worldTangentT)
{
    if (!bSkinning || numBones == 0)
    {
        worldPos = (modelMatrix * modelPos).xyz;
        worldNormal = mat3(modelMatrix) * modelNormal;
        worldTangentS = mat3(modelMatrix) * modelTangentS.xyz;
    }
    else // skinning - always three bones
    {
        vec3 weights;
        weights[0] = boneWeights.x;
        weights[1] = boneWeights.y;
        weights[2] = 1.0 - (boneWeights.x + boneWeights.y);

        mat4 blendMatrix = bones[boneIndices[0]] * weights[0] +
                           bones[boneIndices[1]] * weights[1] +
                           bones[boneIndices[2]] * weights[2];

        worldPos = (blendMatrix * modelPos).xyz;
        worldNormal = mat3(blendMatrix) * modelNormal;
        worldTangentS = mat3(blendMatrix) * modelTangentS.xyz;
    }

    worldTangentT = cross(worldNormal, worldTangentS) * modelTangentS.w;
}

#endif
