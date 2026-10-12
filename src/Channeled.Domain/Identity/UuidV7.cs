using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Channeled.Domain.Identity;

/// <summary>UUIDv7 (RFC 9562) generator for primary keys.</summary>
public static class UuidV7
{
    public static Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        BinaryPrimitives.WriteInt64BigEndian(bytes, unixMs << 16);

        RandomNumberGenerator.Fill(bytes[6..]);

        // version 7
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // variant RFC 4122
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
