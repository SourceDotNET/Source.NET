#version 460
// STATIC: "BASETEXTURE"				"0..1"

in vec2 vs_TexCoord;
in vec4 vs_Color;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};


uniform sampler2D basetexture;

out vec4 fragColor;

void main()
{
#if BASETEXTURE
    vec4 texelColor = texture(basetexture, vs_TexCoord);
#else
    vec4 texelColor = vec4(1.0, 1.0, 1.0, 1.0);
#endif
    if(isAlphaTesting){
        switch(alphaTestFunc){
            case 0: discard; break;
            case 1: if(texelColor.a >=  alphaTestRef){ discard; } break;
            case 2: if(texelColor.a != alphaTestRef){ discard; } break;
            case 3: if(texelColor.a > alphaTestRef){ discard; } break;
            case 4: if(texelColor.a <=  alphaTestRef){ discard; } break;
            case 5: if(texelColor.a == alphaTestRef){ discard; } break;
            case 6: if(texelColor.a < alphaTestRef){ discard; } break;
        }
    }

    fragColor = texelColor * vs_Color;
}