using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Relp;

/// <summary>Provides a Microsoft.Extensions.Logging sink that forwards log events over RELP.</summary>
public sealed class RelpLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly Channel<RelpLogEntry> _channel;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    /// <summary>Initializes a new RELP logging provider.</summary>
    public RelpLoggerProvider(RelpLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
        _channel = Channel.CreateBounded<RelpLogEntry>(new BoundedChannelOptions(options.QueueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    internal RelpLoggerOptions Options { get; }

    internal IExternalScopeProvider ScopeProvider => _scopeProvider;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RelpLogger(categoryName, this);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _stopping.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Logging providers must not throw during application shutdown.
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    internal bool IsEnabled(LogLevel logLevel) => !_disposed && logLevel != LogLevel.None && logLevel >= Options.MinimumLevel;

    internal void Enqueue(RelpLogEntry entry)
    {
        if (!_disposed)
        {
            _channel.Writer.TryWrite(entry);
        }
    }

    private async Task ProcessQueueAsync()
    {
        RelpConnection? connection = null;
        RelpSession? session = null;

        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                try
                {
                    if (session is null || !session.IsActive)
                    {
                        connection = new RelpConnection(Options.Host, Options.Port, Options.UseTls, Options.ClientCertificates);
                        session = new RelpSession(connection);
                        await connection.ConnectAsync(_stopping.Token).ConfigureAwait(false);
                        await session.OpenAsync(_stopping.Token).ConfigureAwait(false);
                    }

                    await session.SendMessageAsync(Options.Formatter(entry), _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (connection is not null)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }

                    connection = null;
                    session = null;
                }
            }
        }
        finally
        {
            if (session?.IsActive == true)
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
