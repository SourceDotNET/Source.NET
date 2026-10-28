#version 460
//	STATIC: "SKY_LAYERS"	"0..4"

layout(location = 0) in vec3 vs_WorldPos;

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D StarSampler;

#define g_EyePos ps_const[0].xyz
#define g_TopColor ps_const[1]
#define g_BottomColor ps_const[2]
#define g_FadeBias ps_const[3].x
#define g_HDRScale ps_const[4].x
#define g_SunNormal ps_const[5].xyz
#define g_DuskColor ps_const[6]
#define g_DuskSize ps_const[7].x
#define g_DuskPower ps_const[8].x
#define g_SunColor ps_const[9]
#define g_SunSize ps_const[10].x
#define g_StarFade ps_const[11].x
#define g_StarScale ps_const[12].x
#define g_StarPos ps_const[13].x
#define cLightScale ps_const[30]

float StarCoord(float d, float horizon)
{
    return asin(d) + sin(d) * horizon;
}

void main()
{
    vec3 dir = normalize(vs_WorldPos - g_EyePos);
    float horizon = 1.0 - abs(dir.z);

    float sunDot = length(g_SunNormal) - 0.00100000005 >= 0.0 ? dot(normalize(g_SunNormal), dir) : 0.0;
    float dusk = (horizon * 8.0 + sunDot) * 0.111111112;
    float duskAmount = pow(abs(dusk), g_DuskSize * -5.0 + 10.0) * g_DuskPower;

    vec3 sky = mix(g_BottomColor.xyz, g_TopColor.xyz, pow(abs(dir.z), g_FadeBias));

    if (dusk > 0.0)
        sky = sky - sky * duskAmount * 0.5 + g_DuskColor.xyz * duskAmount;

    if (sunDot > 0.0 && g_SunSize > 0.0)
        sky += g_SunColor.xyz * pow(abs(sunDot), 1.0 / (g_SunSize * 0.0500000007));

    sky *= g_HDRScale;

#if SKY_LAYERS > 0
    float starAmount = pow(abs(dir.z), 1.0 / g_StarFade);
    vec2 starCoord = vec2(StarCoord(dir.x, horizon) + g_StarPos, StarCoord(dir.y, horizon));

    vec3 stars = texture(StarSampler, starCoord * g_StarScale).xyz * starAmount;
#if SKY_LAYERS > 1
    starCoord.x -= g_StarPos * 0.00999999978 + 1.0;
    stars += texture(StarSampler, starCoord * g_StarScale).xyz * starAmount * 0.5;
#endif
#if SKY_LAYERS > 2
    starCoord.x -= g_StarPos * 0.0199999996 + 1.0;
    stars += texture(StarSampler, starCoord * g_StarScale).xyz * starAmount * 0.75;
#endif

    if (dir.z > 0.0)
        sky += stars;
#endif

    fragColor = vec4(sky * cLightScale.x, 1.0);
}
