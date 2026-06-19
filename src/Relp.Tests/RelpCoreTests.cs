using System.Collections.Concurrent;
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
        Assert.AreEqual("0 syslog 12 Hello World!", frame.ToProtocolString());
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
}