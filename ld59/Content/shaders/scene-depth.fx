#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

// Camera depth pass. Writes LINEAR radial distance from the camera (divided by FarDistance) into
// the red channel of a Vector2-format target, rather than post-projection z. Two reasons:
//   * a full-screen effect can rebuild the shaded world position as camera + rayDir * depth *
//     FarDistance with no inverse-projection z curve to undo, and
//   * fog wants distance travelled through the medium, which is radial distance, not depth along
//     the view axis (otherwise fog would thin out toward the edges of the screen).
// Driven through the same Entity.DrawDepth path as the shadow shaders (which set only "World");
// the host sets ViewProjection / CameraPosition / FarDistance once per pass.

float4x4 World;
float4x4 ViewProjection;
float3   CameraPosition;
float    FarDistance = 1000.0;

// Per-entity id written to green, set by the host before each entity draws. Starts at 1 so the
// target's 0 clear doubles as "background" -- readers that only care whether geometry was drawn
// (fog) test against 0.5, while readers that need to tell surfaces apart (outlines) compare ids
// directly. Stored as a raw float in a 32-bit channel, so it stays exact well past any realistic
// entity count.
float EntityId = 1.0;

struct VSInput  { float4 Position : POSITION; };
struct VSOutput
{
    float4 Position : POSITION;
    float3 WorldPos : TEXCOORD0;
};

VSOutput VS(VSInput input)
{
    VSOutput output;
    float4 worldPos  = mul(input.Position, World);
    output.Position  = mul(worldPos, ViewProjection);
    // Interpolate the world position and take the length per pixel. Distance itself isn't linear
    // in world space, so interpolating it directly would sag across large triangles (a big floor
    // plane would read visibly wrong between its verts); world position interpolates exactly.
    output.WorldPos  = worldPos.xyz;
    return output;
}

float4 PS(VSOutput input) : COLOR
{
    // Green carries the entity id (0 = background), and is why depth is NOT clamped: a reader can
    // tell a surface 10x past FarDistance from empty background by the id alone, so distant
    // geometry keeps its true distance instead of collapsing onto the clear value and getting
    // mistaken for sky.
    float d = length(input.WorldPos - CameraPosition) / FarDistance;
    return float4(d, EntityId, 0, 1);
}

technique SceneDepth
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS();
        PixelShader  = compile PS_SHADERMODEL PS();
    }
}
