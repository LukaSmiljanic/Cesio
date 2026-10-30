using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Documents;
using Modules.Documents.Contracts;

namespace Platform.Integration.Tests;

/// <summary>ADR-023: the ClamAV scanner speaks clamd's INSTREAM protocol and fails closed when clamd does not answer.</summary>
public sealed class ClamAvScannerTests
{
    [Theory]
    [InlineData("stream: OK", ScanStatus.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND", ScanStatus.Infected)]
    [InlineData("INSTREAM size limit exceeded. ERROR", ScanStatus.Pending)]
    public async Task The_answer_of_clamd_becomes_the_verdict(string answer, ScanStatus expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var received = FakeClamdAsync(listener, answer);
        var scanner = new ClamAvScanner("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, NullLogger<ClamAvScanner>.Instance);

        var verdict = await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes("file content")), TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict);
        Assert.Equal("file content", await received);
    }

    [Fact]
    public async Task An_unreachable_scanner_gives_no_verdict()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var scanner = new ClamAvScanner("127.0.0.1", port, NullLogger<ClamAvScanner>.Instance);

        Assert.Equal(ScanStatus.Pending, await scanner.ScanAsync(new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken));
    }

    /// <summary>Reads the command and the length-prefixed chunks up to the zero chunk, answers, and returns the payload.</summary>
    private static async Task<string> FakeClamdAsync(TcpListener listener, string answer)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var command = new byte["zINSTREAM\0".Length];
        await stream.ReadExactlyAsync(command);
        Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));
        var payload = new MemoryStream();
        var length = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(length);
            var size = BinaryPrimitives.ReadInt32BigEndian(length);
            if (size == 0)
            {
                break;
            }

            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk);
            payload.Write(chunk);
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(answer + "\0"));
        return Encoding.ASCII.GetString(payload.ToArray());
    }
}
