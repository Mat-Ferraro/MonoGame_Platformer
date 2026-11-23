using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Production-only material evaluation policy. The two-light shader
/// is reused in bounded batches; the older lab always uses its original slots.</summary>
public enum ProductionMaterialLightMode { AllEligible, CompareTwo, EmissionOnly }

/// <summary>One pair of lights, indexed into the deterministically sorted
/// production selection. Light 0 of each batch always maps to shader slot 0.</summary>
public readonly record struct ProductionMaterialLightBatch(int Start, int Count)
{
    public void Validate()
    {
        if (Start < 0 || Count is < 1 or > 2 || Start % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(Start));
    }
}

public static class ProductionMaterialLightPlan
{
    public const int MaximumLights = 12; // Matches LightingFrameBudget.High.MaxLights.
    public const int ShaderLightsPerBatch = 2;

    public static int Limit(ProductionMaterialLightMode mode) => mode switch
    {
        ProductionMaterialLightMode.AllEligible => MaximumLights,
        ProductionMaterialLightMode.CompareTwo => ShaderLightsPerBatch,
        ProductionMaterialLightMode.EmissionOnly => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static IReadOnlyList<ProductionMaterialLightBatch> Batches(int lightCount)
    {
        if (lightCount is < 0 or > MaximumLights)
            throw new ArgumentOutOfRangeException(nameof(lightCount));
        var result = new List<ProductionMaterialLightBatch>((lightCount + 1) / 2);
        for (int start = 0; start < lightCount; start += ShaderLightsPerBatch)
        {
            var batch = new ProductionMaterialLightBatch(start,
                Math.Min(ShaderLightsPerBatch, lightCount - start));
            batch.Validate();
            result.Add(batch);
        }
        return result;
    }
}
