#version 460

in vec2 vs_BaseTexCoord;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

out vec4 fragColor;

layout(binding = 0) uniform sampler2D TexSampler;

void main()
{
    vec4 result = texture(TexSampler, vs_BaseTexCoord);
    result.a = 1.0;

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
}
