using Quartz.Components;

namespace ld59.WalkingSim;

// Marker component: an entity is gathered into the navmesh bake only if it has this component
// (see SceneNavBaker.Bake). Replaces "bake everything except NoCollide" with an explicit opt-in,
// so decorative/background meshes never need a flag to stay out of the navmesh -- they simply
// don't get tagged. No properties; presence is the whole signal.
public sealed class NavMeshObstacleComponent : Component
{
}
