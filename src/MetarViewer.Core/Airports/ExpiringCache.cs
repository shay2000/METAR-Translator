using System.Collections.Concurrent;

namespace MetarViewer.Airports;

/// <summary>
/// A cache of values keyed by user input, where each entry is discarded after a fixed period.
///
/// The lookup service held two of these by hand, each with its own dictionary, its own
/// cache-entry record, and its own near-identical pair of read and write methods. One generic
/// cache removes that duplication and makes the expiry testable through
/// <see cref="TimeProvider"/>.
/// </summary>
/// <typeparam name="TValue">The cached value. Nulls may be stored, so that "the API had no
/// answer for this input" is remembered too and is not asked again on every keystroke.</typeparam>
internal sealed class ExpiringCache<TValue>
    where TValue : class
{
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _timeProvider;

    // Keyed case-insensitively so that "heathrow" reuses the entry stored for "Heathrow".
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpiringCache{TValue}"/> class.
    /// </summary>
    /// <param name="lifetime">How long an entry remains usable after it is stored.</param>
    /// <param name="timeProvider">The clock used for expiry. Defaults to the system clock.</param>
    public ExpiringCache(TimeSpan lifetime, TimeProvider? timeProvider = null)
    {
        _lifetime = lifetime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns whether a usable entry exists for the key, dropping it if it has expired.
    /// </summary>
    public bool TryGet(string key, out TValue? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > _timeProvider.GetUtcNow())
            {
                value = entry.Value;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Stores a value against the key, replacing any existing entry.
    /// </summary>
    public void Set(string key, TValue? value)
    {
        _entries[key] = new Entry(value, _timeProvider.GetUtcNow().Add(_lifetime));
    }

    private sealed record Entry(TValue? Value, DateTimeOffset ExpiresAt);
}
