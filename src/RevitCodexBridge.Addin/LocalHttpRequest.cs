using System.Net.Sockets;
using System.Text;

namespace RevitCodexBridge.Addin;

internal sealed record LocalHttpRequest(string Method, string Path, string Body)
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    public static async Task<LocalHttpRequest> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(4096);
        var buffer = new byte[4096];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidOperationException("Client disconnected before sending headers.");
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = FindHeaderEnd(bytes);

            if (bytes.Count > MaxHeaderBytes)
            {
                throw new InvalidOperationException("HTTP request headers are too large.");
            }
        }

        var headerText = Encoding.ASCII.GetString(bytes.GetRange(0, headerEnd).ToArray());
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (requestLine.Length < 2)
        {
            throw new InvalidOperationException("Invalid HTTP request line.");
        }

        var method = requestLine[0].ToUpperInvariant();
        var path = requestLine[1].Split('?', 2)[0];
        var contentLength = 0;

        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(value);
            }
        }

        if (contentLength > MaxBodyBytes)
        {
            throw new InvalidOperationException("HTTP request body is too large.");
        }

        var bodyStart = headerEnd + 4;
        var availableBodyBytes = bytes.Count - bodyStart;

        while (availableBodyBytes < contentLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidOperationException("Client disconnected before sending the full body.");
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            availableBodyBytes = bytes.Count - bodyStart;
        }

        var body = contentLength == 0
            ? string.Empty
            : Encoding.UTF8.GetString(bytes.GetRange(bodyStart, contentLength).ToArray());

        return new LocalHttpRequest(method, path, body);
    }

    private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
    {
        for (var i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == '\r' &&
                bytes[i - 2] == '\n' &&
                bytes[i - 1] == '\r' &&
                bytes[i] == '\n')
            {
                return i - 3;
            }
        }

        return -1;
    }
}

