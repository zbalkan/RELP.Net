using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Relp;
using ZstdSharp;

var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 1601;
var count = args.Length > 2 && int.TryParse(args[2], out var parsedCount) ? parsedCount : 5;

using var tcp = new TcpClient();
await tcp.ConnectAsync(host, port);
await using var stream = tcp.GetStream();
using var compressor = new Compressor(3);

var txId = new TxId();
await SendFrameAsync(stream, RelpFrameTx.FromCommandAndMessage(
    RelpCommand.Open,
    Encoding.UTF8.GetBytes("relp_version=0\nrelp_software=pyrelp-dotnet-example-client,1.0.0,https://github.com/zbalkan/pyrelp\ncommands=syslog")), txId.Next());
await ReadAckAsync(stream);

for (var index = 1; index <= count; index++)
{
    var line = JsonSerializer.Serialize(new
    {
        timestamp = DateTimeOffset.UtcNow,
        level = "info",
        message = $"hello from zstd-compressed NDJSON #{index}",
        sequence = index
    });

    var compressed = compressor.Wrap(Encoding.UTF8.GetBytes(line));
    await SendFrameAsync(stream, RelpFrameTx.FromMessage(compressed), txId.Next());
    await ReadAckAsync(stream);
}

await SendFrameAsync(stream, RelpFrameTx.FromCommand(RelpCommand.Close), txId.Next());
await ReadAckAsync(stream);

static async Task SendFrameAsync(NetworkStream stream, RelpFrameTx frame, int transactionId)
{
    var bytes = frame.ToByteArray(transactionId);
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();
}

static async Task ReadAckAsync(NetworkStream stream)
{
    var parser = new RelpParser();
    var buffer = new byte[4096];
    while (!parser.IsComplete)
    {
        var read = await stream.ReadAsync(buffer);
        if (read == 0) throw new IOException("Server closed the connection before acknowledging the frame.");
        parser.Parse(buffer.AsSpan(0, read));
    }

    var frame = parser.ToFrame();
    if (frame.Command != RelpCommand.Response || frame.GetResponseCode() != 200)
    {
        throw new InvalidOperationException($"Server returned unsuccessful RELP acknowledgement: {frame.GetData()}");
    }
}
