#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

// Screen-space ambient occlusion over a rendered 3D scene, from the same depth/id buffer the fog
// and outlines already read (shaders/scene-depth: red = linear radial distance / FarDistance,
// green = entity id, 0 = background).
//
// There is no G-buffer here, so surface normals are RECONSTRUCTED from depth rather than stored.
// That is the whole reason this effect costs one extra pass and not a second scene draw: the depth
// pass is already paid for the moment fog or outlines are on.
//
// Four passes:
//   1. Occlusion : hemisphere sampling around each pixel's reconstructed world position -> AO map
//   2. BlurH     : depth-aware (bilateral) blur across x, to clean up the per-pixel sample noise
//   3. BlurV     : the same across y
//   4. Composite : blend the occlusion over the scene, optionally quantised into bands
//
// IMPORTANT (the trap outline.fx and exponential-fog.fx both document, stated more precisely here
// because the loose version of the rule is wrong and cost a bug during this file's bring-up):
//
// Under ps_4_0 the sampler register and the texture register are SEPARATE binding spaces.
// `register(s1)` pins ONLY the sampler. The texture registers are handed out by fxc in order of
// FIRST USE in the code -- not declaration order, and not the pinned sampler slot. Whichever
// sampler a pass touches first gets t0, which is the slot SpriteBatch overwrites with the drawn
// sprite after the effect has bound its parameters.
//
// So the rule every pass here obeys is: the SpriteBatch source is sampled through TextureSampler,
// and TextureSampler is sampled BEFORE any other sampler. Single-texture passes (Occlusion,
// DebugNormals, DebugAO) are simply handed the buffer they want as that source. Multi-texture
// passes (BlurH/BlurV, Composite) touch TextureSampler first so t0 stays theirs and s1/s2 land
// where they were pinned -- BilateralBlur in particular reads the AO centre tap before the depth
// centre tap purely for this reason.
//
// Only t0 actually matters. The other textures land wherever fxc puts them -- Composite currently
// gets AOSampler in t1 and DepthSampler in t2, NOT matching their pinned s1/s2 -- and that is fine,
// because MonoGame binds each texture parameter to the slot it reflected out of the bytecode. t0 is
// the sole exception, being the one slot something else writes after that binding happens.
//
// Verify with: fxc /Gec /T ps_4_0 /E <entry> ssao.fx /Fc out.asm, then read the "Resource
// Bindings" table and check the `texture` rows, not the `sampler` rows.
sampler TextureSampler : register(s0);   // the SpriteBatch source: scene colour, or the pass input

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

// Point, even though the AO map may be smaller than the scene (see Downscale) and therefore needs
// upsampling: hardware bilinear is the WRONG filter across a depth discontinuity. Background is
// stored as fully lit, so a blind bilinear tap at a silhouette mixes that white in and fringes the
// object with a bright line exactly where its contact shadow should be darkest. UpsampleAO below
// does the interpolation by hand with a depth test on each tap instead.
texture AOTexture;
sampler2D AOSampler : register(s2) = sampler_state
{
    Texture   = <AOTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

float4x4 InverseViewProjection;   // pixel -> world ray
float4x4 ViewProjection;          // world sample point -> screen, to look its occluder up
float3   CameraPosition;
float    FarDistance = 1000.0;    // world distance the depth map's 1.0 encodes

// Resolution of the target THIS pass is writing, not necessarily the screen: the AO and blur passes
// run at the AO map's size, the composite at the scene's.
float2 Resolution = float2(1280.0, 720.0);

// Size of the AO map itself, which the composite needs separately from its own output size in order
// to reconstruct where the AO texels sit. Equal to Resolution for every pass except the composite.
float2 AOResolution = float2(1280.0, 720.0);

// Hemisphere radius in world units -- the distance out to which geometry can shade a pixel. This is
// the parameter to reach for first: too small and the effect vanishes into the surface's own
// texel-scale noise, too large and it stops reading as contact shading and becomes a grimy vignette
// around everything.
float Radius = 0.75;

// World-unit slack on the occlusion test. Reconstructed normals are only as accurate as the depth
// derivative allows, so a flat surface samples slightly into itself; without a bias that shows up as
// uniform grey haze over every polygon (the classic "self-occlusion" acne).
float Bias = 0.03;

float SampleCount = 16.0;   // hemisphere taps per pixel
float Intensity   = 1.0;    // scales the darkening; 0 = no AO
float Power       = 1.6;    // contrast exponent on the AO term; > 1 keeps mid-tones open

// World distance at which AO has faded out entirely (0 = never fade). Fades in over the last 40%.
// Far geometry occupies few pixels, so its hemisphere collapses to sub-pixel size and the sampling
// turns to noise -- fading it out is cheaper and steadier than sampling it harder.
float FadeDistance = 0.0;

float BlurRadius = 2.0;     // bilateral blur taps each way, per axis

float3 OcclusionColor = float3(0.13, 0.12, 0.20);

// Quantise the occlusion into this many bands (< 1.5 = smooth), same reasoning as the fog's
// FogLevels: a smooth ramp is exactly what the downstream 1-bit error diffusion destroys, turning
// the shading cue into spatial noise, whereas discrete steps survive as readable shadow shapes.
float Levels = 0.0;

// Ordered (Bayer) dithering of the band edges, 0 (hard steps) to 1 (full stipple). Anchored to the
// pixel grid, so unlike the error diffusion downstream it holds still as the camera moves.
float Dither     = 1.0;
float DitherScale = 1.0;    // pixels per dither cell

// Bounded loops, so the compiled shader has a fixed instruction count whatever the runtime values.
#define MAX_SAMPLES 32
#define MAX_BLUR    8

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

// ── depth -> world ───────────────────────────────────────────────────────────────

// World-space ray through this pixel. Unprojects the near and far plane points rather than assuming
// a z convention, so it holds for both DX (z 0..1) and GL (z -1..1) clip space.
float3 RayDirection(float2 uv)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 np  = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
    float4 fp  = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    return normalize(fp.xyz / fp.w - np.xyz / np.w);
}

