using Avalonia;
using SharpKVM;

namespace SharpKVM.Tests;

public class LayoutGeometryTests
{
    [Theory]
    [InlineData(EdgeDirection.Left, EdgeDirection.Right)]
    [InlineData(EdgeDirection.Right, EdgeDirection.Left)]
    [InlineData(EdgeDirection.Top, EdgeDirection.Bottom)]
    [InlineData(EdgeDirection.Bottom, EdgeDirection.Top)]
    [InlineData(EdgeDirection.None, EdgeDirection.None)]
    public void OppositeEdge_ReturnsExpectedDirection(EdgeDirection edge, EdgeDirection expected)
    {
        var opposite = LayoutGeometry.OppositeEdge(edge);

        Assert.Equal(expected, opposite);
    }

    [Fact]
    public void Overlap1D_HandlesReversedRanges()
    {
        var overlap = LayoutGeometry.Overlap1D(10, 0, 5, 15);

        Assert.Equal(5, overlap);
    }

    [Fact]
    public void AreEdgesAdjacent_RightEdgeWithinToleranceAndEnoughOverlap_ReturnsTrue()
    {
        var source = new Rect(0, 0, 100, 100);
        var target = new Rect(100.5, 10, 80, 90);

        var adjacent = LayoutGeometry.AreEdgesAdjacent(source, EdgeDirection.Right, target, tolerance: 1.0);

        Assert.True(adjacent);
    }

    [Fact]
    public void AreEdgesAdjacent_WhenPerpendicularOverlapIsExactlyMinimum_ReturnsFalse()
    {
        var source = new Rect(0, 0, 100, 100);
        var target = new Rect(100, 92, 100, 100);

        var adjacent = LayoutGeometry.AreEdgesAdjacent(source, EdgeDirection.Right, target, tolerance: 1.0);

        Assert.False(adjacent);
    }

    [Fact]
    public void MapPointAcrossEdge_WithVerticalOverlap_ClampsToOverlapRange()
    {
        var sourceRect = new Rect(0, 0, 100, 100);
        var targetRect = new Rect(100, 20, 100, 60);
        var sourcePoint = new Point(100, 95);

        var mapped = LayoutGeometry.MapPointAcrossEdge(sourcePoint, sourceRect, EdgeDirection.Right, targetRect);

        Assert.Equal(targetRect.Left, mapped.X);
        Assert.Equal(targetRect.Bottom, mapped.Y);
    }

    [Fact]
    public void MapPointAcrossEdge_WithoutVerticalOverlap_UsesRelativeRatio()
    {
        var sourceRect = new Rect(0, 0, 100, 100);
        var targetRect = new Rect(100, 200, 100, 50);
        var sourcePoint = new Point(100, 25);

        var mapped = LayoutGeometry.MapPointAcrossEdge(sourcePoint, sourceRect, EdgeDirection.Right, targetRect);

        Assert.Equal(targetRect.Left, mapped.X);
        Assert.Equal(212.5, mapped.Y);
    }

    [Fact]
    public void ClampRectByAnchor_LeftEdge_OnlyClampsY()
    {
        var rect = new Rect(150, -50, 40, 30);
        var globalBounds = new Rect(0, 0, 300, 200);

        var clamped = LayoutGeometry.ClampRectByAnchor(rect, EdgeDirection.Left, globalBounds);

        Assert.Equal(rect.X, clamped.X);
        Assert.Equal(globalBounds.Top, clamped.Y);
    }

    [Fact]
    public void TryGetEdgeAtLocalScreen_TopLeftCorner_PrioritizesLeftEdge()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        var found = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: 0, y: 0, out var edge);

        Assert.True(found);
        Assert.Equal(EdgeDirection.Left, edge);
    }

    [Fact]
    public void TryGetEdgeAtLocalScreen_PointOutsideExtendedBuffer_ReturnsFalse()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        var found = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: -50, y: -50, out var edge);

        Assert.False(found);
        Assert.Equal(EdgeDirection.None, edge);
    }
}
