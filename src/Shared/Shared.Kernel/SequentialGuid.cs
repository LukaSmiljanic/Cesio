using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Shared.Kernel;

/// <summary>
/// Generates GUIDs that are ascending in SQL Server's <c>uniqueidentifier</c> sort order (ADR-021, database.md),
/// the same layout as EF Core's <c>SequentialGuidValueGenerator</c>: random bytes 0–7, then a counter seeded with the
/// current UTC ticks in bytes 10–15 (compared first by SQL Server) and 8–9. Do not use <c>Guid.CreateVersion7()</c>
/// for clustered keys: SQL Server does not sort it by time.
/// </summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    public static Guid NewGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..8]);

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, Interlocked.Increment(ref _counter));

        // SQL Server compares bytes 10–15 first, then 8–9: the most significant counter bytes go to 10–15.
        counter[..6].CopyTo(bytes[10..]);
        counter[6..].CopyTo(bytes[8..10]);
        return new Guid(bytes);
    }
}
