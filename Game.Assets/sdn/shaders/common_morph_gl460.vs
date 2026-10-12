#ifndef COMMON_MORPH_GL460_VS
#define COMMON_MORPH_GL460_VS

const int VERTEX_SHADER_FLEXSCALE = 3;
#define cFlexScale			vs_const[VERTEX_SHADER_FLEXSCALE]

bool ApplyMorph(vec3 vPosFlex, inout vec3 vPosition)
{
    vec3 vPosDelta = vPosFlex.xyz * cFlexScale.x;
    vPosition.xyz += vPosDelta;
    return true;
}

bool ApplyMorph(vec3 vPosFlex, vec3 vNormalFlex, inout vec3 vPosition, inout vec3 vNormal)
{
    vec3 vPosDelta = vPosFlex.xyz * cFlexScale.x;
    vec3 vNormalDelta = vNormalFlex.xyz * cFlexScale.x;
    vPosition.xyz += vPosDelta;
    vNormal       += vNormalDelta;
    return true;
}

bool ApplyMorph(vec3 vPosFlex, vec3 vNormalFlex, inout vec3 vPosition, inout vec3 vNormal, inout vec3 vTangent)
{
    vec3 vPosDelta = vPosFlex.xyz * cFlexScale.x;
    vec3 vNormalDelta = vNormalFlex.xyz * cFlexScale.x;
    vPosition.xyz += vPosDelta;
    vNormal       += vNormalDelta;
    vTangent.xyz  += vNormalDelta;
    return true;
}

bool ApplyMorph(vec4 vPosFlex, vec3 vNormalFlex, inout vec3 vPosition, inout vec3 vNormal, inout vec3 vTangent, out float flWrinkle)
{
    vec3 vPosDelta = vPosFlex.xyz * cFlexScale.x;
    vec3 vNormalDelta = vNormalFlex.xyz * cFlexScale.x;
    flWrinkle = vPosFlex.w * cFlexScale.y;
    vPosition.xyz += vPosDelta;
    vNormal       += vNormalDelta;
    vTangent.xyz  += vNormalDelta;
    return true;
}

#endif
