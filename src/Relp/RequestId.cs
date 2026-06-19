namespace Relp;

public sealed class RequestId
{
    private int _requestIdentifier;

    public int Next() => Interlocked.Increment(ref _requestIdentifier) - 1;

    [Obsolete("Use Next instead.")]
    public int GetNextId() => Next();
}