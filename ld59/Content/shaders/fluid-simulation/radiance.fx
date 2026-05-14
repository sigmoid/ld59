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

texture temperatureTexture;
sampler2D temperatureSampler = sampler_state
{
    Texture = <temperatureTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float ambientTemperature;
float coolingRate;
float timeStep;
float maxTemperature;

float4 RadiancePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float temperature = tex2D(temperatureSampler, pos).r;

    float newTemperature;

    float a = 1/ pow(temperature - ambientTemperature, 3);
    float b = (3 * coolingRate * timeStep) / pow (maxTemperature - ambientTemperature, 4);

    newTemperature = ambientTemperature + pow(a + b, -1.0/3.0);

    if(newTemperature < ambientTemperature)
    {
        newTemperature = ambientTemperature;
    }   

    return float4(newTemperature, 0, 0, 1);
}   

technique Radiance
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL RadiancePS();
    }
}
