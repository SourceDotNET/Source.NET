#version 460

in vec3 vs_T0;
in vec3 vs_T1;
in vec3 vs_T2;
in float vs_T3;
in vec4 vs_Color;

layout(binding = 0) uniform sampler2D basetexture;

out vec4 fragColor;

void main()
{
    if (vs_T1.x < 0.0 || vs_T1.y < 0.0 || vs_T1.z < 0.0)
        discard;
    if (vs_T2.x < 0.0 || vs_T2.y < 0.0 || vs_T2.z < 0.0)
        discard;
    if (vs_T3 < 0.0)
        discard;

    float shadowAlpha = texture(basetexture, vs_T0.xy).a;

    fragColor = vec4(mix(vec3(1.0, 1.0, 1.0), vs_Color.xyz, shadowAlpha), 1.0);
}
