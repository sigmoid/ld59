#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler TextureSampler : register(s0);

float2 curvature = float2(6.0, 4.0);
float screenResolution = 1080.0;
float roundness = 8.0;
float vignetteOpacity = 0.55;

struct VertexShaderOutput
{
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float2 curveRemapUV(float2 uv)
{
    uv = uv * 2.0 - 1.0;
    float2 offset = abs(uv.yx) / curvature;
    uv = uv + uv * offset * offset;
    uv = uv * 0.5 + 0.5;
    return uv;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = curveRemapUV(input.TextureCoordinates);

    // Pixels outside the warped screen area are black
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    float4 color = tex2D(TextureSampler, uv);

    // Scanlines
    float scanline = sin(uv.y * 260.0 * 3.14159);
    if (scanline < 0)
    {
        color.rgb *= 0.95;
    }
    else
    {
        color.rgb *= 1.05;
    }

    // Vignette
    float2 vig2d = clamp((uv * (1.0 - uv)) * screenResolution / roundness, 0.0, 1.0);
    float vignette = pow(vig2d.x * vig2d.y, vignetteOpacity);
    color.rgb *= vignette;

    return color;
}

technique CRT
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
