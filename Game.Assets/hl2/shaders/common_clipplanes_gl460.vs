#ifndef COMMON_CLIPPLANES_GL460_VS
#define COMMON_CLIPPLANES_GL460_VS

layout(std140, binding = 7) uniform source_clip_planes {
    vec4 clipPlanes[2];
};

void WriteUserClipDistances(vec4 projPos)
{
    gl_ClipDistance[0] = dot(projPos, clipPlanes[0]);
    gl_ClipDistance[1] = dot(projPos, clipPlanes[1]);
}

#endif
