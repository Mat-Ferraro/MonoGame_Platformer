using System;
using System.IO;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

/// <summary>Exercises the exact LDtk terrain grid involved in the reported long-run floor drift.</summary>
public sealed class LdtkStaticFloorPrecisionTests
{
    [Fact]
    public void TerrainDemo_WalkingAcrossAdjacentCells_NeverCommitsStaticPenetration()
    {
        string? basePath = AppContext.BaseDirectory;
        string? fixture = null;
        for (int i = 0; i < 12 && basePath is not null; i++)
        {
            string path = Path.Combine(basePath,
                "PlatformerSandbox.Tests", "Fixtures", "LdtkTerrainDemoBaseline.ldtk");
            if (File.Exists(path)) { fixture = path; break; }
            basePath = Directory.GetParent(basePath)?.FullName;
        }
        if (fixture is null)
            throw new FileNotFoundException("Missing LDtk terrain fixture.");

        LdtkImportedLevel level = LdtkLevelImporter.FromFile(fixture);
        WorldSession world = LdtkLevelGym.Create(level,
            MovementGymSession.PlayableAssistancePreset,
            SimulationFaultSensitivity.Strict, 1, 501);

        // Reproduce the position shown in the crash report: walking at
        // collider 11794 (an LDtk grid-top at world Y=18).
        Aabb initial = new(new Vector2D(34.0, 18.0),
            new Vector2D(34.8, 19.6));
        world.RespawnPlayer(1, initial);

        for (int i = 0; i < 400; i++)
        {
            WorldStepResult result = world.Step([
                new WorldPlayerCommand(1, HorizontalDirection.Right)]);
            Assert.Equal((long)i + 1, result.WorldTick);
            Aabb player = world.GetPlayer(1).Bounds;
            foreach (var collider in level.World.StaticColliders)
                Assert.False(
                    player.Left < collider.Bounds.Right &&
                    player.Right > collider.Bounds.Left &&
                    player.Bottom < collider.Bounds.Top &&
                    player.Top > collider.Bounds.Bottom,
                    $"Committed overlap at tick {result.WorldTick}, collider {collider.Id}.");
        }
    }
}
