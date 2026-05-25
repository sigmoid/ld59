#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

#define MAX_LIGHTS 16

float4x4 World;
float4x4 View;
float4x4 Projection;

float3 DiffuseColor;
float3 AmbientColor;
bool   HasTexture;
bool   ShadowsEnabled;
float4 ShadowFarPlanes;   // x,y,z,w = far plane for shadow lights 0,1,2,3
float  NumShadowLights;   // how many lights have shadow maps (up to 4)

// xyz = world position, w = range
float4 LightPositions[MAX_LIGHTS];
// xyz = color * intensity
float4 LightColors[MAX_LIGHTS];
float  NumLights;

texture DiffuseTexture;
sampler2D DiffuseSampler = sampler_state
{
    Texture   = <DiffuseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Wrap;
    AddressV  = Wrap;
};

textureCUBE ShadowCubeMap0;
samplerCUBE ShadowCubeSampler0 = sampler_state
{
    Texture   = <ShadowCubeMap0>;
    MinFilter = Linear;
    MagFilter = Linear;
};

textureCUBE ShadowCubeMap1;
samplerCUBE ShadowCubeSampler1 = sampler_state
{
    Texture   = <ShadowCubeMap1>;
    MinFilter = Linear;
    MagFilter = Linear;
};

textureCUBE ShadowCubeMap2;
samplerCUBE ShadowCubeSampler2 = sampler_state
{
    Texture   = <ShadowCubeMap2>;
    MinFilter = Linear;
    MagFilter = Linear;
};

textureCUBE ShadowCubeMap3;
samplerCUBE ShadowCubeSampler3 = sampler_state
{
    Texture   = <ShadowCubeMap3>;
    MinFilter = Linear;
    MagFilter = Linear;
};

struct VertexInput
{
    float4 Position : POSITION;
    float3 Normal   : NORMAL;
    float2 TexCoord : TEXCOORD0;
};

struct VertexInputNoUV
{
    float4 Position : POSITION;
    float3 Normal   : NORMAL;
};

struct VertexOutput
{
    float4 Position : POSITION;
    float3 Normal   : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    float4 worldPos = mul(input.Position, World);
    output.WorldPos = worldPos.xyz;
    output.Position = mul(mul(worldPos, View), Projection);
    output.Normal   = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = input.TexCoord;
    return output;
}

VertexOutput VS_NoUV(VertexInputNoUV input)
{
    VertexOutput output;
    float4 worldPos = mul(input.Position, World);
    output.WorldPos = worldPos.xyz;
    output.Position = mul(mul(worldPos, View), Projection);
    output.Normal   = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = float2(0, 0);
    return output;
}

float3 CalcPointLight(float3 worldPos, float3 normal, float4 posRange, float4 colorIntensity)
{
    float3 toLight = posRange.xyz - worldPos;
    float  dist    = length(toLight);
    float3 dir     = toLight / (dist + 0.0001);
    float  atten   = 1.0 - saturate(dist / (posRange.w + 0.0001));
    atten          = atten * atten;
    float  diff    = saturate(dot(normal, dir));
    return colorIntensity.xyz * diff * atten;
}

// MonoGame's right-handed CreateLookAt produces a camera-right axis that is
// mirrored vs what DirectX texCUBE expects for X and Z faces.  Negate the
// relevant component so texCUBE lands on the texel that was actually rendered
// for that direction.
float3 FixShadowDir(float3 dir)
{
    float3 a = abs(dir);
    if (a.z > a.x && a.z > a.y)
        return float3(-dir.x, dir.y, dir.z);
    return float3(dir.x, dir.y, -dir.z);
}

// Use texCUBElod (explicit LOD=0) so sampling is safe inside any control flow.
float SampleShadow0(float3 worldPos)
{
    float3 dir         = worldPos - LightPositions[0].xyz;
    float  currentDist = length(dir) / ShadowFarPlanes.x;
    float  storedDist  = texCUBElod(ShadowCubeSampler0, float4(FixShadowDir(dir), 0)).r;
    return (currentDist - 0.005) < storedDist ? 1.0 : 0.0;
}

float SampleShadow1(float3 worldPos)
{
    float3 dir         = worldPos - LightPositions[1].xyz;
    float  currentDist = length(dir) / ShadowFarPlanes.y;
    float  storedDist  = texCUBElod(ShadowCubeSampler1, float4(FixShadowDir(dir), 0)).r;
    return (currentDist - 0.005) < storedDist ? 1.0 : 0.0;
}

float SampleShadow2(float3 worldPos)
{
    float3 dir         = worldPos - LightPositions[2].xyz;
    float  currentDist = length(dir) / ShadowFarPlanes.z;
    float  storedDist  = texCUBElod(ShadowCubeSampler2, float4(FixShadowDir(dir), 0)).r;
    return (currentDist - 0.005) < storedDist ? 1.0 : 0.0;
}

float SampleShadow3(float3 worldPos)
{
    float3 dir         = worldPos - LightPositions[3].xyz;
    float  currentDist = length(dir) / ShadowFarPlanes.w;
    float  storedDist  = texCUBElod(ShadowCubeSampler3, float4(FixShadowDir(dir), 0)).r;
    return (currentDist - 0.005) < storedDist ? 1.0 : 0.0;
}

float4 PS(VertexOutput input) : COLOR
{
    float3 normal = normalize(input.Normal);

    // Sample all shadow maps up front (dummy cubes return 1.0 for unused slots).
    // Sampling outside the loop avoids gradient/derivative issues in dynamic branches.
    float sh0 = ShadowsEnabled ? SampleShadow0(input.WorldPos) : 1.0;
    float sh1 = ShadowsEnabled ? SampleShadow1(input.WorldPos) : 1.0;
    float sh2 = ShadowsEnabled ? SampleShadow2(input.WorldPos) : 1.0;
    float sh3 = ShadowsEnabled ? SampleShadow3(input.WorldPos) : 1.0;

    float3 lighting = AmbientColor;
    int n  = int(NumLights);
    int ns = int(NumShadowLights);
    for (int i = 0; i < n; i++)
    {
        float shadow = 1.0;
        if      (i == 0 && ns > 0) shadow = sh0;
        else if (i == 1 && ns > 1) shadow = sh1;
        else if (i == 2 && ns > 2) shadow = sh2;
        else if (i == 3 && ns > 3) shadow = sh3;
        lighting += shadow * CalcPointLight(input.WorldPos, normal, LightPositions[i], LightColors[i]);
    }

    float3 diffuse = HasTexture ? tex2D(DiffuseSampler, input.TexCoord).rgb : DiffuseColor;
    return float4(diffuse * saturate(lighting), 1.0);
}

technique PointLightMesh
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}

technique PointLightMeshNoUV
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_NoUV();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
