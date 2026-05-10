using System.Threading;
using System.Threading.Tasks;

namespace Recommand.Client.AspNetCore;

/// <summary>
/// Records observed webhook delivery idempotency keys so replays can be
/// short-circuited without re-running the handler.
/// </summary>
/// <remarks>
/// <para>
/// The Recommand API includes an <c>X-Idempotency-Key</c> header on every
/// delivery; the same key is used across retries of the same logical event,
/// so consumers should treat first-write-wins semantics on this key as the
/// boundary of "already processed."
/// </para>
/// <para>
/// Implement this interface against whatever durable store your app uses
/// (Redis with <c>SETNX</c>, a Postgres table with a unique constraint,
/// DynamoDB conditional write, …). The shipped <see cref="InMemoryWebhookDeduplicator"/>
/// is intended for local development and tests only — it doesn't survive
/// process restarts and doesn't share state across instances.
/// </para>
/// </remarks>
public interface IWebhookDeduplicator
{
    /// <summary>
    /// Atomically records that a delivery with this idempotency key has been
    /// observed.
    /// </summary>
    /// <param name="idempotencyKey">
    /// The value of the <c>X-Idempotency-Key</c> header on the webhook
    /// delivery. Treat as opaque; do not parse.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if this is the first time the key has been seen — the
    /// caller should process the delivery. <c>false</c> if the key was
    /// previously registered — the caller should ack with <c>200 OK</c>
    /// without re-running the handler.
    /// </returns>
    /// <remarks>
    /// Implementations <b>must</b> be atomic: two concurrent calls with the
    /// same key must result in exactly one returning <c>true</c>. Otherwise
    /// you'll double-process under retry pressure.
    /// </remarks>
    ValueTask<bool> TryRegisterAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
