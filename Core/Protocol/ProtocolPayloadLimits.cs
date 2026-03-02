namespace SharpKVM
{
    public static class ProtocolPayloadLimits
    {
        public const int MaxClipboardTextBytes = 1024 * 1024;
        public const int MaxClipboardFileBytes = 100 * 1024 * 1024;
        public const int MaxClipboardImageBytes = 50 * 1024 * 1024;
        public const int MaxClientDiagnosticLogBytes = 16 * 1024;

        public static bool TryGetMaxPayload(PacketType type, out int maxBytes)
        {
            switch (type)
            {
                case PacketType.Clipboard:
                    maxBytes = MaxClipboardTextBytes;
                    return true;
                case PacketType.ClipboardFile:
                    maxBytes = MaxClipboardFileBytes;
                    return true;
                case PacketType.ClipboardImage:
                    maxBytes = MaxClipboardImageBytes;
                    return true;
                case PacketType.ClientDiagnosticLog:
                    maxBytes = MaxClientDiagnosticLogBytes;
                    return true;
                default:
                    maxBytes = 0;
                    return false;
            }
        }

        public static bool IsValidPayloadLength(PacketType type, int length)
        {
            return length > 0 && TryGetMaxPayload(type, out var maxBytes) && length <= maxBytes;
        }
    }
}
