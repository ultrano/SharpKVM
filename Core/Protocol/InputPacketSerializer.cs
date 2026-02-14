using System;
using System.Runtime.InteropServices;

namespace SharpKVM
{
    public static class InputPacketSerializer
    {
        public static byte[] Serialize(InputPacket packet)
        {
            int size = Marshal.SizeOf(packet);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(packet, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static bool TryDeserialize(ReadOnlySpan<byte> data, out InputPacket packet)
        {
            int size = Marshal.SizeOf<InputPacket>();
            if (data.Length != size)
            {
                packet = default;
                return false;
            }

            byte[] buffer = data.ToArray();
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                packet = Marshal.PtrToStructure<InputPacket>(handle.AddrOfPinnedObject());
                return true;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
