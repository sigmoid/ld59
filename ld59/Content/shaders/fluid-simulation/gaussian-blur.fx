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
float blurRadius;
int blurKernelSize;

float4 GaussianBlurHorizontalPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 result = float4(0, 0, 0, 0);
    
    int kernelSize = clamp(blurKernelSize, 1, 32);
    int halfKernel = kernelSize / 2;
    
    float sigma = kernelSize / 6.0f; 
    float twoSigmaSquared = 2.0f * sigma * sigma;
    float weightSum = 0.0f;
    
    for (int i = 0; i < kernelSize; i++)
    {
        int offset = i - halfKernel;
        float weight = exp(-(offset * offset) / twoSigmaSquared);
        weightSum += weight;
        
        float2 samplePos = pos + float2(offset * texelSize.x * blurRadius, 0);
        result += tex2D(sourceSampler, samplePos) * weight;
    }
    
    result /= weightSum;
    
    return result;
}

float4 GaussianBlurVerticalPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 result = float4(0, 0, 0, 0);
    
    int kernelSize = clamp(blurKernelSize, 1, 32);
    int halfKernel = kernelSize / 2;
    
    float sigma = kernelSize / 6.0f;
    float twoSigmaSquared = 2.0f * sigma * sigma;
    float weightSum = 0.0f;
    
    for (int i = 0; i < kernelSize; i++)
    {
        int offset = i - halfKernel;
        float weight = exp(-(offset * offset) / twoSigmaSquared);
        weightSum += weight;
        
        float2 samplePos = pos + float2(0, offset * texelSize.y * blurRadius);
        result += tex2D(sourceSampler, samplePos) * weight;
    }
    
    result /= weightSum;
    
    return result;
}


technique GaussianBlurHorizontal
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL GaussianBlurHorizontalPS();
    }
}

technique GaussianBlurVertical
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL GaussianBlurVerticalPS();
    }
}
