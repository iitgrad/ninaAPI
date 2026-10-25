// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections;

namespace StarfrontControl.Core;

/// <summary>
/// Retains the most recent items. Enumeration copies references under a short lock;
/// callers never enumerate the mutable ring or hold its lock while serializing.
/// Capacity bounds item count, not the size of individual payloads.
/// </summary>
public sealed class BoundedHistory<T> : IEnumerable<T>
{
    private readonly object gate = new();
    private readonly T?[] items;
    private int start;
    private int count;
    private long evictedCount;

    public BoundedHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        items = new T?[capacity];
    }

    public int Capacity => items.Length;
    public int Count { get { lock (gate) return count; } }
    public long EvictedCount { get { lock (gate) return evictedCount; } }

    public void Add(T item)
    {
        lock (gate)
        {
            if (count == items.Length)
            {
                items[start] = item;
                start = (start + 1) % items.Length;
                evictedCount++;
            }
            else
            {
                items[(start + count) % items.Length] = item;
                count++;
            }
        }
    }

    public T[] Snapshot()
    {
        lock (gate)
        {
            var snapshot = new T[count];
            for (var index = 0; index < count; index++)
                snapshot[index] = items[(start + index) % items.Length]!;
            return snapshot;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            Array.Clear(items);
            start = 0;
            count = 0;
            // Keep the lifetime eviction counter for diagnostics.
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Snapshot()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
