#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

sampler TextureSampler : register(s0);

float time = 0.0;
float intensity = 0.05;

struct VSInput
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput MainVS(VSInput input)
{
    VSOutput output;
    output.Position = float4(input.TexCoord.x * 2.0 - 1.0, -(input.TexCoord.y * 2.0 - 1.0), 0.0, 1.0);
    output.Color    = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float rand(float2 co)
{
    return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
}

float4 MainPS(VSOutput input) : COLOR
{
    float4 color = tex2D(TextureSampler, input.TexCoord);

    float noise = rand(input.TexCoord + frac(time));
    color.rgb *= noise * intensity + (1.0 - intensity);

    return color;
}

technique StaticNoise
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}
