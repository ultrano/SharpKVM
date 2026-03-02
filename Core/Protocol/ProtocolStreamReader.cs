using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpKVM
{
    public enum InputPacketHeaderReadStatus
    {
        Success,
        EndOfStream,
        InvalidHeader
    }

    public static class ProtocolStreamReader
    {
        public static readonly int InputPacketHeaderSize = Marshal.SizeOf<InputPacket>();

        public static byte[] CreateInputPacketHeaderBuffer() => new byte[InputPacketHeaderSize];

        public static async Task<(InputPacketHeaderReadStatus Status, InputPacket Packet)> ReadInputPacketHeaderAsync(
            Stream stream,
            byte[] headerBuffer,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(headerBuffer);

            if (headerBuffer.Length < InputPacketHeaderSize)
            {
                throw new ArgumentException(
                    $"Header buffer must be at least {InputPacketHeaderSize} bytes.",
                    nameof(headerBuffer));
            }

            if (!await ReadExactAsync(stream, headerBuffer, InputPacketHeaderSize, cancellationToken).ConfigureAwait(false))
            {
                return (InputPacketHeaderReadStatus.EndOfStream, default);
            }

            if (!InputPacketSerializer.TryDeserialize(headerBuffer.AsSpan(0, InputPacketHeaderSize), out var packet))
            {
                return (InputPacketHeaderReadStatus.InvalidHeader, default);
            }

            return (InputPacketHeaderReadStatus.Success, packet);
        }

        public static async Task<bool> ReadExactAsync(
            Stream stream,
            byte[] buffer,
            int size,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(buffer);

            if (size < 0 || size > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            int totalRead = 0;
            while (totalRead < size)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, size - totalRead),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    return false;
                }

                totalRead += read;
            }

            return true;
        }

        public static async Task<byte[]?> ReadPayloadAsync(
            Stream stream,
            PacketType type,
            int length,
            CancellationToken cancellationToken = default)
        {
            if (!ProtocolPayloadLimits.IsValidPayloadLength(type, length))
            {
                return null;
            }

            byte[] payload = new byte[length];
            if (!await ReadExactAsync(stream, payload, length, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return payload;
        }
    }
}
