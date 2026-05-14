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

float timeStep;
float2 texelSize;


texture velocityTexture;
sampler2D velocitySampler = sampler_state
{
    Texture = <velocityTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};


texture sourceTexture;
sampler2D sourceSampler = sampler_state
{
    Texture = <sourceTexture>;
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

float4 AdvectPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float2 scrollPos = frac(input.TexCoord + uvOffset);

    // If current cell is an obstacle, don't advect (obstacles don't scroll)
    float obsC = tex2D(obstacleSampler, pos).r;
    if (obsC > 0.1f) {
        return float4(0, 0, 0, 0);
    }

    float2 velocity = tex2D(velocitySampler, scrollPos).xy;

    float2 prevPos = frac(scrollPos - velocity * texelSize * timeStep);

    // If we're trying to advect from an obstacle, use current position instead (obstacles don't scroll)
    float obsPrev = tex2D(obstacleSampler, pos).r;
    if (obsPrev > 0.1f) {
        prevPos = scrollPos;
    }

    float4 result = tex2D(sourceSampler, prevPos);

    return result;
}

technique Advect
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL AdvectPS();
    }
}