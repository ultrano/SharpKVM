using System.Runtime.InteropServices;

namespace SharpKVM
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InputPacket
    {
        public PacketType Type;
        public int X;
        public int Y;
        public int KeyCode;
        public int ClickCount;
    }
}
