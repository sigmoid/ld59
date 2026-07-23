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

float3 AmbientColor;

// xyz = world position, w = range
float4 LightPositions[MAX_LIGHTS];
// xyz = color * intensity
float4 LightColors[MAX_LIGHTS];
float  NumLights;

// Optional directional light (sun). HasDirLight defaults to 0, so callers that never set it
// (e.g. BoxPrimitive3D) are unaffected.
bool   HasDirLight;
float3 DirLightDirection;   // normalized, toward the light
float3 DirLightColor;
float  DirLightIntensity;

struct VertexInput
{
    float4 Position : POSITION;
    float4 Color    : COLOR0;
    float3 Normal   : NORMAL;
};

struct VertexOutput
{
    float4 Position : POSITION;
    float4 Color    : COLOR0;
    float3 Normal   : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    float4 worldPos  = mul(input.Position, World);
    output.WorldPos  = worldPos.xyz;
    output.Position  = mul(mul(worldPos, View), Projection);
    output.Color     = input.Color;
    output.Normal    = normalize(mul(input.Normal, (float3x3)World));
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

float4 PS(VertexOutput input) : COLOR
{
    float3 normal   = normalize(input.Normal);
    float3 lighting = AmbientColor;
    int n = int(NumLights);
    for (int i = 0; i < n; i++)
        lighting += CalcPointLight(input.WorldPos, normal, LightPositions[i], LightColors[i]);

    if (HasDirLight)
        lighting += DirLightColor * (DirLightIntensity * saturate(dot(normal, DirLightDirection)));

    return float4(input.Color.rgb * saturate(lighting), input.Color.a);
}

technique PointLight
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
