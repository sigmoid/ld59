#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

// Unlit textured quad — used to display a render-target (e.g. a puzzle) on a 3D surface so the
// "screen" reads at full brightness regardless of scene lighting.

float4x4 World;
float4x4 View;
float4x4 Projection;

texture ScreenTexture;
sampler2D ScreenSampler = sampler_state
{
    Texture   = <ScreenTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

struct VSInput  { float4 Position : POSITION; float2 TexCoord : TEXCOORD0; };
struct VSOutput { float4 Position : POSITION; float2 TexCoord : TEXCOORD0; };

VSOutput VS(VSInput input)
{
    VSOutput output;
    float4 worldPos = mul(input.Position, World);
    output.Position = mul(mul(worldPos, View), Projection);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    return tex2D(ScreenSampler, input.TexCoord);
}

technique TexturedQuad
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
