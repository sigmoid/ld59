// Adds noise-driven divergence pockets to the divergence field.
// Positive noise → source (air expanding outward), negative → sink (air converging).
// The pressure projection step converts this into actual velocities, so wind
// emerges organically from the solve rather than being injected as velocity directly.

#ifndef RENDERTARGETSIZE_DECLARED
float2 renderTargetSize;
#define RENDERTARGETSIZE_DECLARED
#endif

#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput MainVS(VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    float2 ndc = (input.Position.xy / renderTargetSize) * 2.0 - 1.0;
    output.Position = float4(ndc, 0, 1);
    output.TexCoord = float2(input.TexCoord.x, 1.0 - input.TexCoord.y);
    return output;
}

float time;
float divergenceIntensity;
float noiseScale;
float evolutionSpeed;

texture divergenceTexture;
sampler2D divergenceSampler = sampler_state
{
    Texture = <divergenceTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float2 hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)),
               dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
}

float gnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(dot(hash2(i),               f),
             dot(hash2(i + float2(1,0)), f - float2(1,0)), u.x),
        lerp(dot(hash2(i + float2(0,1)), f - float2(0,1)),
             dot(hash2(i + float2(1,1)), f - float2(1,1)), u.x),
        u.y);
}

float fbm(float2 p)
{
    float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
    float v  = 0.5000 * gnoise(p);
    p = mul(rot, p) * 2.1 + float2(1.7, 9.2);
    v += 0.2500 * gnoise(p);
    p = mul(rot, p) * 2.1 + float2(1.7, 9.2);
    v += 0.1250 * gnoise(p);
    return v; // roughly in [-0.875, 0.875]
}

float4 WindDivergencePS(VertexShaderOutput input) : COLOR0
{
    float existingDiv = tex2D(divergenceSampler, input.TexCoord).r;

    float2 p = input.TexCoord * noiseScale + float2(0.0, time * evolutionSpeed);
    float noiseDivergence = fbm(p) * divergenceIntensity;

    return float4(existingDiv + noiseDivergence, 0, 0, 1);
}

technique WindDivergence
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL WindDivergencePS();
    }
}
