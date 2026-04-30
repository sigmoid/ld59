#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler TextureSampler : register(s0);

float strength = 0.003;

struct VertexShaderOutput
{
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 offset = (uv - 0.5) * strength;

    float r = tex2D(TextureSampler, uv - offset).r;
    float g = tex2D(TextureSampler, uv).g;
    float b = tex2D(TextureSampler, uv + offset).b;
    float a = tex2D(TextureSampler, uv).a;

    return float4(r, g, b, a);
}

technique ChromaticAberration
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
