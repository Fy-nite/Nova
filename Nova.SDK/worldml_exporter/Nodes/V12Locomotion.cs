using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Enables player movement (walk, sprint, jump) with desktop and VR locomotion settings.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/arrows_clockwise.svg")]
public partial class V12Locomotion : Node, IV12ComponentNode
{
    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 2.0f;
    [Export] public float SprintMultiplier { get; set; } = 1.9f;
    [Export] public float JumpStrength { get; set; } = 3.0f;
    [Export] public float Gravity { get; set; } = 9.8f;
    [Export] public bool CanJump { get; set; } = true;

    [ExportGroup("Look")]
    [Export] public float LookSensitivity { get; set; } = 1.2f;

    [ExportGroup("VR")]
    [Export] public float VrMoveSpeed { get; set; } = 3.0f;
    [Export] public bool VrSmoothLocomotion { get; set; } = true;
    [Export] public bool EnableHandTracking { get; set; } = true;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<LocomotionComponent" +
               $" moveSpeed=\"{MoveSpeed:F3}\"" +
               $" sprintMultiplier=\"{SprintMultiplier:F3}\"" +
               $" jumpStrength=\"{JumpStrength:F3}\"" +
               $" gravity=\"{Gravity:F3}\"" +
               $" canJump=\"{CanJump.ToString().ToLower()}\"" +
               $" lookSensitivity=\"{LookSensitivity:F3}\"" +
               $" vrMoveSpeed=\"{VrMoveSpeed:F3}\"" +
               $" vrSmoothLocomotion=\"{VrSmoothLocomotion.ToString().ToLower()}\"" +
               $" enableHandTracking=\"{EnableHandTracking.ToString().ToLower()}\"" +
               $" />\n";
    }
}
