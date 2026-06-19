using System.Text;

namespace Relp;

/// <summary>A RELP frame ready for transmission.</summary>
public sealed class RelpFrameTx
{
    public RelpFrameTx(RelpCommand command, byte[]? message = null)
    {
        Command = command;
        Message = message ?? Array.Empty<byte>();
    }

    public RelpCommand Command { get; }
    public byte[] Message { get; }

    public static RelpFrameTx FromMessage(byte[] message) => new(RelpCommand.Syslog, message);

    public static RelpFrameTx FromCommand(RelpCommand command) => new(command);

    public static RelpFrameTx FromCommandAndMessage(RelpCommand command, byte[] message) => new(command, message);

    public byte[] ToByteArray(int transactionId = 0)
    {
        if (transactionId is < 0 or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between 0 and {TxId.MaxValue}.");
        }

        var header = Encoding.ASCII.GetBytes($"{transactionId} {Command.ToProtocolString()} {Message.Length}");
        if (Message.Length == 0)
        {
            return [.. header, (byte)'\n'];
        }

        return [.. header, (byte)' ', .. Message, (byte)'\n'];
    }

    public string ToProtocolString(int transactionId = 0)
    {
        var frame = ToByteArray(transactionId);
        return Encoding.UTF8.GetString(frame, 0, frame.Length - 1);
    }
}