#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler TextureSampler : register(s0);

float2 resolution = float2(1920.0, 1080.0);
float3 darkColor  = float3(0.0, 0.0, 0.0);
float3 brightColor = float3(1.0, 1.0, 1.0);
float px = 0;
float py = 0;

struct VertexShaderOutput
{
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// State texture layout:
//   R = original luminance (preserved throughout)
//   G = quantization error magnitude
//   B = binary output: 0 = rendered dark, 1 = rendered bright

// Returns the signed error a neighbor contributes to the current pixel.
// Positive = neighbor rendered too dark (push us brighter).
// Negative = neighbor rendered too bright (push us darker).
float neighborErr(float2 uv, float dx, float dy)
{
    float4 s = tex2D(TextureSampler, uv + float2(dx / resolution.x, dy / resolution.y));
    return s.y * (1.0 - s.z) - s.y * s.z;
}

// --- Init: convert source colour to (lum, 0, 0, 1) ---

float4 InitPS(VertexShaderOutput input) : COLOR
{
    float4 c = tex2D(TextureSampler, input.TextureCoordinates);
    float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
    return float4(lum, 0.0, 0.0, 1.0);
}

// --- Diffuse: Atkinson error diffusion, one grid cell per pass ---
//
// Atkinson spreads 1/8 of the error to each of 6 neighbours:
//         [x+1,y]  [x+2,y]
// [x-1,y+1] [x,y+1] [x+1,y+1]
//         [x,y+2]
//
// Equivalently, pixel (x,y) receives 1/8 of the error from:
//   (x-1,y)  (x-2,y)  (x+1,y-1)  (x,y-1)  (x-1,y-1)  (x,y-2)

float4 DiffusePS(VertexShaderOutput input) : COLOR
{
    float2 uv  = input.TextureCoordinates;
    float4 cur = tex2D(TextureSampler, uv);

    int fx = int(uv.x * resolution.x);
    int fy = int(uv.y * resolution.y);

    if ((fx % 3 != int(px)) || (fy % 3 != int(py)))
        return cur;

    float sum = cur.x
        + neighborErr(uv, -1,  0) / 8.0    // left
        + neighborErr(uv, -2,  0) / 8.0    // 2 left
        + neighborErr(uv, +1, -1) / 8.0    // above-right
        + neighborErr(uv,  0, -1) / 8.0    // above
        + neighborErr(uv, -1, -1) / 8.0    // above-left
        + neighborErr(uv,  0, -2) / 8.0;   // 2 above

    if (sum > 0.5)
        return float4(cur.x, 1.0 - sum, 1.0, 1.0);   // rendered bright, store overshoot
    else
        return float4(cur.x, sum,       0.0, 1.0);    // rendered dark,   store undershoot
}

// --- Composite: map binary B channel to dark/bright colour ---

float4 CompositePS(VertexShaderOutput input) : COLOR
{
    float4 s = tex2D(TextureSampler, input.TextureCoordinates);
    float3 c = s.z > 0.5 ? brightColor : darkColor;
    return float4(c, 1.0);
}

technique InitPass
{
    pass Pass1 { PixelShader = compile PS_SHADERMODEL InitPS(); }
}

technique DiffusePass
{
    pass Pass1 { PixelShader = compile PS_SHADERMODEL DiffusePS(); }
}

technique CompositePass
{
    pass Pass1 { PixelShader = compile PS_SHADERMODEL CompositePS(); }
}
