#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

sampler TextureSampler : register(s0);

float scanBarPosition = 0.0;

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
    float4 color = tex2D(TextureSampler, uv);

    float scanline = sin(uv.y * 260.0 * 3.14159);
    color.rgb *= scanline < 0.0 ? 0.98 : 1.03;

    float dist = abs(uv.y - scanBarPosition);
    float barFactor = 1.0 - smoothstep(0.0, 0.06, dist);
    color.rgb *= 1.0 + barFactor * 0.1;

    return color;
}

technique CRTScanlines
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}
