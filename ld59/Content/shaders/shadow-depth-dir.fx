#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 World;
float4x4 LightViewProjection;

struct VSInput  { float4 Position : POSITION; };
struct VSOutput
{
    float4 Position : POSITION;
    float  Depth    : TEXCOORD0;
};

VSOutput VS(VSInput input)
{
    VSOutput output;
    float4 worldPos  = mul(input.Position, World);
    float4 clipPos   = mul(worldPos, LightViewProjection);
    output.Position  = clipPos;
    // Store normalised depth in [0,1] regardless of whether NDC z is 0..1 (DX) or -1..1 (GL).
    output.Depth     = clipPos.z / clipPos.w;
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    return float4(input.Depth * 0.5 + 0.5, 0, 0, 1);
}

technique ShadowDepthDir
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
