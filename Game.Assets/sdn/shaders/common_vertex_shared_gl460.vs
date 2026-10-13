#ifndef COMMON_VERTEX_SHARED_GL460_VS
#define COMMON_VERTEX_SHARED_GL460_VS

layout(std140, binding = 2) uniform source_vertex_sharedUBO {
    int numBones;
    int lightCount;
    int vertexSharedPad0;
    int vertexSharedPad1;
    vec4 lightEnabled;
};

#endif
