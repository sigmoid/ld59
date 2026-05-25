// Renders one face of a shadow cube map as a grayscale depth image.
// Designed to be used with SpriteBatch (SpriteSortMode.Immediate).
//
// For face F, define FaceA/B/C from the OpenGL cube map spec:
//   dir = normalize(FaceA + FaceB*(u*2-1) + FaceC*(v*2-1))
// where (u,v) are the UV coordinates in [0,1] across the drawn quad.
//
// Face vectors (derived from OpenGL spec sc/tc/ma formulas):
//   +X: A=(1,0,0)  B=(0,0,-1) C=(0,-1,0)
//   -X: A=(-1,0,0) B=(0,0,1)  C=(0,-1,0)
//   +Y: A=(0,1,0)  B=(1,0,0)  C=(0,0,1)
//   -Y: A=(0,-1,0) B=(1,0,0)  C=(0,0,-1)
//   +Z: A=(0,0,1)  B=(1,0,0)  C=(0,-1,0)
//   -Z: A=(0,0,-1) B=(-1,0,0) C=(0,-1,0)

#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

// Set by SpriteBatch automatically
float4x4 MatrixTransform;

float3 FaceA;
float3 FaceB;
float3 FaceC;

textureCUBE CubeMap;
samplerCUBE CubeSampler = sampler_state
{
    Texture   = <CubeMap>;
    MinFilter = Linear;
    MagFilter = Linear;
};

struct VSInput
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput VS(VSInput input)
{
    VSOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color    = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    float2 uv  = input.TexCoord;
    float3 dir = normalize(FaceA + FaceB * (uv.x * 2 - 1) + FaceC * (uv.y * 2 - 1));
    float  d   = texCUBE(CubeSampler, dir).r;
    return float4(d, d, d, 1.0);
}

technique DebugCubeMap
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
