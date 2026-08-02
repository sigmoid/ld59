#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

// Silhouette outlines from the scene depth/id buffer (shaders/scene-depth: red = linear distance,
// green = entity id, 0 = background).
//
// Three passes, because thickness done naively is expensive: a box kernel of radius R costs
// (2R+1)^2 samples per pixel, and it can't be separated into two axes because the id each sample is
// compared against changes per pixel. So the id comparison happens ONCE at one-pixel range to
// produce a hairline mask, and that mask -- a plain scalar -- is then dilated with two separable
// max filters, 2*(2R+1) samples total. Line width comes out uniform and the cost is linear in R.
//
//   1. Edge     : id discontinuity -> a 1px mask, laid on the nearer surface
//   2. DilateH  : horizontal max over the Dilate window
//   3. Composite: vertical max over the Dilate window, then blend the outline over the scene

// IMPORTANT, and the source of a long-lived bug here: under ps_4_0 the sampler register and the
// texture register are SEPARATE binding spaces, and `register(s1)` pins only the former. A pass
// that references one sampler gets its texture placed in t0 no matter what its sampler slot is --
// and t0 is the slot SpriteBatch writes the drawn sprite into, after the effect has bound its own
// parameters. So any single-texture pass would silently read the scene colour instead of the
// buffer it asked for. (Check with: fxc /Gec /T ps_4_0 /E <entry>, "Resource Bindings" table.)
//
// The fix is to not have a secondary binding to lose: every pass that reads exactly ONE texture
// takes it through TextureSampler and is handed that texture as the SpriteBatch source. Only
// Composite needs more than one, and there TextureSampler genuinely is the scene, so t0 is right.
sampler TextureSampler : register(s0);   // the SpriteBatch source: scene colour, or the pass input

// Only Composite reads these as secondary textures (t1/t2), where TextureSampler holds t0 down.
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

