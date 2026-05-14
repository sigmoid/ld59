#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler TextureSampler : register(s0);

float time = 0.0;
float intensity = 0.05;

struct VertexShaderOutput
{
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float rand(float2 co)
{
    return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D(TextureSampler, input.TextureCoordinates);

    float noise = rand(input.TextureCoordinates + frac(time));
    color.rgb *= noise * intensity + (1.0 - intensity);

    return color;
}

technique StaticNoise
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
