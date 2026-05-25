#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 World;
float4x4 LightViewProjection;
float3   LightPosition;
float    FarPlane;

struct VSInput  { float4 Position : POSITION; };
struct VSOutput { float4 Position : POSITION; float3 WorldPos : TEXCOORD0; };

VSOutput VS(VSInput input)
{
    VSOutput output;
    float4 worldPos  = mul(input.Position, World);
    output.WorldPos  = worldPos.xyz;
    output.Position  = mul(worldPos, LightViewProjection);
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    float depth = length(input.WorldPos - LightPosition) / FarPlane;
    return float4(depth, 0, 0, 1);
}

technique ShadowDepth
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
