#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

// Moon skybox. Drawn as a full-screen triangle before the scene: each pixel's world view ray is
// reconstructed from the inverse view-projection, then coloured by sampling a cubemap (the baked
// starfield / nebula background) with an Earth disk composited on top.

float4x4 InvViewProj;
float3   CameraPos;

float3 SunDir;      // direction from camera toward the sun (only used to light the Earth fallback disk)

float3 EarthDir;
float3 EarthColor;
float  EarthCos;    // cos(angular radius) of the earth disk
bool   EarthTextured;

texture EarthTex;
sampler2D EarthSampler = sampler_state
{
    Texture   = <EarthTex>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

texture SkyCube;
samplerCUBE SkyCubeSampler = sampler_state
{
    Texture   = <SkyCube>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
    AddressW  = Clamp;
};

struct VSIn  { float3 Position : POSITION; };
struct VSOut { float4 Position : POSITION; float2 Clip : TEXCOORD0; };

VSOut VS(VSIn input)
{
    VSOut o;
    o.Position = float4(input.Position.xy, 0, 1);
    o.Clip     = input.Position.xy;
    return o;
}

float3 SkyColor(float3 dir)
{
    // Baked starfield / nebula background from the cubemap. Rotate 90 deg about X so the exported
    // "Front" face (with the sun centred) sits directly overhead: looking up (+Y) samples +Z.
    float3 col = texCUBE(SkyCubeSampler, float3(dir.x, -dir.z, dir.y)).rgb;

    // Earth disk.
    float ed = dot(dir, EarthDir);
    if (ed > EarthCos)
    {
        float3 perp   = dir - EarthDir * ed;          // offset within the disk plane
        float  radius = sqrt(saturate(1.0 - EarthCos * EarthCos));  // sin(angular radius) = disk edge
        float  edge   = smoothstep(EarthCos, EarthCos + 0.0004, ed);

        if (EarthTextured)
        {
            // Build a basis in the disk plane and map the offset to [0,1] UVs, so the disk's
            // inscribed circle maps to the earth image (its transparent corners are never sampled).
            float3 up0   = abs(EarthDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 right = normalize(cross(up0, EarthDir));
            float3 upv   = cross(EarthDir, right);
            float2 uv    = float2(dot(perp, right), dot(perp, upv)) / radius; // -1..1 across the disk
            uv = float2(uv.x * 0.5 + 0.5, -uv.y * 0.5 + 0.5);
            float4 tex = tex2D(EarthSampler, uv);
            col = lerp(col, tex.rgb, tex.a * edge);
        }
        else
        {
            // Fallback: flat disk with a sun-lit terminator.
            float3 perpN   = normalize(perp + 1e-5);
            float3 sunPerp = normalize(SunDir - EarthDir * dot(SunDir, EarthDir) + 1e-5);
            float  lit     = saturate(dot(perpN, sunPerp) * 0.5 + 0.5);
            col = lerp(col, EarthColor * lerp(0.04, 1.0, lit), edge);
        }
    }

    // The sun is baked into the cubemap background, so nothing is drawn procedurally here.
    return col;
}

float4 PS(VSOut input) : COLOR
{
    float4 h = mul(float4(input.Clip, 1.0, 1.0), InvViewProj);
    float3 world = h.xyz / h.w;
    float3 dir   = normalize(world - CameraPos);
    return float4(SkyColor(dir), 1.0);
}

technique Skybox
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
