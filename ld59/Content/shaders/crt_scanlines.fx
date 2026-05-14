#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler TextureSampler : register(s0);

float scanBarPosition = 0.0;

struct VertexShaderOutput
{
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 color = tex2D(TextureSampler, uv);

    // Scanlines
    float scanline = sin(uv.y * 260.0 * 3.14159);
    color.rgb *= scanline < 0.0 ? 0.98 : 1.03;

    // Scrolling bright bar
    float dist = abs(uv.y - scanBarPosition);
    float barFactor = 1.0 - smoothstep(0.0, 0.06, dist);
    color.rgb *= 1.0 + barFactor * 0.1;

    return color;
}

technique CRTScanlines
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
