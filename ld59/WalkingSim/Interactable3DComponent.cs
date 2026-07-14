using Quartz.Components;

namespace ld59.WalkingSim;

// Marks a scene entity as interactable in the walking sim. Picking is done with the ID-buffer
// pass in UI3DScene, which only renders Mesh3D geometry — so an interactable entity MUST also
// carry a Mesh3DComponent, or it will never appear in the ID buffer and can never be hovered.
//
// Auto-registers with EntityFactory by reflection, so scene XML references it as
// Type="Interactable3D" with Property Name="PromptText"/"Action"/"Message".
public class Interactable3DComponent : Component
{
    // Shown next to the crosshair while hovered, e.g. "read the tablet".
    public string PromptText { get; set; } = "interact";

    // Action verb the host handles (reserved for routing: "open-file", "show-word", ...).
    public string Action { get; set; } = "";

    // Payload for the action; v1 host just shows it in a notification.
    public string Message { get; set; } = "";

    // Assigned at scene load by UI3DScene (1..255, encoded into the ID buffer's red channel).
    // Runtime-only; never serialized.
    public int Id { get; set; }
}
