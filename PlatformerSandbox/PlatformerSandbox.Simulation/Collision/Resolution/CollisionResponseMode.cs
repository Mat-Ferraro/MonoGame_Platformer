namespace PlatformerSandbox.Simulation.Collision.Resolution;

/// <summary>
/// Explicit post-impact velocity policies. Neither policy performs depenetration,
/// friction, bouncing, or acceleration integration.
/// </summary>
public enum CollisionResponseMode
{
    /// <summary>Remove the inward normal component; retain tangential velocity.</summary>
    Slide = 0,

    /// <summary>Stop both velocity components on the first collision.</summary>
    Stop = 1
}
