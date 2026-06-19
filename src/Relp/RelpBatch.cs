using System.Collections.Concurrent;

namespace Relp;

public sealed class RelpBatch
{
    private readonly RequestId _requestId = new();
    private readonly ConcurrentDictionary<int, RelpFrameTx> _requests = [];
    private readonly ConcurrentDictionary<int, RelpFrameRx> _responses = [];
    private readonly ConcurrentDictionary<int, byte> _workQueue = [];

    public IReadOnlyCollection<int> WorkQueue => _workQueue.Keys.ToArray();

    public int Insert(byte[] syslogMessage) => PutRequest(RelpFrameTx.FromMessage(syslogMessage));

    public int PutRequest(RelpFrameTx request)
    {
        var id = _requestId.Next();
        _requests[id] = request;
        _workQueue[id] = 0;
        return id;
    }

    public RelpFrameTx? GetRequest(int id) => _requests.GetValueOrDefault(id);

    public void RemoveRequest(int id)
    {
        _requests.TryRemove(id, out _);
        _workQueue.TryRemove(id, out _);
    }

    public RelpFrameRx? GetResponse(int id) => _responses.GetValueOrDefault(id);

    public void PutResponse(int id, RelpFrameRx response)
    {
        if (_requests.ContainsKey(id))
        {
            _responses[id] = response;
        }
    }

    public bool VerifyTransaction(int id) =>
        _requests.ContainsKey(id) &&
        _responses.TryGetValue(id, out var response) &&
        response.GetResponseCode() == 200;

    public bool VerifyAllTransactions() => _requests.Keys.All(VerifyTransaction);

    public void RemoveTransaction(int id)
    {
        RemoveRequest(id);
        _responses.TryRemove(id, out _);
    }
}