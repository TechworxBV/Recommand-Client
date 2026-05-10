using Microsoft.AspNetCore.Http;

namespace Recommand.Client.AspNetCore;

/// <summary>
/// Context passed to a webhook handler delegate. Carries the parsed
/// (polymorphic) payload, the delivery's idempotency key, and the underlying
/// <see cref="HttpContext"/> for advanced scenarios (custom headers, logging,
/// scoped services).
/// </summary>
/// <param name="Payload">
/// The parsed webhook event. Pattern-match on the runtime type
/// (<see cref="DocumentReceivedWebhook"/>, <see cref="CompanyVerificationWebhook"/>,
/// etc.) for typed handling. For event types this SDK version doesn't
/// recognise, the runtime type is the base <see cref="WebhookPayload"/>;
/// <see cref="WebhookPayload.EventType"/> still surfaces the wire identifier.
/// </param>
/// <param name="IdempotencyKey">
/// Value of the <c>X-Idempotency-Key</c> header. Identical across retries of
/// the same logical event; use it to make handlers naturally idempotent.
/// Always present (the spec marks the header required); the empty string is
/// returned only if a malformed delivery omitted it.
/// </param>
/// <param name="Http">
/// The underlying HTTP context. Use for custom headers, logging, or scoped
/// service resolution.
/// </param>
public readonly record struct WebhookDelivery(
    WebhookPayload Payload,
    string IdempotencyKey,
    HttpContext Http);
