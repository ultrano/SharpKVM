using Avalonia;
using SharpKVM;

namespace SharpKVM.Tests;

public class LayoutGeometryBoundaryTests
{
    [Theory]
    [InlineData(EdgeDirection.Left, true)]
    [InlineData(EdgeDirection.Right, true)]
    [InlineData(EdgeDirection.Top, false)]
    [InlineData(EdgeDirection.Bottom, false)]
    [InlineData(EdgeDirection.None, false)]
    public void IsHorizontalEdge_ReturnsExpectedResult(EdgeDirection edge, bool expected)
    {
        var isHorizontal = LayoutGeometry.IsHorizontalEdge(edge);

        Assert.Equal(expected, isHorizontal);
    }

    [Fact]
    public void HasPerpendicularOverlapForEntry_RightEdgeOverlapAboveMinimum_ReturnsTrue()
    {
        var source = new Rect(0, 0, 100, 100);
        var target = new Rect(100, 91, 80, 30);

        var hasOverlap = LayoutGeometry.HasPerpendicularOverlapForEntry(source, target, EdgeDirection.Right);

        Assert.True(hasOverlap);
    }

    [Fact]
    public void HasPerpendicularOverlapForEntry_RightEdgeOverlapAtMinimum_ReturnsFalse()
    {
        var source = new Rect(0, 0, 100, 100);
        var target = new Rect(100, 92, 80, 30);

        var hasOverlap = LayoutGeometry.HasPerpendicularOverlapForEntry(source, target, EdgeDirection.Right);

        Assert.False(hasOverlap);
    }

    [Fact]
    public void AttachToScreenEdge_RightEdge_ClampsYAndSnapsToRight()
    {
        var rect = new Rect(50, -100, 40, 30);
        var screen = new Rect(0, 0, 300, 200);

        var attached = LayoutGeometry.AttachToScreenEdge(rect, screen, EdgeDirection.Right);

        Assert.Equal(screen.Right, attached.X);
        Assert.Equal(-22, attached.Y);
    }

    [Fact]
    public void GetNearestPointOnEdge_BottomEdge_ClampsXToRect()
    {
        var source = new Point(-10, 0);
        var rect = new Rect(10, 20, 100, 50);

        var nearest = LayoutGeometry.GetNearestPointOnEdge(source, rect, EdgeDirection.Bottom);

        Assert.Equal(10, nearest.X);
        Assert.Equal(rect.Bottom, nearest.Y);
    }

    [Fact]
    public void IsCandidateOnSide_Left_UsesCenterComparison()
    {
        var root = new Rect(100, 100, 100, 100);
        var candidateOnLeft = new Rect(0, 110, 100, 100);
        var candidateOnRight = new Rect(250, 110, 100, 100);

        Assert.True(LayoutGeometry.IsCandidateOnSide(root, candidateOnLeft, EdgeDirection.Left));
        Assert.False(LayoutGeometry.IsCandidateOnSide(root, candidateOnRight, EdgeDirection.Left));
    }

    [Fact]
    public void IsGlobalOuterEdge_LeftWithinTolerance_ReturnsTrue()
    {
        var screen = new Rect(0.8, 0, 1920, 1080);
        var global = new Rect(0, 0, 3000, 1500);

        var isOuter = LayoutGeometry.IsGlobalOuterEdge(screen, EdgeDirection.Left, global);

        Assert.True(isOuter);
    }

    [Fact]
    public void IsGlobalOuterEdge_LeftOutsideTolerance_ReturnsFalse()
    {
        var screen = new Rect(1.2, 0, 1920, 1080);
        var global = new Rect(0, 0, 3000, 1500);

        var isOuter = LayoutGeometry.IsGlobalOuterEdge(screen, EdgeDirection.Left, global);

        Assert.False(isOuter);
    }

    [Fact]
    public void TryGetEdgeAtLocalScreen_SmallScreen_UsesMinimumEdgeBuffer()
    {
        var screen = new Rect(0, 0, 100, 100);

        var foundInsideMinBuffer = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: 5, y: 50, out var insideEdge);
        var foundOutsideMinBuffer = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: 6, y: 50, out var outsideEdge);

        Assert.True(foundInsideMinBuffer);
        Assert.Equal(EdgeDirection.Left, insideEdge);
        Assert.False(foundOutsideMinBuffer);
        Assert.Equal(EdgeDirection.None, outsideEdge);
    }

    [Fact]
    public void TryGetEdgeAtLocalScreen_LargeScreen_UsesMaximumEdgeBuffer()
    {
        var screen = new Rect(0, 0, 10000, 6000);

        var foundInsideMaxBuffer = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: 30, y: 3000, out var insideEdge);
        var foundOutsideMaxBuffer = LayoutGeometry.TryGetEdgeAtLocalScreen(screen, x: 31, y: 3000, out var outsideEdge);

        Assert.True(foundInsideMaxBuffer);
        Assert.Equal(EdgeDirection.Left, insideEdge);
        Assert.False(foundOutsideMaxBuffer);
        Assert.Equal(EdgeDirection.None, outsideEdge);
    }
}
