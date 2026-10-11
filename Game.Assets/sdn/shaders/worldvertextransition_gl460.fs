#version 460
in vec2 vs_TexCoord0;
centroid in vec2 vs_TexCoord1;
in vec4 vs_Color;

layout(std140, binding = 3) uniform source_pixel_sharedUBO {
    bool isAlphaTesting;
    int alphaTestFunc;
    float alphaTestRef;
};

const int VertexColor = 16;
const int VertexAlpha = 32;

uniform int flags;
uniform sampler2D basetexture;
uniform sampler2D basetexture2;
uniform sampler2D lightmaptexture;

out vec4 fragColor;

void main()
{
    vec4 tex1Color = texture(basetexture, vs_TexCoord0);
    vec4 tex2Color = texture(basetexture2, vs_TexCoord0);
    
    vec4 texelColor = mix(tex1Color, tex2Color, vs_Color.a);
    
    vec4 lightmapColor = texture(lightmaptexture, vs_TexCoord1);

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

    vec4 vertexColor = vec4(1.0, 1.0, 1.0, 1.0);

    if((flags & VertexColor) != 0){
        vertexColor.r = vs_Color.r;
        vertexColor.g = vs_Color.g;
        vertexColor.b = vs_Color.b;
    }

    if((flags & VertexAlpha) != 0){
        vertexColor.a = vs_Color.a;
    }

    fragColor = texelColor * vertexColor * lightmapColor * 2.2;
}