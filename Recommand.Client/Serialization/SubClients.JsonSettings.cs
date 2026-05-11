using System.Text.Json;

// One partial method per generated sub-client class, each routing the
// JsonSerializerOptions instance through the shared default policy in
// RecommandJsonDefaults.ConfigureCommon. NSwag declares the partial as
// `static partial void UpdateJsonSerializerSettings(JsonSerializerOptions)`
// in each generated client; we implement it once, identically, for all of
// them.
//
// If the spec ever adds a new resource client (tag), append a matching
// partial below. The generator does not auto-discover these.

namespace Recommand.Client;

public partial class AuthenticationClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class CompaniesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class CompanyDocumentTypesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class CompanyIdentifiersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class CompanyNotificationEmailAddressesClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class CustomersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class DocumentsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class LabelsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class PlaygroundsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class RecipientsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class SendingClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class SuppliersClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}

public partial class WebhooksClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        RecommandJsonDefaults.ConfigureCommon(settings);
}
