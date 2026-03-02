using SharpHook.Native;
using SharpKVM;

namespace SharpKVM.Tests;

public class RemotePressedKeyTrackerTests
{
    [Fact]
    public void TrackKeyDownAndUp_RemovesTrackedKey()
    {
        var tracker = new RemotePressedKeyTracker();
        const string clientKey = "mac-client";

        tracker.TrackKeyDown(clientKey, KeyCode.VcLeftShift);
        tracker.TrackKeyUp(clientKey, KeyCode.VcLeftShift);

        Assert.Equal(0, tracker.Count(clientKey));
    }

    [Fact]
    public void Drain_ReturnsUniqueSortedKeys_AndClearsClientState()
    {
        var tracker = new RemotePressedKeyTracker();
        const string clientKey = "mac-client";

        tracker.TrackKeyDown(clientKey, KeyCode.VcRightControl);
        tracker.TrackKeyDown(clientKey, KeyCode.VcLeftShift);
        tracker.TrackKeyDown(clientKey, KeyCode.VcLeftShift);

        var drained = tracker.Drain(clientKey);

        Assert.Equal(new[] { KeyCode.VcLeftShift, KeyCode.VcRightControl }, drained);
        Assert.Equal(0, tracker.Count(clientKey));
    }

    [Fact]
    public void TracksKeys_PerClientIndependently()
    {
        var tracker = new RemotePressedKeyTracker();
        const string firstClient = "client-a";
        const string secondClient = "client-b";

        tracker.TrackKeyDown(firstClient, KeyCode.VcLeftAlt);
        tracker.TrackKeyDown(secondClient, KeyCode.VcRightMeta);
        tracker.TrackKeyUp(firstClient, KeyCode.VcLeftAlt);

        Assert.Equal(0, tracker.Count(firstClient));
        Assert.Equal(1, tracker.Count(secondClient));
    }

    [Fact]
    public void Clear_RemovesOnlySpecifiedClient()
    {
        var tracker = new RemotePressedKeyTracker();
        const string firstClient = "client-a";
        const string secondClient = "client-b";

        tracker.TrackKeyDown(firstClient, KeyCode.VcLeftAlt);
        tracker.TrackKeyDown(secondClient, KeyCode.VcRightMeta);

        tracker.Clear(firstClient);

        Assert.Equal(0, tracker.Count(firstClient));
        Assert.Equal(1, tracker.Count(secondClient));
    }
}
