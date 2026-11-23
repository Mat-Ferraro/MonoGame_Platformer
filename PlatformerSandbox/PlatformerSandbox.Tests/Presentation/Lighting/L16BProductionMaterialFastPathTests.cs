using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L16BProductionMaterialFastPathTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NeutralMapFallbackOccursOnlyForAbsentMapCallbacks(bool normal, bool edge)
    {
        var plan = ProductionMaterialPassPlanner.Maps(normal, edge);
        Assert.Equal(normal, plan.CaptureNormal);
        Assert.Equal(edge, plan.CaptureEdge);
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, true, true, true, true)] // Back rim needs shadows even if face does not.
    [InlineData(true, true, true, true, true)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, true, true, false, false)]
    public void VisibilityFallbackHonorsDepthAndBudgetRules(
        bool receiver, bool backRim, bool casts, bool budget, bool expected)
    {
        Assert.Equal(expected,
            ProductionMaterialPassPlanner.NeedsVisibility(receiver, backRim, casts, budget));
    }

    [Fact]
    public void VisibilityPolicyExhaustivelyMatchesOriginalRenderingRules()
    {
        foreach (bool receives in new[] { false, true })
        foreach (bool rim in new[] { false, true })
        foreach (bool casts in new[] { false, true })
        foreach (bool quota in new[] { false, true })
        {
            bool before = (receives || rim) && casts && quota;
            Assert.Equal(before,
                ProductionMaterialPassPlanner.NeedsVisibility(receives, rim, casts, quota));
        }
    }

    [Fact]
    public void MissingOneMapNeverDisablesTheOther()
    {
        var normalOnly = ProductionMaterialPassPlanner.Maps(true, false);
        var edgeOnly = ProductionMaterialPassPlanner.Maps(false, true);
        Assert.True(normalOnly.CaptureNormal);
        Assert.False(normalOnly.CaptureEdge);
        Assert.False(edgeOnly.CaptureNormal);
        Assert.True(edgeOnly.CaptureEdge);
    }
}
