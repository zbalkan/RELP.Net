using System.Reflection;
using System.Text;

namespace Relp;

/// <summary>Client-side RELP session helper that waits for application-level acknowledgements.</summary>
public sealed class RelpSession
{
    private readonly RelpConnection _connection;
    private readonly TxId _txId = new();
    private readonly RelpWindow _window = new();
    private readonly SemaphoreSlim _transactionLock = new(1, 1);
    private byte[] _receiveRemainder = Array.Empty<byte>();

    /// <summary>Provides a RELP API operation.</summary>
    public RelpSession(RelpConnection connection) => _connection = connection;

    /// <summary>Gets a RELP API value.</summary>
    public bool IsActive { get; private set; }
    /// <summary>Gets a RELP API value.</summary>
    public int PendingAcknowledgements => _window.Size;

    /// <summary>Provides a RELP API operation.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive) throw new InvalidOperationException("Session is already active.");
            var transactionId = _txId.Next();
            var openFrame = RelpFrameTx.FromCommandAndMessage(RelpCommand.Open, CreateOpenOffers());
            var response = await SendFrameAndExpectSuccessfulAckAsync(openFrame, transactionId, cancellationToken).ConfigureAwait(false);
            if (!ResponseIncludesOffer(response, "relp_version"))
            {
                var closeTransactionId = _txId.Next();
                await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromCommand(RelpCommand.Close), closeTransactionId, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("RELP server did not accept the relp_version offer.");
            }

            IsActive = true;
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive) return;
            var transactionId = _txId.Next();
            await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromCommand(RelpCommand.Close), transactionId, cancellationToken).ConfigureAwait(false);
            IsActive = false;
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public async Task SendMessageAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive) throw new InvalidOperationException("Session is not active.");
            var transactionId = _txId.Next();
            await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromMessage(message), transactionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    private async Task<RelpFrameRx> SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx frame, int transactionId, CancellationToken cancellationToken)
    {
        _window.PutPending(transactionId, transactionId);
        try
        {
            await _connection.SendAsync(frame.ToByteArray(transactionId), cancellationToken).ConfigureAwait(false);
            return await ExpectSuccessfulAckAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _window.RemovePending(transactionId);
            throw;
        }
    }

    private async Task<RelpFrameRx> ExpectSuccessfulAckAsync(int transactionId, CancellationToken cancellationToken)
    {
        var parser = new RelpParser();
        if (_receiveRemainder.Length > 0)
        {
            parser.Parse(_receiveRemainder);
            _receiveRemainder = Array.Empty<byte>();
        }

        while (!parser.IsComplete)
        {
            parser.Parse(await _connection.ReceiveAsync(cancellationToken).ConfigureAwait(false));
        }

        _receiveRemainder = parser.RemainingBytes;
        var response = parser.ToFrame();
        if (response.Command != RelpCommand.Response || response.TransactionId != transactionId || response.GetResponseCode() != 200)
        {
            throw new InvalidOperationException($"RELP transaction {transactionId} was not acknowledged successfully.");
        }

        _window.RemovePending(transactionId);
        return response;
    }

    private static byte[] CreateOpenOffers()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
        return Encoding.UTF8.GetBytes($"relp_version=0\nrelp_software=pyrelp-dotnet,{version},https://github.com/zbalkan/pyrelp\ncommands=syslog");
    }

    private static bool ResponseIncludesOffer(RelpFrameRx response, string offerName)
    {
        var data = response.GetData();
        var acceptedOffers = data.Contains('\n') ? data[(data.IndexOf('\n') + 1)..] : string.Empty;
        return acceptedOffers.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(offer => offer.StartsWith(offerName + "=", StringComparison.Ordinal));
    }
}
