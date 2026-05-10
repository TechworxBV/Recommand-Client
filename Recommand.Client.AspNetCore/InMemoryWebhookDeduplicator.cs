using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Recommand.Client.AspNetCore;

/// <summary>
/// In-process bounded LRU implementation of <see cref="IWebhookDeduplicator"/>.
/// Suitable for local development and tests; <b>not</b> appropriate for
/// production multi-instance deployments because state isn't shared and is
/// lost on process restart. For production, implement
/// <see cref="IWebhookDeduplicator"/> against a durable, shared store
/// (Redis, Postgres, DynamoDB, …).
/// </summary>
public sealed class InMemoryWebhookDeduplicator : IWebhookDeduplicator
{
    /// <summary>Default in-memory capacity (10,000 keys).</summary>
    public const int DefaultCapacity = 10_000;

    private readonly object _gate = new();
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, LinkedListNode<string>> _index;
    private readonly int _capacity;

    /// <param name="capacity">
    /// Maximum number of keys retained. When exceeded, the oldest is evicted.
    /// Pick a value larger than the maximum number of distinct deliveries you
    /// expect within your retry window (the Recommand API retry policy isn't
    /// formally documented yet — 10,000 is a comfortable default for typical
    /// workloads).
    /// </param>
    public InMemoryWebhookDeduplicator(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _capacity = capacity;
        _index = new Dictionary<string, LinkedListNode<string>>(capacity, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryRegisterAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (idempotencyKey is null) throw new ArgumentNullException(nameof(idempotencyKey));

        lock (_gate)
        {
            if (_index.ContainsKey(idempotencyKey)) return new ValueTask<bool>(false);

            var node = _order.AddLast(idempotencyKey);
            _index[idempotencyKey] = node;

            if (_index.Count > _capacity)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _index.Remove(oldest.Value);
            }
            return new ValueTask<bool>(true);
        }
    }
}
