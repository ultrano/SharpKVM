namespace SharpKVM
{
    public enum PacketType : byte
    {
        Hello = 0,
        MouseMove = 1,
        MouseDown = 2,
        MouseUp = 3,
        KeyDown = 4,
        KeyUp = 5,
        MouseWheel = 6,
        Clipboard = 7,
        ClipboardFile = 8,
        ClipboardImage = 9,
        PlatformInfo = 10
    }
}
