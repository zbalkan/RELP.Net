using System.Text;

namespace Relp;

/// <summary>A received RELP frame.</summary>
public sealed class RelpFrameRx
{
    private readonly byte[] _buffer;

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameRx(int transactionId, RelpCommand command, int length, byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "RELP payload length must not be negative.");
        }

        if (buffer.Length != length)
        {
            throw new ArgumentException("Buffer length must match RELP payload length.", nameof(buffer));
        }

        TransactionId = transactionId;
        Command = command;
        Length = length;
        _buffer = buffer.ToArray();
    }

    /// <summary>Gets a RELP API value.</summary>
    public int TransactionId { get; }
    /// <summary>Gets a RELP API value.</summary>
    public RelpCommand Command { get; }
    /// <summary>Gets a RELP API value.</summary>
    public int Length { get; }
    /// <summary>Provides a RELP API operation.</summary>
    public byte[] Buffer => _buffer.ToArray();

    /// <summary>Provides a RELP API operation.</summary>
    public int GetResponseCode()
    {
        var text = GetData().Trim();
        var firstToken = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!int.TryParse(firstToken, out var code) || code is < 100 or > 999)
        {
            throw new FormatException("Invalid RELP response code.");
        }
        return code;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public string GetData() => Encoding.UTF8.GetString(_buffer);
}
