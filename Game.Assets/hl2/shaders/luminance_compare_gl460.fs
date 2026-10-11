#version 460

in vec2 vs_BaseTexCoord;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D TexSampler;

#define g_vComparisonMinMaxScale ps_const[0]
#define g_flComparisonMin   g_vComparisonMinMaxScale.x
#define g_flComparisonMax   g_vComparisonMinMaxScale.y
#define g_flComparisonScale g_vComparisonMinMaxScale.z

void main()
{
    vec3 color = texture(TexSampler, vs_BaseTexCoord).rgb * g_flComparisonScale;

    vec3 tmpv = vec3(0.2125, 0.7154, 0.0721);
    float flLuminance = dot(color.rgb, tmpv.rgb);

    vec4 result = vec4(step(g_flComparisonMin, flLuminance) * step(flLuminance, g_flComparisonMax));

    if(isAlphaTesting){
        switch(alphaTestFunc){
            case 0: discard; break;
            case 1: if(result.a >= alphaTestRef){ discard; } break;
            case 2: if(result.a != alphaTestRef){ discard; } break;
            case 3: if(result.a > alphaTestRef){ discard; } break;
            case 4: if(result.a <= alphaTestRef){ discard; } break;
            case 5: if(result.a == alphaTestRef){ discard; } break;
            case 6: if(result.a < alphaTestRef){ discard; } break;
        }
    }

    fragColor = result;
    gl_FragDepth = 0.0;
}
