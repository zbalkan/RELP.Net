using System.Collections.Concurrent;

namespace Relp;

public sealed class RelpWindow
{
    private readonly ConcurrentDictionary<int, int> _pending = [];

    public void PutPending(int transactionId, int requestId) => _pending[transactionId] = requestId;

    public bool IsPending(int transactionId) => _pending.ContainsKey(transactionId);

    public int? GetPending(int transactionId) => _pending.TryGetValue(transactionId, out var requestId) ? requestId : null;

    public void RemovePending(int transactionId) => _pending.TryRemove(transactionId, out _);

    public int Size => _pending.Count;
}