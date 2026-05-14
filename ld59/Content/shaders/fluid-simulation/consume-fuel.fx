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

texture fuelTexture;
sampler2D fuelSampler = sampler_state
{
    Texture = <fuelTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

texture temperatureTexture;
sampler2D temperatureSampler = sampler_state
{
    Texture = <temperatureTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float ignitionTemperature;
float fuelConsumptionRate;
float timeStep;

float4 ConsumeFuelPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float fuel = tex2D(fuelSampler, pos).r;
    float temperature = tex2D(temperatureSampler, pos).r;

    if (temperature > ignitionTemperature && fuel > 0.0f)
    {
        fuel -= fuelConsumptionRate * temperature * timeStep;
        fuel = max(fuel, 0.0f);
    }

    return float4(fuel, 0, 0, 1);
}

technique ConsumeFuel
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ConsumeFuelPS();
    }
}
