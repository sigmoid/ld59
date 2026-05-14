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

float minFuelThreshold;
float ignitionTemperature;
float2 texelSize;


float4 SpreadFirePS(VertexShaderOutput input) : COLOR0
{
    float4 centerTemp = tex2D(temperatureSampler, input.TexCoord);
    float4 leftTemp = tex2D(temperatureSampler, input.TexCoord - float2(texelSize.x, 0));
    float4 rightTemp = tex2D(temperatureSampler, input.TexCoord + float2(texelSize.x, 0));
    float4 topTemp = tex2D(temperatureSampler, input.TexCoord - float2(0, texelSize.y));
    float4 bottomTemp = tex2D(temperatureSampler, input.TexCoord + float2(0, texelSize.y));

    float4 centerFuel = tex2D(fuelSampler, input.TexCoord);
    float4 leftFuel = tex2D(fuelSampler, input.TexCoord - float2(texelSize.x, 0));
    float4 rightFuel = tex2D(fuelSampler, input.TexCoord + float2(texelSize.x, 0));
    float4 topFuel = tex2D(fuelSampler, input.TexCoord - float2(0, texelSize.y));
    float4 bottomFuel = tex2D(fuelSampler, input.TexCoord + float2(0, texelSize.y));

    if (leftFuel.r > minFuelThreshold && leftTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (rightFuel.r > minFuelThreshold && rightTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (topFuel.r > minFuelThreshold && topTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (bottomFuel.r > minFuelThreshold && bottomTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;

    return centerTemp;
}

technique SpreadFire
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL SpreadFirePS();
    }
}