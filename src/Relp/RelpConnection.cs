using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Relp;

/// <summary>TCP/TLS transport for RELP clients.</summary>
public sealed class RelpConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private TcpClient? _client;
    private Stream? _stream;
    private bool _disposed;

    /// <summary>Provides a RELP API operation.</summary>
    public RelpConnection(string host, int port, bool useTls = false, X509CertificateCollection? clientCertificates = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("RELP host must not be empty.", nameof(host));
        }

        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "RELP port must be between 1 and 65535.");
        }

        Host = host;
        Port = port;
        UseTls = useTls;
        ClientCertificates = clientCertificates;
    }

    /// <summary>Gets a RELP API value.</summary>
    public string Host { get; }

    /// <summary>Gets a RELP API value.</summary>
    public int Port { get; }

    /// <summary>Gets a RELP API value.</summary>
    public bool UseTls { get; }

    /// <summary>Gets a RELP API value.</summary>
    public X509CertificateCollection? ClientCertificates { get; }

    /// <summary>Provides a RELP API operation.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is not null)
            {
                throw new InvalidOperationException("Connection is already open.");
            }
            ObjectDisposedException.ThrowIf(_disposed, this);

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(Host, Port, cancellationToken).ConfigureAwait(false);
                Stream stream = client.GetStream();

                if (UseTls)
                {
                    var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                        TargetHost = Host,
                        ClientCertificates = ClientCertificates
                    }, cancellationToken).ConfigureAwait(false);
                    stream = ssl;
                }

                _client = client;
                _stream = stream;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public async Task SendAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var stream = _stream ?? throw new InvalidOperationException("Connection is not open.");
            await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var stream = _stream ?? throw new InvalidOperationException("Connection is not open.");
            var buffer = new byte[4096];
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new IOException("Connection closed by the server.");
            return buffer[..count];
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public async ValueTask DisposeAsync()
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        Stream? stream;
        TcpClient? client;
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stream = _stream;
            client = _client;
            _stream = null;
            _client = null;
        }
        finally
        {
            _connectLock.Release();
        }

        // Disposing the stream outside the connection lock lets a blocked read or
        // write unblock without DisposeAsync waiting forever on the send/receive
        // semaphores held by those operations.
        if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
        client?.Dispose();
    }
}