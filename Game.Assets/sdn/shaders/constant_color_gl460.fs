#version 460


layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

layout(std140, binding = 6) uniform source_ps_constants {
    vec4 ps_const[256];
};

out vec4 fragColor;

#include "common_gl460.fs"

void main()
{
    vec4 result = FinalOutput(vec4(0.0, 1.0, 0.0, 1.0), 0.0, PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_NONE);

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
