namespace Recommand.Client;

/// <summary>
/// The Recommand Peppol API client. Resource groups are exposed as properties;
/// each property returns the typed client interface for that group.
/// </summary>
public interface IRecommandClient
{
    /// <summary>Authentication endpoints (token verification).</summary>
    IAuthenticationClient Authentication { get; }

    /// <summary>Companies — top-level resource for organisations registered with Peppol.</summary>
    ICompaniesClient Companies { get; }

    /// <summary>Document types a company can send and receive.</summary>
    ICompanyDocumentTypesClient CompanyDocumentTypes { get; }

    /// <summary>Peppol identifiers attached to a company.</summary>
    ICompanyIdentifiersClient CompanyIdentifiers { get; }

    /// <summary>Email addresses notified about company-level events.</summary>
    ICompanyNotificationEmailAddressesClient CompanyNotificationEmailAddresses { get; }

    /// <summary>Customers (recipients you have sent to).</summary>
    ICustomersClient Customers { get; }

    /// <summary>Documents — listing, retrieval, download.</summary>
    IDocumentsClient Documents { get; }

    /// <summary>Labels for tagging documents and other resources.</summary>
    ILabelsClient Labels { get; }

    /// <summary>Playgrounds — sandbox environments for testing integrations.</summary>
    IPlaygroundsClient Playgrounds { get; }

    /// <summary>Peppol recipient lookups.</summary>
    IRecipientsClient Recipients { get; }

    /// <summary>Send a Peppol document.</summary>
    ISendingClient Sending { get; }

    /// <summary>Suppliers (parties that send to your companies).</summary>
    ISuppliersClient Suppliers { get; }

    /// <summary>Webhook subscriptions and delivery management.</summary>
    IWebhooksClient Webhooks { get; }
}
