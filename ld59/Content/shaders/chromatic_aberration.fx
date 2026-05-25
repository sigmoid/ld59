#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

sampler TextureSampler : register(s0);

float strength = 0.003;

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

float4 MainPS(VSOutput input) : COLOR
{
    float2 uv = input.TexCoord;
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
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}
