# Changelog

All notable changes to `Recommand.Client` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
