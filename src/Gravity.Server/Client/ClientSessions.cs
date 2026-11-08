using System.Collections.Concurrent;
using Gravity.Shared;

namespace Gravity.Client;

/// <summary>클라 플러그인이 인사(hello)한 세션 기록. 서버 재시작 전까지 메모리에만 둔다.</summary>
public sealed class ClientSessions
{
    private readonly ConcurrentDictionary<string, HelloRequest> _hello = new(StringComparer.Ordinal);

    public string? LastVersion { get; private set; }

    public void Hello(string sessionId, HelloRequest r)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _hello[sessionId] = r;
        LastVersion = r.ClientVersion;
    }

    public bool HasClient(string? sessionId) => !string.IsNullOrEmpty(sessionId) && _hello.ContainsKey(sessionId);

    public bool AbpsClient(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _hello.TryGetValue(sessionId, out var r) && r.AbpsClient;
}
