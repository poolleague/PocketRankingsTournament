using System.Collections.Concurrent;

namespace PocketRankingsTournament.Services;

public sealed class LiveLinkRevealStore
{
    private readonly ConcurrentDictionary<Guid, (string Code, DateTimeOffset ExpiresAt)> _reveals = new();

    // Holds the usable code in process memory only for the immediate redirect, never in a browser cookie or durable store.
    public Guid Hold(string code)
    {
        RemoveExpired();
        var id = Guid.NewGuid();
        _reveals[id] = (code, DateTimeOffset.UtcNow.AddMinutes(5));
        return id;
    }

    // Removes a reveal on first read and refuses stale entries so refresh cannot redisplay a venue address.
    public string? Take(Guid id)
    {
        if (!_reveals.TryRemove(id, out var reveal) || reveal.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        return reveal.Code;
    }

    // Opportunistic cleanup keeps abandoned redirects from accumulating in the single-instance process.
    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _reveals.Where(item => item.Value.ExpiresAt <= now))
        {
            _reveals.TryRemove(entry.Key, out _);
        }
    }
}
