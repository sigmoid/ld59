using Quartz.Components;

namespace ld59.WalkingSim;

// Marks a scene entity as interactable in the walking sim. Picking is done with the ID-buffer
// pass in UI3DScene, which only renders Mesh3D geometry — so an interactable entity MUST also
// carry a Mesh3DComponent, or it will never appear in the ID buffer and can never be hovered.
//
// Auto-registers with EntityFactory by reflection, so scene XML references it as
// Type="Interactable3D" with Property Name="PromptText"/"Action"/"Target"/"Message".
//
// Interaction is data-driven: InteractionDispatcher switches on Action and interprets Target
// (a file path, glyph id, entity name, ...) and Message (display payload) per action.
public class Interactable3DComponent : Component
{
    // Shown next to the crosshair while hovered, e.g. "read the tablet".
    public string PromptText { get; set; } = "interact";

    // Action verb the dispatcher routes on (e.g. "show-text", "reveal-file", "play-sound").
    public string Action { get; set; } = "";

    // The thing acted on; meaning depends on Action (file path, glyph id, entity name, ...).
    public string Target { get; set; } = "";

    // Payload for the action; for "show-text" this is the text shown in a notification.
    public string Message { get; set; } = "";

    // How close (world units) the player's camera eye must be to this entity's origin for it to
    // become hoverable. Raise it for large or elevated objects, whose origin can sit well away
    // from the surface the player actually looks at. Editable in the editor Inspector.
    public float InteractRange { get; set; } = 10f;

    // Assigned at scene load by UI3DScene (1..255, encoded into the ID buffer's red channel).
    // Runtime-only: a field (not a property) so the reflection-based ComponentSerializer and the
    // editor Inspector never touch it — mirrors Mesh3DComponent.HighlightFactor. It must NOT be a
    // property named Id: that collides with Component.Id (Guid), making GetProperty("Id") ambiguous
    // and breaking scene load ("Ambiguous match found for '... Int32 Id'"). Hence the PickId name.
    public int PickId;
}
