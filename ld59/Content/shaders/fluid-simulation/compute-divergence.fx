#ifndef RENDERTARGETSIZE_DECLARED
float2 renderTargetSize;
#define RENDERTARGETSIZE_DECLARED
#endif

float2 uvOffset;
#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
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

texture velocityTexture;
sampler2D velocitySampler = sampler_state
{
    Texture = <velocityTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture obstacleTexture;
sampler2D obstacleSampler = sampler_state
{
    Texture = <obstacleTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

float2 texelSize;

float4 ComputeDivergencePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float2 scrollPos = frac(input.TexCoord + uvOffset);

    float obsC = tex2D(obstacleSampler, scrollPos).r;
    if (obsC > 0.1f) {
        return float4(0, 0, 0, 1);
    }

    float obsL = tex2D(obstacleSampler, frac(scrollPos - float2(texelSize.x, 0))).r;
    float obsR = tex2D(obstacleSampler, frac(scrollPos + float2(texelSize.x, 0))).r;
    float obsT = tex2D(obstacleSampler, frac(scrollPos - float2(0, texelSize.y))).r;
    float obsB = tex2D(obstacleSampler, frac(scrollPos + float2(0, texelSize.y))).r;

    float2 vL = tex2D(velocitySampler, frac(scrollPos - float2(texelSize.x, 0))).xy;
    float2 vR = tex2D(velocitySampler, frac(scrollPos + float2(texelSize.x, 0))).xy;
    float2 vT = tex2D(velocitySampler, frac(scrollPos - float2(0, texelSize.y))).xy;
    float2 vB = tex2D(velocitySampler, frac(scrollPos + float2(0, texelSize.y))).xy;

    // If neighbor is obstacle, use zero velocity (solid wall boundary condition)
    if (obsL > 0.1f) vL = float2(0, 0);
    if (obsR > 0.1f) vR = float2(0, 0);
    if (obsT > 0.1f) vT = float2(0, 0);
    if (obsB > 0.1f) vB = float2(0, 0);

    float halfRdx = 0.5f / texelSize.x;

    float divergence = halfRdx * ((vR.x - vL.x) + (vB.y - vT.y));

    return float4(divergence, 0, 0, 1);
}

technique ComputeDivergence
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ComputeDivergencePS();
    }
}