using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Headless L03A material, map, response, and authoring tests.</summary>
public sealed class L03MaterialShaderArchitectureTests
{
    private static string Fixture(params string[] segments)
    {
        string relative = Path.Combine(segments);
        string? folder = AppContext.BaseDirectory;
        for (int n=0;n<12 && folder is not null;n++)
        {
            string path = Path.Combine(folder,relative);
            if (File.Exists(path)) return path;
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact] public void ManifestDefinesFiveComparableMaterialResponses()
    {
        var materials = SurfaceMaterialCatalog.FromJson(File.ReadAllText(Fixture(
            "Content","Lighting","L03_SurfaceMaterials.json"))).Materials;
        Assert.Equal(5,materials.Count);
        Assert.Equal(new[] { SurfaceLightResponse.Unlit, SurfaceLightResponse.Flat,
            SurfaceLightResponse.NormalMapped, SurfaceLightResponse.Rim,
            SurfaceLightResponse.Hybrid },materials.Select(x => x.Response));
        Assert.Single(materials.Select(x => x.Albedo).Distinct(StringComparer.Ordinal));
    }

    [Fact] public void OptionalMaterialMapsAreRealShippedAssets()
    {
        var materials = SurfaceMaterialCatalog.FromJson(File.ReadAllText(Fixture(
            "Content","Lighting","L03_SurfaceMaterials.json"))).Materials;
        foreach (var material in materials)
            foreach (var map in new[] { material.Albedo, material.Normal, material.Emission,
                material.Mask, material.Edge })
                if (map is not null)
                    Assert.True(File.Exists(Fixture("Content","Lighting","Textures",map)),
                        "Missing material map " + map);
    }

    [Fact] public void GpuSampleTexturesShareConsistentUvDimensions()
    {
        var materials = SurfaceMaterialCatalog.FromJson(File.ReadAllText(Fixture(
            "Content","Lighting","L03_SurfaceMaterials.json"))).Materials;
        foreach (var material in materials)
        {
            var paths = new[] { material.Albedo, material.Normal, material.Emission, material.Mask, material.Edge };
            int? width = null, height = null;
            foreach (string? name in paths)
            {
                if (name is null) continue;
                byte[] png = File.ReadAllBytes(Fixture("Content","Lighting","Textures",name));
                Assert.True(png.Length > 24 && png[0] == 137 && png[1] == 80, "Invalid PNG: " + name);
                int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4));
                int h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4));
                width ??= w; height ??= h;
                Assert.Equal(width.Value,w);
                Assert.Equal(height.Value,h);
            }
        }
    }

    [Fact] public void UnlitIgnoresDirectIllumination()
    {
        Assert.False(SurfaceMaterialRules.ReceivesDirectLight(SurfaceLightResponse.Unlit));
        Assert.False(SurfaceMaterialRules.ReceivesDiffuse(SurfaceLightResponse.Unlit));
        Assert.False(SurfaceMaterialRules.ReceivesRim(SurfaceLightResponse.Unlit));
    }

    [Theory]
    [InlineData(SurfaceLightResponse.Flat,true,false,false)]
    [InlineData(SurfaceLightResponse.NormalMapped,true,true,false)]
    [InlineData(SurfaceLightResponse.Rim,false,false,true)]
    [InlineData(SurfaceLightResponse.Hybrid,true,true,true)]
    public void ProfilesChooseIndependentDirectNormalAndRimBehaviors(
        SurfaceLightResponse mode,bool diffuse,bool normal,bool rim)
    {
        Assert.Equal(diffuse,SurfaceMaterialRules.ReceivesDiffuse(mode));
        Assert.Equal(normal,SurfaceMaterialRules.ReceivesNormal(mode));
        Assert.Equal(rim,SurfaceMaterialRules.ReceivesRim(mode));
    }

    [Fact] public void DuplicateMaterialIdsAreRejected()
    {
        string json = """{"version":1,"materials":[{"id":"X"},{"id":"X"}]}""";
        Assert.Throws<ArgumentException>(()=>SurfaceMaterialCatalog.FromJson(json));
    }

    [Fact] public void UnknownMaterialsOrFieldsAreRejected()
    {
        Assert.Throws<JsonException>(()=>SurfaceMaterialCatalog.FromJson(
            """{"version":1,"materials":[{"id":"X","response":"NotReal"}]}"""));
        Assert.Throws<JsonException>(()=>SurfaceMaterialCatalog.FromJson(
            """{"version":1,"materials":[{"id":"X","stray":123}]}"""));
    }

    [Fact] public void InvalidMapTraversalIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SurfaceMaterialProfile
        { Id = "X", Albedo = "../elsewhere.png" }.Validate());
        Assert.Throws<ArgumentException>(() => new SurfaceMaterialProfile
        { Id = "X", Normal = "C:\\bad.png" }.Validate());
    }

    [Fact] public void InvalidNumericValuesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceMaterialProfile
        { Id = "X", NormalStrength = double.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceMaterialProfile
        { Id = "X", SpecularPower = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceMaterialProfile
        { Id = "X", Ambient = -1 }.Validate());
    }

    [Fact] public void ModesDoNotHaveShadowCasterSideEffects()
    {
        // Material response intentionally contains no "casts shadow" flag.
        // The existing LightingSurfaceOptions owns that independent decision.
        var ctor = typeof(SurfaceMaterialProfile).GetProperties().Select(x=>x.Name).ToArray();
        Assert.DoesNotContain("CastsShadows",ctor);
        Assert.DoesNotContain("OccluderLayers",ctor);
    }

    [Fact] public void BuiltShaderIsIncludedByCodeFirstContentBuilder()
    {
        string builder = File.ReadAllText(Fixture("Content","Builder","Builder.cs"));
        Assert.Contains("Effects/L03_SurfaceMaterial.fx",builder);
        string shader = File.ReadAllText(Fixture("Content","Assets","Effects","L03_SurfaceMaterial.fx"));
        Assert.Contains("NormalMap",shader);
        Assert.Contains("EmissionMap",shader);
        Assert.Contains("EdgeMap",shader);
        Assert.Contains("PixelShader",shader);
    }
}
