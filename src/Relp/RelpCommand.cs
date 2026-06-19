namespace Relp;

/// <summary>RELP protocol command names.</summary>
public enum RelpCommand
{
    Open,
    Close,
    Abort,
    ServerClose,
    Syslog,
    Response
}

public static class RelpCommandExtensions
{
    public static string ToProtocolString(this RelpCommand command) => command switch {
        RelpCommand.Open => "open",
        RelpCommand.Close => "close",
        RelpCommand.Abort => "abort",
        RelpCommand.ServerClose => "serverclose",
        RelpCommand.Syslog => "syslog",
        RelpCommand.Response => "rsp",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    public static bool TryParseProtocolString(string value, out RelpCommand command)
    {
        command = value switch {
            "open" => RelpCommand.Open,
            "close" => RelpCommand.Close,
            "abort" => RelpCommand.Abort,
            "serverclose" => RelpCommand.ServerClose,
            "syslog" => RelpCommand.Syslog,
            "rsp" => RelpCommand.Response,
            _ => default
        };
        return value is "open" or "close" or "abort" or "serverclose" or "syslog" or "rsp";
    }
}