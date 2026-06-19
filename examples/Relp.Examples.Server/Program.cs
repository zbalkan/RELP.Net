using System.Net;
using System.Net.Sockets;
using System.Text;
using Relp;
using ZstdSharp;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 1601;
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.Error.WriteLine($"RELP zstd NDJSON example server listening on 0.0.0.0:{port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClientAsync(client));
}

static async Task HandleClientAsync(TcpClient client)
{
    using var clientRegistration = client;
    await using var stream = client.GetStream();
    using var decompressor = new Decompressor();

    var parser = new RelpParser();
    var pending = Array.Empty<byte>();
    var buffer = new byte[4096];

    while (true)
    {
        if (pending.Length > 0)
        {
            parser.Parse(pending);
            pending = Array.Empty<byte>();
        }

        while (!parser.IsComplete)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) return;
            parser.Parse(buffer.AsSpan(0, read));
        }

        pending = parser.RemainingBytes;
        var frame = parser.ToFrame();
        parser = new RelpParser();

        switch (frame.Command)
        {
            case RelpCommand.Open:
                await SendAckAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog");
                break;
            case RelpCommand.Syslog:
                PrintCompressedJsonLines(decompressor, frame.Buffer);
                await SendAckAsync(stream, frame.TransactionId, "200 OK");
                break;
            case RelpCommand.Close:
                await SendAckAsync(stream, frame.TransactionId, "200 OK");
                return;
            default:
                await SendAckAsync(stream, frame.TransactionId, "500 unsupported command");
                break;
        }
    }
}

static void PrintCompressedJsonLines(Decompressor decompressor, byte[] compressed)
{
    var payload = Encoding.UTF8.GetString(decompressor.Unwrap(compressed));
    foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Console.WriteLine($"{DateTimeOffset.UtcNow:O}: {line}");
    }
}

static async Task SendAckAsync(NetworkStream stream, int transactionId, string message)
{
    var frame = RelpFrameTx.FromCommandAndMessage(RelpCommand.Response, Encoding.UTF8.GetBytes(message));
    await stream.WriteAsync(frame.ToByteArray(transactionId));
    await stream.FlushAsync();
}
