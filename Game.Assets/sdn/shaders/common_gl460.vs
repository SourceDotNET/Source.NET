#ifndef COMMON_GL460_VS
#define COMMON_GL460_VS

#include "common_gl460.glsl"

const int VERTEX_SHADER_CAMERA_POS = 2;

#define cEyePosWaterZ		vs_const[VERTEX_SHADER_CAMERA_POS]
#define cEyePos				cEyePosWaterZ.xyz

#endif // COMMON_GL460_VS
