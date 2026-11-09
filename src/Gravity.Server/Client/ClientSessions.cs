using System.Collections.Concurrent;
using Gravity.Shared;

namespace Gravity.Client;

/// <summary>클라 플러그인이 인사(hello)한 세션 기록. 서버 재시작 전까지 메모리에만 둔다.</summary>
public sealed class ClientSessions
{
    private readonly ConcurrentDictionary<string, HelloRequest> _hello = new(StringComparer.Ordinal);

    public string? LastVersion { get; private set; }
    public bool LastAbps { get; private set; }
    public DateTime? LastHelloAt { get; private set; }

    public void Hello(string sessionId, HelloRequest r)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _hello[sessionId] = r;
        LastVersion = r.ClientVersion;
        LastAbps = r.AbpsClient;
        LastHelloAt = DateTime.Now;
    }

    /// <summary>판이 맞는 클라가 인사한 세션인가. 판이 다르면 클라가 스스로 꺼지므로 서버도 클라 몫을 비우지 않는다.</summary>
    public bool HasClient(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _hello.TryGetValue(sessionId, out var r)
        && GravityVersion.Compatible(r.ClientVersion, GravityVersion.Current);

    public bool AbpsClient(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _hello.TryGetValue(sessionId, out var r) && r.AbpsClient;
}
