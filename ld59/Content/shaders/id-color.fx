#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

// Flat unlit ID pass. Renders each entity in a solid colour encoding its interactable id, so
// the crosshair pixel can be read back on the CPU for object picking. Driven through the same
// Entity.DrawDepth path as the shadow shaders (which sets only "World"); the host sets
// LightViewProjection = view * projection and IdColor per entity.

float4x4 World;
float4x4 LightViewProjection;
float4   IdColor;

struct VSInput  { float4 Position : POSITION; };
struct VSOutput { float4 Position : POSITION; };

VSOutput VS(VSInput input)
{
    VSOutput output;
    float4 worldPos = mul(input.Position, World);
    output.Position = mul(worldPos, LightViewProjection);
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    return IdColor;
}

technique IdColor
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
