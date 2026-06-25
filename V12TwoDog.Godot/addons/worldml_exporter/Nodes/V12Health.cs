using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Gives an element a health pool with optional invincibility. Used for damageable objects.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/heart.svg")]
public partial class V12Health : Node, IV12ComponentNode
{
    [Export] public float MaxHealth { get; set; } = 100.0f;
    [Export] public bool IsInvincible { get; set; } = false;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<HealthComponent maxHealth=\"{MaxHealth:F3}\" isInvincible=\"{IsInvincible.ToString().ToLower()}\" />\n";
    }
}
