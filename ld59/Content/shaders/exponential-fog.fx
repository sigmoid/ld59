#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

// Exponential distance fog as a full-screen post-process over a rendered 3D scene.
//
// Reads the linear radial depth written by shaders/scene-depth (distance from the camera over
// FarDistance) plus the inverse view-projection, so every pixel can be turned back into the world
// segment the camera looked along. Fog is then the analytic integral of an exponentially
// height-decaying density along that segment, optionally broken up by fbm noise that fades in with
// distance (so nearby geometry stays crisp and only the far haze churns).

sampler TextureSampler : register(s0);   // the rendered scene colour (SpriteBatch source)

// Explicit register, but note that this alone is NOT enough: under ps_4_0 the sampler register and
// the texture register are separate binding spaces, and a pass that references only this sampler
// still gets its TEXTURE placed in t0 -- the slot SpriteBatch overwrites with the drawn sprite
// after the effect binds its parameters. Pinning s1 only helps a pass that also samples s0, which
// here means MainPS. DepthPS instead takes the depth map as its SpriteBatch source and reads it
// through TextureSampler, so it has no secondary binding to lose.
// (Verify with: fxc /Gec /T ps_4_0 /E <entry>, and read the "Resource Bindings" table.)
texture DepthTexture;
sampler2D DepthSampler : register(s1) = sampler_state
{
    Texture   = <DepthTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

float4x4 InverseViewProjection;
float3   CameraPosition;
float    FarDistance = 1000.0;   // world distance the depth map's 1.0 encodes

float3 FogColor   = float3(0.55, 0.6, 0.7);
float  FogDensity = 0.02;        // fog per world unit at the base height
float  FogStart   = 0.0;         // world units of clear air in front of the camera

// Height fog: density falls off as exp(-(y - FogBaseHeight) * FogHeightFalloff).
// A falloff of 0 makes the fog uniform (no height dependence) at FogDensity everywhere.
float FogHeightFalloff = 0.0;
float FogBaseHeight    = 0.0;

// How much fog the background (pixels no geometry covered) gets. 1 = fully fogged horizon, which
// is what you want without a skybox; drop it toward 0 to leave a drawn sky visible.
float BackgroundFog = 1.0;

// Ceiling on the blend, so distance never resolves to the flat fog colour. Aiming fog at the 1-bit
// palette's bright colour is what keeps the dither clean, but it also means saturated fog is solid
// white and far geometry disappears into the page. Clipping the top of the curve (rather than
// scaling the whole thing) leaves the near-to-mid falloff exactly as tuned and only holds the far
// end back, so distant shapes keep a little of their own value.
float MaxFog = 1.0;

// Quantise the final fog amount into this many bands (< 1.5 = smooth). A continuous ramp is the
// wrong shape for a 1-bit output: the downstream error diffusion turns it into spatial noise, and
// the depth cue is lost. Discrete steps survive dithering as readable planes of haze instead, and
// with the fbm modulation on they become wavy contours through the fog volume.
float FogLevels = 0.0;

// Ordered (Bayer) dithering of the band edges. 0 rounds to the nearest band -- hard-edged steps;
// 1 thresholds against the dither matrix instead, so each edge dissolves into a stipple whose dot
// density tracks the fog. The pattern is locked to screen pixels, which is the point: unlike the
// error diffusion downstream, it doesn't reshuffle every frame, so the fog stops boiling when the
// camera moves.
float FogDither = 0.0;

// Pixels per dither cell. Above 1 the stipple gets chunkier, which survives both the 1-bit pass and
// the final upscale better than a single-pixel pattern.
float DitherScale = 1.0;

// Render target size, so texcoords can be turned back into the pixel grid the dither rides on.
float2 Resolution = float2(1280.0, 720.0);

// fbm density modulation. Strength 0 skips the noise entirely (the loops below are the expensive
// part of this shader). NoiseDistance is the distance over which the noise lerps in.
float  NoiseStrength = 0.0;
float  NoiseScale    = 0.02;
float  NoiseDistance = 60.0;
float3 NoiseWind     = float3(0.6, 0.05, 0.3);
float  Time          = 0.0;

struct VSInput
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput MainVS(VSInput input)
{
    VSOutput output;
    output.Position = float4(input.TexCoord.x * 2.0 - 1.0, -(input.TexCoord.y * 2.0 - 1.0), 0.0, 1.0);
    output.Color    = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

// ── simplex noise ────────────────────────────────────────────────────────────────
// Textureless GLSL 3D simplex noise (Ian McEwan / Ashima Arts, MIT), transcribed to HLSL.
// https://github.com/ashima/webgl-noise

float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 mod289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 permute(float4 x) { return mod289(((x * 34.0) + 1.0) * x); }
float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);

    // First corner
    float3 i  = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);

    // Other corners
    float3 g  = step(x0.yzx, x0.xyz);
    float3 l  = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;   // 2.0*C.x = 1/3 = C.y
    float3 x3 = x0 - D.yyy;        // -1.0+3.0*C.x = -0.5 = -D.y

    // Permutations
    i = mod289(i);
    float4 p = permute(permute(permute(
                 i.z + float4(0.0, i1.z, i2.z, 1.0))
               + i.y + float4(0.0, i1.y, i2.y, 1.0))
               + i.x + float4(0.0, i1.x, i2.x, 1.0));

    // Gradients: 7x7 points over a square, mapped onto an octahedron.
    float  n_ = 0.142857142857;   // 1/7
    float3 ns = n_ * D.wyz - D.xzx;

    float4 j  = p - 49.0 * floor(p * ns.z * ns.z);   // mod(p, 7*7)

    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);                 // mod(j, N)

    float4 x = x_ * ns.x + ns.yyyy;
    float4 y = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);

    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));

    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    float3 p0 = float3(a0.xy, h.x);
    float3 p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z);
    float3 p3 = float3(a1.zw, h.w);

    // Normalise gradients
    float4 norm = taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;

    // Mix final noise value
    float4 m = max(0.5 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 105.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// Octave counts are compile-time constants so the loops unroll. The warp pass runs at a coarser
// octave count -- it only needs the low-frequency drift that makes the detail pass look layered
// rather than uniformly cloudy.
#define FBM_OCTAVES  4
#define WARP_OCTAVES 2

float FBM(float3 p, int octaves)
{
    float value     = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < octaves; ++i)
    {
        value     += amplitude * snoise(p);
        p         *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

// Two layers: an fbm field used to warp the sample position of a second fbm. Sampling fbm with
// fbm is what gives the billowing, non-repeating structure a single fbm lacks.
float LayeredNoise(float3 p)
{
    float3 drift = NoiseWind * Time;
    float3 warp  = float3(
        FBM(p * 0.5 + drift, WARP_OCTAVES),
        FBM(p * 0.5 + drift + 31.7, WARP_OCTAVES),
        FBM(p * 0.5 + drift + 57.3, WARP_OCTAVES));
    return FBM(p + warp + drift, FBM_OCTAVES);
}

// ── ordered dithering ────────────────────────────────────────────────────────────
// 4x4 Bayer matrix. Deliberately coarse rather than 8x8: a single-pixel pattern gets chewed up by
// the 1-bit error diffusion and the final upscale, while 4x4 (scalable further with DitherScale)
// stays legible as a deliberate stipple.
static const float Bayer4x4[16] =
{
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
};

// Threshold for this pixel, in (0,1). The +0.5 centres each cell's threshold inside its slot, so a
// fog value of exactly 0 or 1 still lands entirely off or entirely on.
float BayerThreshold(float2 uv)
{
    float2 p = floor(uv * Resolution / max(DitherScale, 1.0));
    int x = (int)fmod(p.x, 4.0);
    int y = (int)fmod(p.y, 4.0);
    return (Bayer4x4[y * 4 + x] + 0.5) / 16.0;
}

// ── fog ──────────────────────────────────────────────────────────────────────────

// Analytic optical depth of an exponentially height-decaying medium along a segment:
//   density(y) = FogDensity * exp(-(y - FogBaseHeight) * FogHeightFalloff)
// integrated from `origin` along `dir` for `dist` world units. Degenerates to the plain
// distance * density product when either the falloff or the vertical component vanishes, which is
// exactly the uniform-fog case.
float OpticalDepth(float3 origin, float3 dir, float dist)
{
    float baseDensity = FogDensity * exp(-(origin.y - FogBaseHeight) * FogHeightFalloff);
    float k = FogHeightFalloff * dir.y;
    if (abs(k) < 1e-4)
        return baseDensity * dist;
    return baseDensity * (1.0 - exp(-k * dist)) / k;
}

// World-space ray through this pixel. Unprojects the near and far plane points rather than
// assuming a z convention, so it holds for both DX (z 0..1) and GL (z -1..1) clip space.
float3 RayDirection(float2 uv)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 np  = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
    float4 fp  = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    return normalize(fp.xyz / fp.w - np.xyz / np.w);
}

float4 MainPS(VSOutput input) : COLOR
{
    float4 scene = tex2D(TextureSampler, input.TexCoord);
    float2 depth = tex2D(DepthSampler, input.TexCoord).rg;

    float3 dir  = RayDirection(input.TexCoord);
    // Unclamped: geometry beyond FarDistance keeps its true distance and so keeps fogging (the
    // depth map's range only sets the encoding scale, not a cutoff).
    float  dist = depth.r * FarDistance;

    // Green is the depth pass' entity id, and ids start at 1 -- so 0 means nothing was drawn and
    // the "surface" here is sky. Fog that by BackgroundFog so a scene with a drawn skybox can keep
    // it, while a plain black background just becomes the fog colour. Distance alone can't stand in
    // for this test: real geometry can sit arbitrarily far away and must still fog fully.
    bool isBackground = depth.g < 0.5;

    // Clear air in front of the camera: shift the integration start down the ray.
    float lead   = min(dist, FogStart);
    float3 start = CameraPosition + dir * lead;
    float  span  = max(dist - lead, 0.0);

    float optical = OpticalDepth(start, dir, span);

    // fbm modulation of the density, faded in with distance so near geometry keeps a stable,
    // noise-free tint and only the distance haze churns. Sampled at the midpoint of the fogged
    // segment -- one sample standing in for the whole integral, which is the usual cheat.
    if (NoiseStrength > 0.001)
    {
        float3 mid   = start + dir * span * 0.5;
        float  n     = LayeredNoise(mid * NoiseScale);
        float  fade  = saturate(dist / max(NoiseDistance, 1e-3));
        optical *= lerp(1.0, max(0.0, 1.0 + n * NoiseStrength), fade);
    }

    float fog = 1.0 - exp(-optical);
    if (isBackground) fog *= BackgroundFog;
    fog = min(saturate(fog), MaxFog);

    // Band last, so what gets quantised is the amount actually blended (background scaling and all)
    // and the steps land on predictable fractions.
    //
    // Which band a pixel snaps to is decided by comparing its position WITHIN the band against a
    // threshold: a flat 0.5 is plain rounding (hard-edged steps), while the Bayer value spreads the
    // decision across the pixel grid so the edge between two bands becomes a stipple that thickens
    // across the transition. FogDither blends between the two, and this formulation keeps both ends
    // reachable -- flooring alone would never produce fully-fogged distance.
    if (FogLevels >= 1.5)
    {
        float scaled = fog * FogLevels;
        float lower  = floor(scaled);
        float t      = lerp(0.5, BayerThreshold(input.TexCoord), saturate(FogDither));
        fog = (lower + step(t, scaled - lower)) / FogLevels;
    }

    return float4(lerp(scene.rgb, FogColor, fog), scene.a);
}

// Debug view: the depth buffer as a grey ramp (black = at the camera, white = FarDistance away,
// and anything past that clips to white). Background pixels come out blue so it's obvious at a
// glance which parts of the frame the depth pass saw no geometry for.
//
// Reads the depth map through TextureSampler, because this pass is handed it as the SpriteBatch
// source rather than as DepthTexture -- see the register note at the top of the file. Only MainPS,
// which needs the scene colour as well, can safely bind a second texture.
float4 DepthPS(VSOutput input) : COLOR
{
    float2 depth = tex2D(TextureSampler, input.TexCoord).rg;
    if (depth.g < 0.5) return float4(0.1, 0.15, 0.5, 1.0);
    float d = saturate(depth.r);
    return float4(d, d, d, 1.0);
}

technique ExponentialFog
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}

technique DepthDebug
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DepthPS();
    }
}
