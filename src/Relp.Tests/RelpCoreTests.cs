using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Relp.Tests;

[TestClass]
public sealed class RelpCoreTests
{
    [TestMethod]
    public void FrameTxFormatsSyslogMessage()
    {
        var frame = RelpFrameTx.FromMessage(Encoding.UTF8.GetBytes("Hello World!"));
        Assert.AreEqual(RelpCommand.Syslog, frame.Command);
        Assert.AreEqual("1 syslog 12 Hello World!", frame.ToProtocolString());
    }

    [TestMethod]
    public void FrameTxOmitsPayloadSeparatorForEmptyFrames()
    {
        var frame = RelpFrameTx.FromCommand(RelpCommand.Close);
        Assert.AreEqual("7 close 0", frame.ToProtocolString(7));
        Assert.AreEqual("7 close 0\n", Encoding.ASCII.GetString(frame.ToByteArray(7)));
    }

    [TestMethod]
    public void FrameTxPreservesPayloadBytes()
    {
        var frame = RelpFrameTx.FromCommandAndMessage(RelpCommand.Syslog, [0xFF, 0x00, 0x41]);
        byte[] expected = [(byte)'4', (byte)' ', (byte)'s', (byte)'y', (byte)'s', (byte)'l', (byte)'o', (byte)'g', (byte)' ', (byte)'3', (byte)' ', 0xFF, 0x00, 0x41, (byte)'\n'];
        CollectionAssert.AreEqual(expected, frame.ToByteArray(4));
    }

    [TestMethod]
    public void ParserReadsCompleteResponse()
    {
        var parser = new RelpParser();
        parser.Parse(Encoding.UTF8.GetBytes("2 rsp 6 200 OK\n"));
        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual(2, parser.TransactionId);
        Assert.AreEqual(RelpCommand.Response, parser.Command);
        Assert.AreEqual(6, parser.Length);
        Assert.AreEqual("200 OK", Encoding.UTF8.GetString(parser.Data));
    }

    [TestMethod]
    public void ParserWaitsForDeclaredPayloadLengthBeforeCompleting()
    {
        var parser = new RelpParser();
        parser.Parse(Encoding.UTF8.GetBytes("2 rsp 11 200 OK\nmore"));
        Assert.IsFalse(parser.IsComplete);
        parser.Parse((byte)'\n');
        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual("200 OK\nmore", Encoding.UTF8.GetString(parser.Data));
    }

    [TestMethod]
    public void ParserAcceptsZeroLengthFrameWithoutPayloadSeparator()
    {
        var parser = new RelpParser();
        parser.Parse(Encoding.UTF8.GetBytes("1 open 0\n"));
        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual(1, parser.TransactionId);
        Assert.AreEqual(RelpCommand.Open, parser.Command);
        Assert.IsEmpty(parser.Data);
    }

    [TestMethod]
    public void ParserKeepsBytesAfterCompleteFrameForBufferedReads()
    {
        var parser = new RelpParser();
        parser.Parse(Encoding.UTF8.GetBytes("2 rsp 6 200 OK\n3 rsp 6 200 OK\n"));

        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual(2, parser.TransactionId);
        Assert.AreEqual("3 rsp 6 200 OK\n", Encoding.UTF8.GetString(parser.RemainingBytes));
    }