texture MaskTexture;
sampler2D MaskSampler : register(s2) = sampler_state
{
    Texture   = <MaskTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

float2 Resolution   = float2(1280.0, 720.0);
float3 OutlineColor = float3(0.1, 0.1, 0.15);
float  Opacity      = 1.0;

// Dilation window around each mask pixel, in pixels: (before, after) along whichever axis the pass
// is working on. Line width = Dilate.x + Dilate.y + 1.
//
// Two sides rather than one radius, because a symmetric window can only ever produce ODD widths
// (1 + 2R). An even width needs one extra pixel on one side only -- (0,1) for 2px, (1,2) for 4px --
// which the caller works out from the width it wants. The cost of that: at even widths the line
// sits half a pixel off-centre on the boundary, so it reads as very slightly inside an object's
// left/top edges and outside its right/bottom ones. Unavoidable without knowing which side of the
// boundary each mask pixel is on, and the scalar mask has deliberately thrown that away (see above).
float2 Dilate = float2(1.0, 1.0);

// Distance (world units) at which outlines have faded out completely. 0 disables the fade, keeping
// every line at full strength no matter how far away -- which is what you want when the outlines
// are meant to punch through fog.
float FadeDistance = 0.0;
float FarDistance  = 1000.0;

// Hard cap on the dilation loop so the shader has a bounded instruction count regardless of what
// Dilate is set to at runtime. Caps the line at 1 + 2*MAX_RADIUS px.
#define MAX_RADIUS 16

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

// True when the neighbour belongs to a different surface AND this pixel is the one that should
// carry the line. Two rules:
//   * background never carries it, so an object's outline sits on the object rather than smearing
//     into the sky;
//   * otherwise the NEARER surface carries it, so a line never bleeds onto whatever is behind it
//     and objects don't visually grow.
// Marking exactly one side is also what makes a hairline 1px wide instead of 2px straddling the
// boundary, which is what lets Thickness mean what it says.
bool IsEdge(float2 self, float2 other)
{
    if (abs(self.g - other.g) < 0.5) return false;   // same entity
    if (self.g < 0.5) return false;                  // self is background
    if (other.g < 0.5) return true;                  // neighbour is background -> self is the object
    return self.r <= other.r;                        // otherwise the nearer surface wins
}

// Reads the id buffer through TextureSampler -- the depth map is the SpriteBatch source for this
// pass, not the scene colour (see the register note at the top).
float4 EdgePS(VSOutput input) : COLOR
{
    float2 texel = 1.0 / Resolution;
    float2 uv    = input.TexCoord;
    float2 self  = tex2D(TextureSampler, uv).rg;

    // Four-neighbour test. Diagonals add nothing here: any diagonal-only discontinuity still has an
    // orthogonal neighbour crossing it one pixel over, and it would cost two more samples.
    bool edge =
        IsEdge(self, tex2D(TextureSampler, uv + float2( texel.x, 0)).rg) ||
        IsEdge(self, tex2D(TextureSampler, uv + float2(-texel.x, 0)).rg) ||
        IsEdge(self, tex2D(TextureSampler, uv + float2(0,  texel.y)).rg) ||
        IsEdge(self, tex2D(TextureSampler, uv + float2(0, -texel.y)).rg);

    return float4(edge ? 1.0 : 0.0, 0, 0, 1);
}

// Max of the mask along one axis, over `range.x` pixels back and `range.y` forward. Takes the
// sampler because the mask arrives in different slots depending on the pass: as the SpriteBatch
// source (t0) when dilation is all the pass does, and as a secondary texture when Composite also
// needs the scene.
float DilateAxis(sampler2D src, float2 uv, float2 stepUv, float2 range)
{
    float m = tex2D(src, uv).r;
    float reach = max(range.x, range.y);
    for (int i = 1; i <= MAX_RADIUS; i++)
    {
        if (i > reach) break;
        if (i <= range.x) m = max(m, tex2D(src, uv - stepUv * i).r);
        if (i <= range.y) m = max(m, tex2D(src, uv + stepUv * i).r);
    }
    return m;
}

float4 DilateHPS(VSOutput input) : COLOR
{
    float m = DilateAxis(TextureSampler, input.TexCoord, float2(1.0 / Resolution.x, 0), Dilate);
    return float4(m, 0, 0, 1);
}

float4 CompositePS(VSOutput input) : COLOR
{
    float4 scene = tex2D(TextureSampler, input.TexCoord);
    float  mask  = DilateAxis(MaskSampler, input.TexCoord, float2(0, 1.0 / Resolution.y), Dilate);

    float alpha = mask * Opacity;

    // Optional distance fade. Sampled at the outline pixel, which after dilation may be a pixel
    // adjacent to the surface that produced the line -- close enough at outline widths.
    if (FadeDistance > 0.0)
    {
        float dist = tex2D(DepthSampler, input.TexCoord).r * FarDistance;
        alpha *= 1.0 - saturate(dist / FadeDistance);
    }

    return float4(lerp(scene.rgb, OutlineColor, alpha), scene.a);
}

// ── debug views ──────────────────────────────────────────────────────────────────

float3 HsvToRgb(float3 hsv)
{
    float3 k = frac(hsv.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0;
    return hsv.z * lerp(1.0, saturate(abs(k) - 1.0), hsv.y);
}

// Each entity in its own colour, so you can see exactly what the outline pass is comparing:
// which surfaces the depth pass considers one object, which it splits, and where it drew nothing.
// Hues step by the golden ratio, which keeps consecutively-numbered (and therefore usually
// adjacent) entities far apart on the colour wheel instead of near-identical.
float4 DebugIdsPS(VSOutput input) : COLOR
{
    float id = tex2D(TextureSampler, input.TexCoord).g;   // depth map is this pass' source
    if (id < 0.5) return float4(0.05, 0.05, 0.08, 1.0);   // background
    return float4(HsvToRgb(float3(frac(id * 0.6180339887), 0.75, 1.0)), 1.0);
}

// The dilated edge mask on its own: white lines on black, exactly the shape that gets blended over
// the scene. Use it to judge thickness and to spot lines you didn't expect without the scene
// underneath confusing the picture. Drawn from the h-dilated mask as source, so it does the same
// vertical pass Composite does.
float4 DebugMaskPS(VSOutput input) : COLOR
{
    float mask = DilateAxis(TextureSampler, input.TexCoord, float2(0, 1.0 / Resolution.y), Dilate);
    return float4(mask, mask, mask, 1.0);
}

technique Edge
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL EdgePS();
    }
}

technique DilateH
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DilateHPS();
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

technique DebugIds
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DebugIdsPS();
    }
}

technique DebugMask
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL DebugMaskPS();
    }
}
