using SharpHook.Native;
using SharpKVM;

namespace SharpKVM.Tests;

public class MacInputMappingTests
{
    [Theory]
    [InlineData(KeyCode.VcLeftMeta, KeyCode.VcLeftAlt)]
    [InlineData(KeyCode.VcRightMeta, KeyCode.VcRightAlt)]
    [InlineData(KeyCode.VcLeftAlt, KeyCode.VcLeftMeta)]
    [InlineData(KeyCode.VcRightAlt, KeyCode.VcRightMeta)]
    public void MapKeyCodeForMacRemote_SwapsMetaAndAlt(KeyCode input, KeyCode expected)
    {
        var mapped = MacInputMapping.MapKeyCodeForMacRemote(input);

        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void MapKeyCodeForMacRemote_LeavesOtherKeysUnchanged()
    {
        var mapped = MacInputMapping.MapKeyCodeForMacRemote(KeyCode.VcA);

        Assert.Equal(KeyCode.VcA, mapped);
    }

    [Theory]
    [InlineData(KeyCode.VcHangul)]
    [InlineData(KeyCode.VcCapsLock)]
    public void MapKeyCodeForMacRemote_LeavesHangulAndCapsLockUnchanged(KeyCode input)
    {
        var mapped = MacInputMapping.MapKeyCodeForMacRemote(input);

        Assert.Equal(input, mapped);
    }

    [Theory]
    [InlineData(0, true, 1u)]
    [InlineData(0, false, 2u)]
    [InlineData(1, true, 3u)]
    [InlineData(1, false, 4u)]
    [InlineData(2, true, 25u)]
    [InlineData(2, false, 26u)]
    [InlineData(3, true, 25u)]
    [InlineData(4, false, 26u)]
    public void TryMapRawMouseClickType_MapsButtons(int button, bool isDown, uint expectedType)
    {
        var ok = MacInputMapping.TryMapRawMouseClickType(button, isDown, out var type);

        Assert.True(ok);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void TryMapRawMouseClickType_NegativeButton_ReturnsFalse()
    {
        var ok = MacInputMapping.TryMapRawMouseClickType(-1, true, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData(0, 6u)]
    [InlineData(1, 7u)]
    [InlineData(2, 27u)]
    [InlineData(4, 27u)]
    public void TryMapRawMouseDragType_MapsButtons(int button, uint expectedType)
    {
        var ok = MacInputMapping.TryMapRawMouseDragType(button, out var type);

        Assert.True(ok);
        Assert.Equal(expectedType, type);
    }
}