    [TestMethod]
    public void ParserRejectsMismatchedPayloadTerminator()
    {
        var parser = new RelpParser();
        Assert.ThrowsExactly<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("2 rsp 5 200 OK\n")));
    }

    [TestMethod]
    public void ParserRejectsTransactionIdPastProtocolMaximum()
    {
        var parser = new RelpParser();
        Assert.ThrowsExactly<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("1000000000 rsp 6 200 OK\n")));
    }

    [TestMethod]
    public void ParserRejectsTransactionIdBelowProtocolMinimum()
    {
        var parser = new RelpParser();
        Assert.ThrowsExactly<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("0 rsp 6 200 OK\n")));
    }

    [TestMethod]
    public void TxIdExposesProtocolBoundsAndRejectsNegativeValues()
    {
#pragma warning disable MSTEST0032 // Assertion condition is always true
        Assert.AreEqual(1, TxId.MinValue);
        Assert.AreEqual(999_999_999, TxId.MaxValue);
#pragma warning restore MSTEST0032 // Assertion condition is always true
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = (TxId)(-1));
    }

    [TestMethod]
    public void TxIdNextReturnsMaximumBeforeWrapping()
    {
        var txId = new TxId(TxId.MaxValue);
        Assert.AreEqual(TxId.MaxValue, txId.Next());
        Assert.AreEqual(1, txId.Next());
    }

    [TestMethod]
    public void TxIdNextLoopsFromMaximumBackToOne()
    {
        var txId = new TxId(TxId.MaxValue - 1);

        Assert.AreEqual(TxId.MaxValue - 1, txId.Next());
        Assert.AreEqual(TxId.MaxValue, txId.Next());
        Assert.AreEqual(1, txId.Next());
        Assert.AreEqual(2, txId.Next());
    }

    [TestMethod]
    public void TxIdPreviousLoopsFromOneBackToMaximum()
    {
        var txId = new TxId(2);

        Assert.AreEqual(2, txId.Previous());
        Assert.AreEqual(1, txId.Previous());
        Assert.AreEqual(TxId.MaxValue, txId.Previous());
        Assert.AreEqual(TxId.MaxValue - 1, txId.Previous());
    }

    [TestMethod]
    public void TxIdCastsToAndFromIntegerTypes()
    {
        var txId = new TxId(42);
        int signed = txId;
        uint unsigned = txId;

        Assert.AreEqual(42, signed);
        Assert.AreEqual(42u, unsigned);
        Assert.AreEqual(42, ((TxId)42).Value);
        Assert.AreEqual(42, ((TxId)42u).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = (TxId)0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = (TxId)1_000_000_000u);
    }

    [TestMethod]
    public void FrameTxRejectsTransactionIdsOutsideProtocolBounds()
    {
        var frame = RelpFrameTx.FromCommand(RelpCommand.Close);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => frame.ToByteArray(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => frame.ToProtocolString(TxId.MaxValue + 1));
    }

    [TestMethod]
    public void TxIdBehavesLikeUnsignedBackedValue()
    {
        var txId = new TxId(42);

        Assert.AreEqual(42u, txId.UnsignedValue);
        Assert.AreEqual("42", txId.ToString());
        Assert.IsTrue(txId.Equals((TxId)42));
        Assert.IsTrue(txId.Equals(42u));
        Assert.IsGreaterThan(0, txId.CompareTo(41u));
        Assert.IsTrue(TxId.TryParse("42", out var parsed));
        Assert.AreEqual(txId, parsed);
        Assert.IsFalse(TxId.TryParse("0", out _));
    }

    [TestMethod]
    public void TxIdOperatorsWrapAroundProtocolBounds()
    {
        var txId = new TxId(TxId.MaxValue);
        txId++;
        Assert.AreEqual(1, txId.Value);

        txId--;
        Assert.AreEqual(TxId.MaxValue, txId.Value);

        Assert.AreEqual(2, (txId + 2).Value);
        Assert.AreEqual(TxId.MaxValue - 1, (txId - 1).Value);
    }

    [TestMethod]
    public void FrameRxRejectsInvalidConstructorArguments()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RelpFrameRx(0, RelpCommand.Response, 0, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RelpFrameRx(-1, RelpCommand.Response, 0, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RelpFrameRx(1, RelpCommand.Response, -1, []));
        Assert.ThrowsExactly<ArgumentException>(() => new RelpFrameRx(1, RelpCommand.Response, 2, [0x41]));
    }

    [TestMethod]
    public void ConnectionRejectsInvalidConstructorArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RelpConnection("", 601));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RelpConnection("localhost", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RelpConnection("localhost", 65_536));
    }

    [TestMethod]
    public async Task ConnectionRejectsUseAfterDispose()
    {
        await using var connection = new RelpConnection("localhost", 601);
        await connection.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.ConnectAsync());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.SendAsync([]));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.ReceiveAsync());
    }

    [TestMethod]
    public void ResponseCodeMustBeThreeDigits()
    {
        var frame = new RelpFrameRx(2, RelpCommand.Response, 6, Encoding.UTF8.GetBytes("200 OK"));
        Assert.AreEqual(200, frame.GetResponseCode());
        Assert.ThrowsExactly<FormatException>(() => new RelpFrameRx(2, RelpCommand.Response, 7, Encoding.UTF8.GetBytes("2000 OK")).GetResponseCode());
    }

    [TestMethod]
    public void TxIdNextIsSafeForConcurrentCallers()
    {
        var txId = new TxId();
        var ids = new ConcurrentBag<int>();

        Parallel.For(0, 1_000, _ => ids.Add(txId.Next()));

        Assert.HasCount(1_000, ids.Distinct());
        Assert.Contains(1, ids);
        Assert.Contains(1_000, ids);
    }

    [TestMethod]
    public void RequestIdNextIsSafeForConcurrentCallers()
    {
        var requestId = new RequestId();
        var ids = new ConcurrentBag<int>();

        Parallel.For(0, 1_000, _ => ids.Add(requestId.Next()));

        Assert.HasCount(1_000, ids.Distinct());
        Assert.Contains(0, ids);
        Assert.Contains(999, ids);
    }

    [TestMethod]
    public void RequestIdsIncrementFromZero()
    {
        var requestId = new RequestId();
        Assert.AreEqual(0, requestId.Next());
        Assert.AreEqual(1, requestId.Next());
        Assert.AreEqual(2, requestId.Next());
    }

    [TestMethod]
    public void WindowTracksPendingTransactions()
    {
        var window = new RelpWindow();
        window.PutPending(12, 1234);
        Assert.IsTrue(window.IsPending(12));
        Assert.AreEqual(1234, window.GetPending(12));
        Assert.AreEqual(1, window.Size);
        window.RemovePending(12);
        Assert.IsFalse(window.IsPending(12));
    }

    [TestMethod]
    public void ParserRejectsAdditionalInputAfterCompletion()
    {
        var parser = new RelpParser();
        parser.Parse(Encoding.UTF8.GetBytes("2 rsp 6 200 OK\n"));

        Assert.ThrowsExactly<InvalidOperationException>(() => parser.Parse((byte)'x'));
    }

    [TestMethod]
    public void FramesDefensivelyCopyPayloadBuffers()
    {
        var outboundPayload = Encoding.UTF8.GetBytes("hello");
        var outbound = RelpFrameTx.FromMessage(outboundPayload);
        outboundPayload[0] = (byte)'j';
        outbound.Message[1] = (byte)'a';

        Assert.AreEqual("1 syslog 5 hello", outbound.ToProtocolString());

        var inboundPayload = Encoding.UTF8.GetBytes("200 OK");
        var inbound = new RelpFrameRx(1, RelpCommand.Response, inboundPayload.Length, inboundPayload);
        inboundPayload[0] = (byte)'5';
        inbound.Buffer[1] = (byte)'9';

        Assert.AreEqual("200 OK", inbound.GetData());
    }

    [TestMethod]
    public async Task SessionCompletesOpenSendAndCloseAgainstLoopbackServer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedPayloads = new ConcurrentQueue<string>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunSingleClientRelpServerAsync(listener, receivedPayloads, timeout.Token);

        await using var connection = new RelpConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new RelpSession(connection);

        await session.OpenAsync(timeout.Token);
        await session.SendMessageAsync(Encoding.UTF8.GetBytes("integration payload"), timeout.Token);
        await session.SendMessageAsync(Encoding.UTF8.GetBytes("second payload"), timeout.Token);
        await session.CloseAsync(timeout.Token);

        await serverTask;
        CollectionAssert.AreEqual(
            new[] { "integration payload", "second payload" },
            receivedPayloads.ToArray());
        Assert.HasCount(2, receivedPayloads);
    }

    [TestMethod]
    public async Task SessionSendsCloseWhenOpenResponseOmitsRelpVersionOffer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedPayloads = new ConcurrentQueue<string>();
        var observedCommands = new ConcurrentQueue<RelpCommand>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunSingleClientRelpServerAsync(
            listener,
            receivedPayloads,
            timeout.Token,
            openResponse: "200 OK",
            observedCommands: observedCommands);

        await using var connection = new RelpConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new RelpSession(connection);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.OpenAsync(timeout.Token));

        await serverTask;
        CollectionAssert.AreEqual(
            new[] { RelpCommand.Open, RelpCommand.Close },
            observedCommands.ToArray());
        Assert.IsEmpty(receivedPayloads);
        Assert.IsFalse(session.IsActive);
    }

    [TestMethod]
    public async Task SessionTransmitsLongPayloadAgainstLoopbackServer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedPayloads = new ConcurrentQueue<string>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunSingleClientRelpServerAsync(listener, receivedPayloads, timeout.Token);
        var payload = "testmessage" + new string('7', 131_072 - "testmessage".Length);

        await using var connection = new RelpConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new RelpSession(connection);

        await session.OpenAsync(timeout.Token);
        await session.SendMessageAsync(Encoding.UTF8.GetBytes(payload), timeout.Token);
        await session.CloseAsync(timeout.Token);

        await serverTask;
        Assert.IsTrue(receivedPayloads.TryDequeue(out var received));
        Assert.AreEqual(payload.Length, received.Length);
        Assert.AreEqual(payload, received);
        Assert.IsEmpty(receivedPayloads);
    }

    [TestMethod]
    public async Task SessionTransmitsMessageSequenceAgainstLoopbackServer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedPayloads = new ConcurrentQueue<string>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunSingleClientRelpServerAsync(listener, receivedPayloads, timeout.Token);
        var expected = Enumerable.Range(1, 128).Select(value => value.ToString("D8")).ToArray();

        await using var connection = new RelpConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new RelpSession(connection);

        await session.OpenAsync(timeout.Token);
        foreach (var payload in expected)
        {
            await session.SendMessageAsync(Encoding.UTF8.GetBytes(payload), timeout.Token);
        }
        await session.CloseAsync(timeout.Token);

        await serverTask;
        CollectionAssert.AreEqual(expected, receivedPayloads.ToArray());
        Assert.HasCount(expected.Length, receivedPayloads);
    }

    [TestMethod]
    public async Task ConnectionReportsUnavailableReceiverOnConnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await using var connection = new RelpConnection(IPAddress.Loopback.ToString(), port);

        await Assert.ThrowsExactlyAsync<SocketException>(() => connection.ConnectAsync(timeout.Token));
    }

    private static async Task RunSingleClientRelpServerAsync(
        TcpListener listener,
        ConcurrentQueue<string> receivedPayloads,
        CancellationToken cancellationToken,
        string openResponse = "200 OK\nrelp_version=0\ncommands=syslog",
        ConcurrentQueue<RelpCommand>? observedCommands = null)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var parser = new RelpParser();
            var pending = Array.Empty<byte>();
            var buffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                if (pending.Length > 0)
                {
                    parser.Parse(pending);
                    pending = Array.Empty<byte>();
                }

                while (!parser.IsComplete)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    parser.Parse(buffer.AsSpan(0, read));
                }

                pending = parser.RemainingBytes;
                var frame = parser.ToFrame();
                parser = new RelpParser();
                observedCommands?.Enqueue(frame.Command);

                switch (frame.Command)
                {
                    case RelpCommand.Open:
                        await SendResponseAsync(stream, frame.TransactionId, openResponse, cancellationToken);
                        break;

                    case RelpCommand.Syslog:
                        receivedPayloads.Enqueue(Encoding.UTF8.GetString(frame.Buffer));
                        await SendResponseAsync(stream, frame.TransactionId, "200 OK", cancellationToken);
                        break;

                    case RelpCommand.Close:
                        await SendResponseAsync(stream, frame.TransactionId, "200 OK", cancellationToken);
                        return;

                    default:
                        await SendResponseAsync(stream, frame.TransactionId, "500 unsupported command", cancellationToken);
                        break;
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task SendResponseAsync(
        NetworkStream stream,
        int transactionId,
        string message,
        CancellationToken cancellationToken)
    {
        var response = RelpFrameTx.FromCommandAndMessage(RelpCommand.Response, Encoding.UTF8.GetBytes(message));
        await stream.WriteAsync(response.ToByteArray(transactionId), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}