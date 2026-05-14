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

float2 texelSize;
float diffusion;
float timeStep;

float4 DiffusePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 center = tex2D(sourceSampler, pos);
    
    if (diffusion <= 0.000001f)
    {
        return center;
    }

    float4 left = tex2D(sourceSampler, pos - float2(texelSize.x, 0));
    float4 right = tex2D(sourceSampler, pos + float2(texelSize.x, 0));
    float4 top = tex2D(sourceSampler, pos - float2(0, texelSize.y));
    float4 bottom = tex2D(sourceSampler, pos + float2(0, texelSize.y));

    float alpha = ((texelSize.x )*(texelSize.x )) / (diffusion * timeStep);
    float beta = 1.0f / (4.0f + alpha);
    
    float4 result = (left + right + top + bottom + alpha * center) * beta;
    
    return result;
}

technique Diffuse
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL DiffusePS();
    }
}