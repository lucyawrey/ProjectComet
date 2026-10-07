using System.Numerics;

namespace Comet.Client.Tests;

public class CorrectionBlendTests
{
    [Fact]
    public void StartsWhereTheShapeIsDrawnAndEndsOnTheSimulation()
    {
        var blend = new CorrectionBlend(duration: 0.15);
        blend.Begin(drawnPosition: new Vector3(10, 0, 0), correctedPosition: new Vector3(4, 0, 0));

        Assert.True(blend.Active);
        Assert.Equal(new Vector3(6, 0, 0), blend.Offset);

        blend.Advance(0.075);
        Assert.Equal(3, blend.Offset.X, 3);

        blend.Advance(0.1);
        Assert.False(blend.Active);
        Assert.Equal(Vector3.Zero, blend.Offset);
    }

    [Fact]
    public void ACorrectionDuringABlendContinuesFromTheDrawnPosition()
    {
        var blend = new CorrectionBlend(duration: 0.15);
        blend.Begin(new Vector3(10, 0, 0), new Vector3(4, 0, 0));
        blend.Advance(0.075);
        var drawn = new Vector3(4, 0, 0) + blend.Offset;

        blend.Begin(drawn, new Vector3(0, 0, 0));
        Assert.Equal(drawn, Vector3.Zero + blend.Offset);
    }

    [Fact]
    public void ClearEndsTheBlend()
    {
        var blend = new CorrectionBlend();
        blend.Begin(Vector3.One, Vector3.Zero);
        blend.Clear();

        Assert.False(blend.Active);
        Assert.Equal(Vector3.Zero, blend.Offset);
    }
}
