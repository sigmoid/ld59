// Adds a curl-noise wind force to the velocity field each simulation step.
// Curl noise = rotated gradient of an FBM scalar potential, which is
// naturally divergence-free so it doesn't fight the projection step.

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

float timeStep;
float time;
float windStrength;
float windScale;
float windEvolutionSpeed;

texture velocityTexture;
sampler2D velocitySampler = sampler_state
{
    Texture = <velocityTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

// ---- Gradient noise ----

float2 hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)),
               dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
}

float gnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f); // C1 smoothstep
    return lerp(
        lerp(dot(hash2(i),               f),
             dot(hash2(i + float2(1,0)), f - float2(1,0)), u.x),
        lerp(dot(hash2(i + float2(0,1)), f - float2(0,1)),
             dot(hash2(i + float2(1,1)), f - float2(1,1)), u.x),
        u.y);
}

// 4-octave FBM used as the scalar potential for curl noise.
// Rotation between octaves de-correlates them so the pattern isn't axis-aligned.
float potential(float2 p)
{
    float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8); // ~37 degree rotation
    float v  = 0.5000 * gnoise(p);
    p = mul(rot, p) * 2.1 + float2(1.7, 9.2);
    v += 0.2500 * gnoise(p);
    p = mul(rot, p) * 2.1 + float2(1.7, 9.2);
    v += 0.1250 * gnoise(p);
    p = mul(rot, p) * 2.1 + float2(1.7, 9.2);
    v += 0.0625 * gnoise(p);
    return v;
}

float4 WindFieldPS(VertexShaderOutput input) : COLOR0
{
    float2 uv = input.TexCoord;
    float2 velocity = tex2D(velocitySampler, uv).xy;

    // Map UV into noise space; time offset slowly evolves the pattern
    float2 p = uv * windScale + float2(0.0, time * windEvolutionSpeed);

    // Numerical gradient of the potential via central differences,
    // then rotated 90° to get curl (divergence-free vector field).
    float eps = 0.01;
    float dpdy = (potential(p + float2(0, eps)) - potential(p - float2(0, eps))) / (2.0 * eps);
    float dpdx = (potential(p + float2(eps, 0)) - potential(p - float2(eps, 0))) / (2.0 * eps);

    velocity += float2(dpdy, -dpdx) * windStrength * timeStep;
    return float4(velocity, 0, 1);
}

technique WindField
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL WindFieldPS();
    }
}

// ---- Wind visualization ----
// Direction → hue (full color wheel), magnitude → brightness.
// windStrength is excluded so the pattern shape is visible at any strength.

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float4 VisualizeWindPS(VertexShaderOutput input) : COLOR0
{
    float2 uv = input.TexCoord;
    float2 p  = uv * windScale + float2(0.0, time * windEvolutionSpeed);

    float eps  = 0.01;
    float dpdy = (potential(p + float2(0, eps)) - potential(p - float2(0, eps))) / (2.0 * eps);
    float dpdx = (potential(p + float2(eps, 0)) - potential(p - float2(eps, 0))) / (2.0 * eps);
    float2 curl = float2(dpdy, -dpdx); // unit-ish curl, independent of windStrength

    float hue  = atan2(curl.y, curl.x) / (2.0 * 3.14159265) + 0.5;
    float val  = saturate(length(curl)); // typically ~0.5-1 for FBM gradients

    return float4(hsv2rgb(float3(hue, 1.0, val)), 1.0);
}

technique VisualizeWind
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL VisualizeWindPS();
    }
}
