#version 460

layout(location = 0) in vec2 vs_TexCoord;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D _Sampler;
layout(binding = 1) uniform sampler2D _Depth;

#define g_TexelSize ps_const[0].xy
#define g_Radius ps_const[1].x
#define g_FocusPoint ps_const[2].x
#define g_FocusRadius ps_const[3].x

const vec3 lumWeights = vec3(0.212599993, 0.715200007, 0.0722000003);

const vec3 kernel[60] = vec3[](
    vec3(0.216499999, 0.125, 1.0), vec3(0.0, 0.25, 1.0), vec3(-0.216499999, 0.125, 1.0),
    vec3(-0.216499999, -0.125, 1.0), vec3(0.0, -0.25, 1.0), vec3(0.216499999, -0.125, 1.0),
    vec3(0.432999998, 0.25, 1.0), vec3(0.0, 0.5, 1.0), vec3(-0.432999998, 0.25, 1.0),
    vec3(-0.432999998, -0.25, 1.0), vec3(0.0, -0.5, 1.0), vec3(0.432999998, -0.25, 1.0),
    vec3(0.649500012, 0.375, 1.0), vec3(0.0, 0.75, 1.0), vec3(-0.649500012, 0.375, 1.0),
    vec3(-0.649500012, -0.375, 1.0), vec3(0.0, -0.75, 1.0), vec3(0.649500012, -0.375, 1.0),
    vec3(0.865999997, 0.5, 1.0), vec3(0.0, 1.0, 1.0), vec3(-0.865999997, 0.5, 1.0),
    vec3(-0.865999997, -0.5, 1.0), vec3(0.0, -1.0, 1.0), vec3(0.865999997, -0.5, 1.0),
    vec3(0.216299996, 0.375400007, 0.866999984), vec3(-0.216999993, 0.375, 0.866999984), vec3(-0.433299989, -0.00039999999, 0.866999984),
    vec3(-0.216299996, -0.375400007, 0.866999984), vec3(0.216999993, -0.375, 0.866999984), vec3(0.433299989, 0.00039999999, 0.866999984),
    vec3(0.432799995, 0.500400007, 0.8847), vec3(-0.216999993, 0.625, 0.8847), vec3(-0.649800003, 0.124600001, 0.8847),
    vec3(-0.432799995, -0.500400007, 0.8847), vec3(0.216999993, -0.625, 0.8847), vec3(0.649800003, -0.124600001, 0.8847),
    vec3(0.649299979, 0.625400007, 0.906499982), vec3(-0.216999993, 0.875, 0.906499982), vec3(-0.866299987, 0.249599993, 0.906499982),
    vec3(-0.649299979, -0.625400007, 0.906499982), vec3(0.216999993, -0.875, 0.906499982), vec3(0.866299987, -0.249599993, 0.906499982),
    vec3(0.216000006, 0.625899971, 0.885100007), vec3(-0.433999985, 0.5, 0.885100007), vec3(-0.649999976, -0.1259, 0.885100007),
    vec3(-0.216000006, -0.625899971, 0.885100007), vec3(0.433999985, -0.5, 0.885100007), vec3(0.649999976, 0.1259, 0.885100007),
    vec3(0.432500005, 0.750899971, 0.866999984), vec3(-0.433999985, 0.75, 0.866999984), vec3(-0.86650002, -0.000899999985, 0.866999984),
    vec3(-0.432500005, -0.750899971, 0.866999984), vec3(0.433999985, -0.75, 0.866999984), vec3(0.86650002, 0.000899999985, 0.866999984),
    vec3(0.215800002, 0.876299977, 0.907000005), vec3(-0.651000023, 0.625, 0.907000005), vec3(-0.86680001, -0.251300007, 0.907000005),
    vec3(-0.215800002, -0.876299977, 0.907000005), vec3(0.651000023, -0.625, 0.907000005), vec3(0.86680001, 0.251300007, 0.907000005)
);

float CircleOfConfusion(vec2 uv)
{
    return pow(abs(texture(_Depth, uv).x - g_FocusPoint), g_FocusRadius) * g_Radius;
}

void main()
{
    vec4 center = texture(_Sampler, vs_TexCoord);
    float centerCoc = CircleOfConfusion(vs_TexCoord);
    float maxRadius = centerCoc * 2.0;
    vec2 scale = maxRadius * g_TexelSize;

    float centerWeight = max(dot(center.xyz + 0.00999999978, lumWeights) / (maxRadius * maxRadius + 1.0), 0.0);
    vec3 color = center.xyz * centerWeight;
    float totalWeight = centerWeight;

    for (int i = 0; i < 60; i++) {
        vec2 uv = vs_TexCoord + scale * kernel[i].xy;
        vec3 tap = texture(_Sampler, uv).xyz;

        float coc = CircleOfConfusion(uv);
        if (centerCoc < 0.0)
            coc = max(abs(maxRadius), coc);

        float edge = coc * kernel[i].z;
        float t = clamp((abs(maxRadius) * length(kernel[i].xy) - edge) / (edge * 0.100000001), 0.0, 1.0);
        float falloff = clamp(1.0 - t * t * (3.0 - 2.0 * t), 0.0, 1.0);

        float weight = max(dot(tap + 0.00999999978, lumWeights) / (coc * coc + 1.0) * falloff, 0.0);
        color += tap * weight;
        totalWeight += weight;
    }

    fragColor = vec4(color / totalWeight, 1.0);
}
