#ifndef RENDERTARGETSIZE_DECLARED
float2 renderTargetSize;
#define RENDERTARGETSIZE_DECLARED
#endif
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

texture sourceTexture;
sampler2D sourceSampler = sampler_state
{
    Texture = <sourceTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

texture spriteObstacleTexture;
sampler2D spriteObstacleSampler = sampler_state
{
    Texture = <spriteObstacleTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

float timeStep;
float sourceStrength;

float4 ObstacleToFuelPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    
    float currentFuel = tex2D(sourceSampler, pos).r;
    
    float obstacleValue = tex2D(spriteObstacleSampler, pos).r;
    
    float addedFuel = obstacleValue * sourceStrength * timeStep;
    
    return float4(currentFuel + addedFuel, 0, 0, 1);
}

technique ObstacleToFuel
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ObstacleToFuelPS();
    }
}