// The world point that shaded this pixel. Trivial precisely because the depth pass stores LINEAR
// radial distance: position is camera + ray * distance, with no projection curve to undo.
// Reads the depth map through TextureSampler -- the passes calling this are handed it as their
// SpriteBatch source (see the register note at the top).
float3 WorldPosAt(float2 uv, out float2 raw)
{
    raw = tex2D(TextureSampler, uv).rg;
    return CameraPosition + RayDirection(uv) * (raw.r * FarDistance);
}

// Surface normal from the depth buffer's slope. Naive central differences smear across silhouettes
// (one of the two taps lands on a completely different surface, tilting the normal wildly and
// ringing the object in false occlusion), so each axis takes whichever neighbour is CLOSER in depth
// to this pixel -- i.e. the one more likely to be on the same surface -- and uses a one-sided
// difference toward it.
float3 ReconstructNormal(float2 uv, float3 P, float centerDepth)
{
    float2 texel = 1.0 / Resolution;
    float2 raw;

    float3 pl = WorldPosAt(uv - float2(texel.x, 0), raw); float dl = raw.r;
    float3 pr = WorldPosAt(uv + float2(texel.x, 0), raw); float dr = raw.r;
    float3 pd = WorldPosAt(uv - float2(0, texel.y), raw); float dd = raw.r;
    float3 pu = WorldPosAt(uv + float2(0, texel.y), raw); float du = raw.r;

    float3 dx = (abs(dl - centerDepth) < abs(dr - centerDepth)) ? (P - pl) : (pr - P);
    float3 dy = (abs(dd - centerDepth) < abs(du - centerDepth)) ? (P - pd) : (pu - P);

    float3 n   = cross(dx, dy);
    float  len = length(n);

    // Degenerate slope (a perfectly flat depth neighbourhood, or a 1px sliver): fall back to facing
    // the camera, which contributes no spurious occlusion.
    float3 toCamera = normalize(CameraPosition - P);
    if (len < 1e-9) return toCamera;

    n /= len;
    // The cross product's sign depends on the texture-space handedness; rather than reason about it,
    // force the normal to face the camera, which is the only orientation a visible surface can have.
    return dot(n, toCamera) < 0.0 ? -n : n;
}

// ── sampling pattern ─────────────────────────────────────────────────────────────

