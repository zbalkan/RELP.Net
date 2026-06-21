using System.Text;

namespace Relp;

/// <summary>Incremental parser for octet-counted RELP frames.</summary>
public sealed class RelpParser
{
    private readonly List<byte> _buffer = [];

    /// <summary>Gets a RELP API value.</summary>
    public bool IsComplete { get; private set; }
    /// <summary>Gets a RELP API value.</summary>
    public int TransactionId { get; private set; }
    /// <summary>Gets a RELP API value.</summary>
    public RelpCommand Command { get; private set; }
    /// <summary>Gets a RELP API value.</summary>
    public int Length { get; private set; }
    /// <summary>Provides a RELP API operation.</summary>
    public byte[] Data { get; private set; } = Array.Empty<byte>();
    /// <summary>Provides a RELP API operation.</summary>
    public byte[] RemainingBytes { get; private set; } = Array.Empty<byte>();

    /// <summary>Provides a RELP API operation.</summary>
    public void Parse(byte value) => Parse([value]);

    /// <summary>Provides a RELP API operation.</summary>
    public void Parse(ReadOnlySpan<byte> bytes)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Parser has already completed a RELP frame. Create a new parser for additional frames and pass RemainingBytes first.");
        }

        foreach (var value in bytes)
        {
            _buffer.Add(value);
        }

        TryParseBufferedFrame();
    }

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameRx ToFrame()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("Parser has not completed parsing a RELP frame.");
        }

        return new RelpFrameRx(TransactionId, Command, Length, Data);
    }

    private void TryParseBufferedFrame()
    {
        var firstSpace = IndexOfSpace(0);
        if (firstSpace < 0) return;

        var secondSpace = IndexOfSpace(firstSpace + 1);
        if (secondSpace < 0) return;

        var thirdSpace = IndexOfSpace(secondSpace + 1);
        var newline = IndexOfNewline(secondSpace + 1);
        if (thirdSpace < 0 && newline < 0) return;
        if (newline >= 0 && (thirdSpace < 0 || newline < thirdSpace))
        {
            thirdSpace = -1;
        }

        var transactionText = Encoding.ASCII.GetString(_buffer.GetRange(0, firstSpace).ToArray());
        if (!int.TryParse(transactionText, out var transactionId) || transactionId < TxId.MinValue || transactionId > TxId.MaxValue)
        {
            throw new FormatException($"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        var commandText = Encoding.ASCII.GetString(_buffer.GetRange(firstSpace + 1, secondSpace - firstSpace - 1).ToArray());
        if (!RelpCommandExtensions.TryParseProtocolString(commandText, out var command))
        {
            throw new FormatException("Invalid RELP command.");
        }

        var lengthEnd = thirdSpace < 0 ? newline : thirdSpace;
        var lengthText = Encoding.ASCII.GetString(_buffer.GetRange(secondSpace + 1, lengthEnd - secondSpace - 1).ToArray());
        if (!int.TryParse(lengthText, out var length) || length < 0)
        {
            throw new FormatException("Negative or invalid payload length.");
        }

        if (thirdSpace < 0)
        {
            if (length != 0)
            {
                throw new FormatException("Non-empty RELP frame is missing the payload separator.");
            }

            TransactionId = transactionId;
            Command = command;
            Length = length;
            Data = Array.Empty<byte>();
            RemainingBytes = _buffer.Skip(newline + 1).ToArray();
            IsComplete = true;
            return;
        }

        var dataStart = thirdSpace + 1;
        var frameEnd = dataStart + length;
        if (_buffer.Count <= frameEnd)
        {
            return;
        }

        if (_buffer[frameEnd] != (byte)'\n')
        {
            throw new FormatException("RELP frame is not terminated after the declared payload length.");
        }

        TransactionId = transactionId;
        Command = command;
        Length = length;
        Data = _buffer.GetRange(dataStart, length).ToArray();
        RemainingBytes = _buffer.Skip(frameEnd + 1).ToArray();
        IsComplete = true;
    }

    private int IndexOfSpace(int start) => IndexOfByte((byte)' ', start);

    private int IndexOfNewline(int start) => IndexOfByte((byte)'\n', start);

    private int IndexOfByte(byte value, int start)
    {
        for (var index = start; index < _buffer.Count; index++)
        {
            if (_buffer[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
