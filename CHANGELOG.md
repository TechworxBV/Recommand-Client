# Changelog

All notable changes to `Recommand.Client` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.2] – 2026-05-11

### Fixed

- **Enum values that start with a digit now serialise correctly.** The
  Peppol scheme codes (`"0208"`, `"0002"`, …), country codes, and similar
  enum values were being emitted on the wire as `"_0208"` etc. — the C#
  enum member name (which must be underscore-prefixed because C#
  identifiers can't start with a digit) was leaking through because
  `System.Text.Json.Serialization.JsonStringEnumConverter` does not
  honour `[EnumMember(Value = "…")]`. The SDK now ships
  `EnumMemberStringEnumConverter<TEnum>`, a drop-in replacement that
  respects `EnumMember` on both serialise and deserialise. The generator
  post-processes the NSwag output to swap the converter type on every
  property attribute (25 sites in the current spec). Affected types
  include `EnterpriseNumberScheme`, `ItemClassificationCodeScheme`, and
  others.

## [0.4.1] – 2026-05-11

### Fixed

- **Request bodies no longer emit `"foo": null` for nullable optional
  properties the caller left unset.** Previously, every nullable property
  on every DTO appeared on the wire as `"prop": null` regardless of
  whether the caller had assigned it — a side-effect of NSwag generating
  `JsonSerializerOptions` with stock defaults and `System.Text.Json`'s
  default `DefaultIgnoreCondition` being `Never`. Some endpoints
  distinguish present-and-null ("clear this field") from absent ("leave
  default"), so the noisy serialization occasionally triggered subtle
  server-side rejections in addition to bloating logs and payloads. Each
  generated resource client now implements its
  `UpdateJsonSerializerSettings` partial method via a shared
  `RecommandJsonDefaults.ConfigureCommon`, which sets
  `JsonIgnoreCondition.WhenWritingNull` once per client. Deserialization
  is unchanged.

### Notes for consumers

- **`SendDocumentRequest.Recipient` is required-yet-nullable** in the spec
  (typed `string?`, `required: [..., recipient, ...]`). `null` is a
  meaningful value there — it means "send via email only, no Peppol
  recipient." With the new ignore policy, callers must **explicitly**
  assign `Recipient` on every request, either to a Peppol address or to
  `null`. A `SendInvoiceRequest { ... }` that leaves `Recipient` at its
  default `null` will now omit the field from the wire and be rejected
  by the server. This is the one place the new policy can bite an
  unsuspecting caller.

## [0.4.0] – 2026-05-10

### Added

- **Webhook delivery support — typed polymorphism, signature verification,
  ASP.NET Core integration.** The spec now defines the full webhook
  delivery contract under OpenAPI 3.1 `webhooks:`, with a `WebhookPayload`
  parent and 5 variants (`document.received`, `document.sent`,
  `document.label.assigned`, `document.label.unassigned`,
  `company.verification`). The SDK exposes:
  - **Generated polymorphism hierarchy** — `WebhookPayload` base class
    with 5 typed subclasses, `JsonInheritanceConverter` dispatch on the
    `eventType` discriminator. Pattern-match on the runtime type for
    typed handling.
  - **`WebhookPayload.Parse(string)` / `ParseAsync(Stream)`** —
    forward-compatible: unknown event types arrive as the base
    `WebhookPayload` rather than throwing, with all wire fields preserved
    in `AdditionalProperties` (including the wire `eventType` accessible
    via `WebhookPayload.EventType`). Known discriminator set is
    auto-discovered via reflection on the generated
    `JsonInheritanceAttribute`s.
  - **`WebhookEventTypes`** — `const string` constants for the 5 known
    event types, suitable for `switch` labels and string comparisons.
  - **`WebhookSignature.Verify(byte[] body, string? header, string secret)`**
    and **`WebhookSignature.Compute(byte[] body, string secret)`** —
    HMAC-SHA256 with the `sha256=<hex>` GitHub-style format the spec
    documents on the `X-Signature` header. Constant-time comparison via
    `CryptographicOperations.FixedTimeEquals`.
- **New companion package: `Recommand.Client.AspNetCore`.** Targets
  `net6.0;net8.0`, framework-references `Microsoft.AspNetCore.App`.
  Provides:
  - **`MapRecommandWebhook(pattern, handler, options?)`** — endpoint
    extension that, per delivery, reads the raw body (capped by
    `MaxBodyBytes`, default 1 MiB), verifies the HMAC-SHA256 signature
    when a secret is configured, deduplicates on `X-Idempotency-Key` via
    an optional `IWebhookDeduplicator`, parses the body via the
    forward-compatible `WebhookPayload.Parse`, and dispatches to a
    strongly-typed handler delegate. Status codes: `401` for signature
    mismatch, `400` for malformed JSON, `413` for oversize body, `200`
    on handler success.
  - **`AddRecommandWebhooks(o => …)`** — DI registration of options
    (signing secret, body-size limit, strict-vs-lenient signature
    requirement). Endpoints pick up options inline or from DI.
  - **`IWebhookDeduplicator`** — atomic test-and-set interface for
    short-circuiting replays. Implementations expected to be backed by
    a durable, shared store (Redis, Postgres, …).
  - **`InMemoryWebhookDeduplicator`** — bounded LRU implementation for
    development and tests. Not for production multi-instance use.
  - **`WebhookDelivery`** — record carrying the parsed payload, the
    idempotency key, and the underlying `HttpContext` for advanced
    scenarios (custom logging, scoped service resolution).

### Changed

- **Spec source switched to in-repo file.** The generator no longer
  fetches `https://peppol.recommand.eu/openapi`; it reads
  `spec/openapi.json` directly. This makes regeneration deterministic
  (no live-network dependency) and lets the spec evolve in lockstep
  with the SDK in version control.
- **`OneOfDiscriminatorNormalizer`** — new generic rewriter for the
  modern OpenAPI 3.1 polymorphism shape (`oneOf: [refs] + discriminator`).
  Walks every definition; when found, computes the property
  intersection across variants and rewrites to the `allOf` inheritance
  form NSwag emits as `JsonInheritanceConverter` dispatch. Mirrors
  `SiblingDiscriminatorPolymorphismNormalizer` for the other shape.
  Handles the new `WebhookPayload` site automatically.
- **`StructuralDeduplicator` const-aware fingerprint.** JSON Schema
  2020-12 `const` values now participate in structural identity (read
  from `ExtensionData["const"]` since NJsonSchema 11.6 doesn't surface
  it as a first-class property). Without this fix, schemas differing
  only by a discriminator's `const` value fingerprinted identically and
  got incorrectly merged by the dedup pass.

### Notes for consumers

- The wire format is unchanged. All renames are C#-side only; JSON
  serialization/deserialization round-trips identically.
- Webhook subscription management endpoints (`IWebhooksClient`) and
  webhook delivery types (`WebhookPayload` and subclasses) are now both
  available out of the typed client.
- Signature verification requires a shared secret. The
  `POST /v1/webhooks` endpoint does not currently return a `signingSecret`
  on creation — that's an open spec question upstream. Until resolved,
  configure secrets out of band. Use the new `Recommand.Client.AspNetCore`
  endpoint extension to verify signatures end-to-end once you have one.

## [0.3.1] – not released

Internal iteration; folded into 0.4.0.

## [0.3.0] – 2026-05-10

### Added

- **`PrimitiveUnionNormalizer`** — collapses `anyOf [string, string-with-const, null]`
  unions to plain `string?` (preserving `format`). Eliminates the empty-placeholder
  classes NJsonSchema otherwise produces for properties like `email` modeled as
  "string OR const-empty-string OR null."
- **`StructuralDeduplicator`** — generic structural dedup pass over the OpenAPI
  document, with three modes:
  - Same-stem: collapses `Foo`/`Foo2`/`Foo3` collisions into the canonical name.
  - Word-suffix canonical naming: when ≥2 structurally-identical definitions share
    a PascalCase-token suffix (e.g. `GetDocumentResponseDocumentValidation` and
    `GetDocumentsResponseDocumentValidation`), renames to the suffix
    (`DocumentValidation`) and redirects refs.
  - Inline-body rules: collapses repeated inline operation-body schemas (notably
    the validation-error envelope) into a single shared definition.
  Polymorphism variants are excluded from dedup so `JsonInheritanceConverter`
  dispatch stays intact.
- **`PascalCaseEnumNameGenerator`** — converts snake_case JSON enum values to
  PascalCase C# member names (`when_no_pdf_attachment` → `WhenNoPdfAttachment`).
  Wire format unchanged; preserved via `[EnumMember(Value = ...)]`.
- **`NullableReferenceNormalizer`** — generalized from a Vat-specific shim to a
  document-wide pass collapsing `anyOf [X, null]` and `oneOf [X, null]` unions
  to plain `$ref: X`.
- **Generic sibling-discriminator polymorphism rewrite.**
  `SiblingDiscriminatorPolymorphismNormalizer` is now driven by per-site config
  (`ParentSchemaName`, `DiscriminatorPropertyName`, `PolymorphicPropertyName`,
  `VariantNameFor`, optional `RefNameForEnum`). Two new sites configured:
  `GetDocumentResponseDocument.parsed` and `GetDocumentsResponseDocument.parsed`
  (both with loose enum-to-ref matching). The Send-document site uses the same
  mechanism with strict positional matching.
- **`InlineSchemaHoister` extended** to also extract:
  - Array-items inline objects (e.g. `documents: [{...}]` → `Document` def with
    naive English singularization).
  - Inline string/integer enums on object properties.
  - Inline enums on operation parameters (path/query/header).
- **Three typed polymorphism hierarchies** in the public surface:
  `SendDocumentRequest` (6 variants), `GetDocumentResponseDocument` (6 variants),
  `GetDocumentsResponseDocument` (6 variants). Variants for unmatched enum values
  (`messageLevelResponse`, `xml` on the Get sites) are emitted as no-payload
  subclasses so the discriminator dispatch is total.

### Changed

- **Public-surface naming.** ~63 fewer types in the generated client (242 → 179).
  Many duplicated cousin types collapsed into shared canonicals:
  - `InvoiceCurrency`/`CreditNoteCurrency`/`SelfBillingInvoiceCurrency`/… → `Currency`
  - `VATCategory`/`VATSubtotalCategory` → `Category`
  - `Get{Document,Documents,Inbox}ResponseDocumentDirection` → `Direction`
  - Per-operation `*Validation` / `*Label` / `*ValidationError` types
    consolidated where structurally identical
  - `Response2`–`Response106` (107 inline error envelopes) → single
    `ValidationErrorResponse`
- **Pipeline order:** polymorphism rewrite now runs *after* the inline-schema
  hoister so it can resolve hoister-produced parents (`GetDocumentResponseDocument`,
  `GetDocumentsResponseDocument`). The Send-document parent is still self-promoted
  via the in-normalizer body-promotion helper.
- **Polymorphism payload nullability:** when the polymorphic property's original
  union includes a `null` branch, variant payload properties are no longer marked
  `Required` (the API can legitimately return `parsed: null` even on typed
  variants).
- Numbered fallback class names (`Email2`, `Email3`, `Parsed2`, `Vat2`–`Vat4`,
  `Labels2`–`Labels7`, `InvoiceReferences2`–`4`, `Documents2`, `Validation2`,
  `Errors2`–`3`, `Response2`–`Response106`, `Type2`) — **all gone**. Generated
  client has zero numbered class names.

### Removed

- `Recommand.Generator/Normalizers/SendDocumentPolymorphismNormalizer.cs`
  (superseded by the generic `SiblingDiscriminatorPolymorphismNormalizer` with
  per-site config).

### Notes for consumers

- This release significantly reshapes the public surface. Many type names have
  changed (renames, consolidations, new polymorphism hierarchies). Treat as
  breaking; pin the version, regenerate any local references.
- The wire format is unchanged. All renames are C#-side only; JSON
  serialization/deserialization round-trips identically.

## [0.2.2] and earlier

Pre-CHANGELOG releases. See [git history][history] for prior changes.

[history]: https://github.com/TechworxBV/recommand-client/commits/main
