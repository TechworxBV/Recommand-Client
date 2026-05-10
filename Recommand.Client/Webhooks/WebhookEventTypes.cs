namespace Recommand.Client.Webhooks;

/// <summary>
/// Wire-format identifiers for the webhook event types this SDK version
/// recognises. The same values appear as <c>JsonInheritanceAttribute</c> keys
/// on the generated <see cref="WebhookPayload"/> subclasses.
/// </summary>
/// <remarks>
/// For typed dispatch, prefer pattern matching on the concrete subclass
/// (<c>DocumentReceivedWebhook</c>, <c>CompanyVerificationWebhook</c>, etc.).
/// These constants are useful for:
/// <list type="bullet">
///   <item>Structured logging and metrics tagging by event type.</item>
///   <item>Comparing against <see cref="WebhookPayload.EventType"/> when
///         working with the base type.</item>
///   <item>Filtering deliveries by event-type string before parsing the body.</item>
/// </list>
/// </remarks>
public static class WebhookEventTypes
{
    /// <summary>
    /// An inbound Peppol document was received and stored for one of your
    /// companies. Maps to <see cref="DocumentReceivedWebhook"/>.
    /// </summary>
    public const string DocumentReceived = "document.received";

    /// <summary>
    /// An outbound document was sent. Maps to <see cref="DocumentSentWebhook"/>.
    /// </summary>
    public const string DocumentSent = "document.sent";

    /// <summary>
    /// A label was attached to a document. Maps to
    /// <see cref="DocumentLabelAssignedWebhook"/>.
    /// </summary>
    public const string DocumentLabelAssigned = "document.label.assigned";

    /// <summary>
    /// A label was removed from a document. Maps to
    /// <see cref="DocumentLabelUnassignedWebhook"/>.
    /// </summary>
    public const string DocumentLabelUnassigned = "document.label.unassigned";

    /// <summary>
    /// A company verification finished (verified, rejected, or errored).
    /// Maps to <see cref="CompanyVerificationWebhook"/>.
    /// </summary>
    public const string CompanyVerification = "company.verification";
}