// Interleaved gradient noise (Jorge Jimenez): a cheap per-pixel hash that, unlike a random one,
// decorrelates neighbouring pixels in a pattern the bilateral blur below removes cleanly.
float PixelNoise(float2 uv)
{
    float2 p = uv * Resolution;
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

// A second, decorrelated hash. The azimuth and the sample RADIUS both need per-pixel offsetting, and
// driving them from the same value ties them together into a visible spiral.
float PixelNoise2(float2 uv)
{
    float2 p = uv * Resolution;
    return frac(43758.5453 * frac(dot(p, float2(0.1031, 0.0973)) + 0.37));
}

// Cosine-weighted hemisphere direction from a Vogel (golden-angle) disk, in tangent space with
// +z along the normal. Low-discrepancy rather than random: for the sample counts that are actually
// affordable here, an even spiral has far less variance than white noise, and the per-pixel
// `rotation` is what keeps the pattern from printing itself onto the image as a fixed texture.
float3 VogelHemisphere(float i, float n, float rotation)
{
    const float goldenAngle = 2.39996323;
    float r     = sqrt((i + 0.5) / n);
    float theta = i * goldenAngle + rotation;
    return float3(cos(theta) * r, sin(theta) * r, sqrt(saturate(1.0 - r * r)));
}

float4 OcclusionPS(VSOutput input) : COLOR
{
    float2 raw;
    float3 P = WorldPosAt(input.TexCoord, raw);

    // Nothing was drawn here -- the sky can't be occluded.
    if (raw.g < 0.5) return float4(1, 1, 1, 1);

    float3 N = ReconstructNormal(input.TexCoord, P, raw.r);

    // Tangent frame around the normal. The `up` pick just has to be non-parallel to N; the azimuth
    // it lands on is arbitrary and gets rotated per pixel anyway.
    float3 up = abs(N.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 T  = normalize(cross(up, N));
    float3 B  = cross(N, T);

    float rotation  = PixelNoise(input.TexCoord) * 6.28318531;
    float radiusJit = PixelNoise2(input.TexCoord);
    float n         = clamp(SampleCount, 4.0, (float)MAX_SAMPLES);
    float occlusion = 0.0;

    for (int i = 0; i < MAX_SAMPLES; i++)
    {
        if ((float)i >= n) break;

        float3 h   = VogelHemisphere((float)i, n, rotation);
        float3 dir = h.x * T + h.y * B + h.z * N;

        // Spread the sample distances across the radius with a second low-discrepancy sequence (the
        // plastic constant); sampling only the shell would miss near-contact occluders entirely,
        // which is where AO earns its keep. Offset PER PIXEL by radiusJit -- without that every
        // pixel on screen probes the same set of radii and only the azimuth varies, so the pattern
        // stops being noise (which the blur removes) and becomes correlated structure (which it
        // cannot), printing faint rings and lines over flat surfaces.
        float mag = Radius * lerp(0.15, 1.0, frac(radiusJit + (float)i * 0.7548776662));

        float3 sp   = P + dir * mag;
        float4 clip = mul(float4(sp, 1.0), ViewProjection);
        float2 suv  = clip.xy / max(clip.w, 1e-6);
        suv = float2(suv.x * 0.5 + 0.5, 0.5 - suv.y * 0.5);

        // tex2Dlod, not tex2D: a gradient-taking sample inside a loop with a runtime trip count
        // forces the compiler to unroll to MAX_SAMPLES, which would make SampleCount cost the same
        // whatever it's set to. Mip gradients are meaningless on a point-sampled depth buffer
        // anyway, so asking for level 0 explicitly costs nothing and keeps the loop dynamic.
        float2 sd = tex2Dlod(TextureSampler, float4(saturate(suv), 0, 0)).rg;

        // Both distances are measured from the same camera origin along the same ray (sp projects to
        // suv, so it LIES on that ray) -- so comparing them directly is exact, not an approximation.
        // Positive difference = real geometry sits in front of where we probed, i.e. it occludes.
        float diff = length(sp - CameraPosition) - sd.r * FarDistance;

        // A sample is only evidence if it landed in front of the camera, on screen, and on something
        // that was actually drawn. Everything else counts as unoccluded, which is the conservative
        // reading -- guessing "occluded" off-screen would ring the frame edges with dark bands.
        bool onScreen = clip.w > 0.0 && suv.x >= 0.0 && suv.x <= 1.0 && suv.y >= 0.0 && suv.y <= 1.0;
        float valid   = (onScreen && sd.g >= 0.5) ? 1.0 : 0.0;

        // Range check: an occluder far in front of the sample point is a different object seen past
        // this surface, not something touching it. Without this, every silhouette gets a dark halo
        // of the background object's shape.
        float range = smoothstep(0.0, 1.0, Radius / max(abs(diff), 1e-4));

        occlusion += valid * step(Bias, diff) * range;
    }

    float ao = saturate(1.0 - occlusion / n);
    ao = pow(ao, max(Power, 0.01));
    ao = saturate(lerp(1.0, ao, Intensity));

    if (FadeDistance > 0.0)
    {
        float dist  = raw.r * FarDistance;
        float start = FadeDistance * 0.6;
        ao = lerp(ao, 1.0, saturate((dist - start) / max(FadeDistance - start, 1e-3)));
    }

    return float4(ao, ao, ao, 1);
}

// ── bilateral blur ───────────────────────────────────────────────────────────────

// Gaussian across the image, but weighted down wherever the neighbour's depth says it belongs to a
// different surface -- otherwise the blur would drag occlusion across silhouettes and undo the edge
// handling the AO pass just did. The depth tolerance is Radius: two points further apart than the
// hemisphere that produced the AO are, by definition, not shading each other.
//
// Reads the AO map as the SpriteBatch source (t0) and the depth map through its pinned s1.
float BilateralBlur(float2 uv, float2 axis)
{
    float2 stepUv = axis / Resolution;

    // The AO tap comes FIRST, and not for style -- see the register note at the top of the file.
    // Reading the depth centre tap first (which is how this function wants to be written) puts
    // DepthSampler's texture in t0 and silently swaps the two inputs.
    float sum  = tex2D(TextureSampler, uv).r;
    float wsum = 1.0;

    float centerDepth = tex2D(DepthSampler, uv).r;

    float sigma = max(BlurRadius, 1.0) * 0.5;

    for (int i = 1; i <= MAX_BLUR; i++)
    {
        if ((float)i > BlurRadius) break;

        float spatial = exp(-((float)i * (float)i) / (2.0 * sigma * sigma));

        float2 uvA = uv + stepUv * (float)i;
        float2 uvB = uv - stepUv * (float)i;

        // Background reads 1.0 in the depth map, so it is automatically far from any real surface
        // and drops out of the blur without needing its own test.
        // tex2Dlod for the same reason as the AO loop above: keep BlurRadius an actual runtime cost
        // rather than something the compiler unrolls to MAX_BLUR regardless.
        float dA = tex2Dlod(DepthSampler, float4(uvA, 0, 0)).r;
        float dB = tex2Dlod(DepthSampler, float4(uvB, 0, 0)).r;

        float wA = spatial * saturate(1.0 - abs(dA - centerDepth) * FarDistance / max(Radius, 1e-3));
        float wB = spatial * saturate(1.0 - abs(dB - centerDepth) * FarDistance / max(Radius, 1e-3));

        sum  += tex2Dlod(TextureSampler, float4(uvA, 0, 0)).r * wA
              + tex2Dlod(TextureSampler, float4(uvB, 0, 0)).r * wB;
        wsum += wA + wB;
    }

    return sum / wsum;
}

float4 BlurHPS(VSOutput input) : COLOR
{
    float ao = BilateralBlur(input.TexCoord, float2(1, 0));
    return float4(ao, ao, ao, 1);
}

float4 BlurVPS(VSOutput input) : COLOR
{
    float ao = BilateralBlur(input.TexCoord, float2(0, 1));
    return float4(ao, ao, ao, 1);
}

// ── banding ──────────────────────────────────────────────────────────────────────

// 4x4 Bayer matrix, matching shaders/exponential-fog: deliberately coarse, because a single-pixel
// pattern gets chewed up by the 1-bit error diffusion and the final upscale.
static const float Bayer4x4[16] =
{
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
};

float BayerThreshold(float2 uv)
{
    float2 p = floor(uv * Resolution / max(DitherScale, 1.0));
    int x = (int)fmod(p.x, 4.0);
    int y = (int)fmod(p.y, 4.0);
    return (Bayer4x4[y * 4 + x] + 0.5) / 16.0;
}

// Snap the occlusion amount to a band. Which band is decided by comparing the position WITHIN the
// band against a threshold: a flat 0.5 is plain rounding (hard steps), the Bayer value spreads the
// decision across the pixel grid so each edge dissolves into a stipple. Dither blends the two, and
// this formulation keeps both ends reachable -- flooring alone could never produce full occlusion.
float Quantise(float v, float2 uv)
{
    if (Levels < 1.5) return v;
    float scaled = v * Levels;
    float lower  = floor(scaled);
    float t      = lerp(0.5, BayerThreshold(uv), saturate(Dither));
    return (lower + step(t, scaled - lower)) / Levels;
}

// ── depth-aware upsample ─────────────────────────────────────────────────────────

// One bilinear tap, but weighted by whether the AO texel is even talking about the same surface as
// the pixel being shaded. Same depth tolerance as the blur, and for the same reason: two points
// further apart than the hemisphere that produced the AO are not shading each other.
// Samples the AO source BEFORE the depth -- see the register note at the top of the file.
void AccumTap(sampler2D aoSrc, float2 uv, float centerDepth, float weight,
              inout float sum, inout float wsum)
{
    float ao = tex2D(aoSrc, uv).r;
    float d  = tex2D(DepthSampler, uv).r;
    float w  = weight * saturate(1.0 - abs(d - centerDepth) * FarDistance / max(Radius, 1e-3));
    sum  += ao * w;
    wsum += w;
}

// Manual bilinear interpolation of the AO map with a depth test on each of the four taps, which is
// what a hardware bilinear fetch cannot do. Degenerates to an exact single tap when the AO map is
// full resolution, so there is nothing to switch off at Downscale 1.
// `fallback` is the nearest AO tap, which the CALLER reads rather than this function -- deliberately.
// Every pass has to touch its own SpriteBatch source before any other sampler (the register note at
// the top), and that source is the scene in the composite but the AO map in the debug view, so the
// first read has to happen at the call site where the right sampler is known.
float UpsampleAO(sampler2D aoSrc, float2 uv, float centerDepth, float fallback)
{
    float2 texel  = 1.0 / AOResolution;
    float2 coord  = uv * AOResolution - 0.5;
    float2 f      = frac(coord);
    float2 origin = (floor(coord) + 0.5) * texel;

    float sum = 0.0, wsum = 0.0;
    AccumTap(aoSrc, origin,                            centerDepth, (1 - f.x) * (1 - f.y), sum, wsum);
    AccumTap(aoSrc, origin + float2(texel.x, 0),       centerDepth, f.x * (1 - f.y),       sum, wsum);
    AccumTap(aoSrc, origin + float2(0, texel.y),       centerDepth, (1 - f.x) * f.y,       sum, wsum);
    AccumTap(aoSrc, origin + float2(texel.x, texel.y), centerDepth, f.x * f.y,             sum, wsum);

    // All four neighbours disagreeing means this pixel is a sliver the AO map never resolved (a thin
    // railing, a distant edge). Falling back to the nearest tap is better than dividing by nothing,
    // and better than declaring it unoccluded, which is what would sparkle.
    return wsum > 1e-4 ? sum / wsum : fallback;
}

// ── composite ────────────────────────────────────────────────────────────────────

// The only pass reading three textures, and the only one where TextureSampler genuinely is the
// scene colour.
float4 CompositePS(VSOutput input) : COLOR
{
    float4 scene   = tex2D(TextureSampler, input.TexCoord);
    float  nearest = tex2D(AOSampler, input.TexCoord).r;
    float2 depth   = tex2D(DepthSampler, input.TexCoord).rg;
    if (depth.g < 0.5) return scene;   // the sky is never occluded, whatever the AO map says nearby

    float ao  = UpsampleAO(AOSampler, input.TexCoord, depth.r, nearest);
    float occ = Quantise(1.0 - ao, input.TexCoord);
    return float4(lerp(scene.rgb, OcclusionColor, saturate(occ)), scene.a);
}

// ── debug views ──────────────────────────────────────────────────────────────────

// The AO map alone, as it will actually be applied -- same depth-aware upsample, same banding -- so
// what you tune here is what the composite blends. White = fully lit, black = fully occluded.
// Handed the blurred AO map as its source, so TextureSampler is the AO map in this pass.
float4 DebugAOPS(VSOutput input) : COLOR
{
    float nearest     = tex2D(TextureSampler, input.TexCoord).r;   // must be the first sampler read
    float centerDepth = tex2D(DepthSampler, input.TexCoord).r;

    float ao = UpsampleAO(TextureSampler, input.TexCoord, centerDepth, nearest);
    ao = 1.0 - Quantise(1.0 - ao, input.TexCoord);
    return float4(ao, ao, ao, 1);
}

// The reconstructed normals the AO pass is actually working from, as RGB. Worth reaching for first
// when the occlusion looks wrong: everything downstream is only as good as this, and a bad normal
// field (flat facets, noise on curved surfaces, halos at silhouettes) is immediately visible here
// while being nearly impossible to diagnose from the AO map. Handed the depth map as its source.
float4 DebugNormalsPS(VSOutput input) : COLOR
{
    float2 raw;
    float3 P = WorldPosAt(input.TexCoord, raw);
    if (raw.g < 0.5) return float4(0.05, 0.05, 0.08, 1.0);

    float3 N = ReconstructNormal(input.TexCoord, P, raw.r);
    return float4(N * 0.5 + 0.5, 1.0);
}

technique Occlusion
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL OcclusionPS();
    }
}

technique BlurH
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL BlurHPS();
    }
}

technique BlurV
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL BlurVPS();
    }
}

technique Composite
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL CompositePS();
    }
}

technique DebugAO
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DebugAOPS();
    }
}

technique DebugNormals
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DebugNormalsPS();
    }
}
