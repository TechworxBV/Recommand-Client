using System.Security.Cryptography;
using System.Text;
using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Generic structural dedup over the entire OpenAPI document.
///
/// Two effects, same mechanism:
///
/// 1. Same-stem Definitions collisions. When two Definitions are
///    structurally identical and their names share a single stem
///    (after stripping trailing digits — e.g. <c>Vat</c> /
///    <c>Vat2</c> / <c>Vat3</c>), every loser is removed and every
///    <c>$ref</c> pointing at it is redirected to the canonical entry.
///    Cleans up name collisions emitted by <see cref="InlineSchemaHoister"/>
///    when the same property name is hoisted from multiple containers.
///    Stem heuristic prevents accidentally merging unrelated types that
///    happen to share a shape (<c>InvoiceLabels</c> vs <c>OrderLabels</c>).
///
/// 2. Inline operation body dedup. When N >= 2 inline (non-<c>$ref</c>)
///    operation request/response bodies share a structural fingerprint,
///    a single canonical Definition is created and every site is
///    rewritten to <c>$ref</c> it. The canonical name comes from a
///    user-supplied <see cref="NamingRule"/> — there's no safe way to
///    invent one (e.g. 100+ inline error envelopes titled
///    <c>{OpId}Response{statusCode}</c> have no clean shared name).
/// </summary>
internal sealed class StructuralDeduplicator : ISpecNormalizer
{
    private readonly IReadOnlyList<NamingRule> _rules;

    public StructuralDeduplicator(params NamingRule[] rules)
    {
        _rules = rules;
    }

    public void Normalize(OpenApiDocument document)
    {
        var defsRemoved = 0;
        var refsRedirected = 0;
        var defsHoisted = 0;
        var inlineRewrites = 0;

        // Polymorphism variants (allOf:[parent-with-discriminator, ...]) MUST
        // remain distinct C# types even if structurally identical, because
        // JsonInheritanceConverter dispatches by type identity. The two
        // no-payload variants for an unmatched enum value would otherwise
        // collide and destroy the discriminator dispatch.
        bool IsPolymorphismVariant(JsonSchema schema)
        {
            foreach (var member in schema.AllOf)
            {
                var target = member.HasReference ? member.Reference : member;
                if (target?.DiscriminatorObject is not null) return true;
            }
            return false;
        }

        // Pass 1: collapse same-stem Definitions collisions.
        var defGroups = document.Definitions
            .Where(kv => !IsPolymorphismVariant(kv.Value))
            .GroupBy(kv => Fingerprint(kv.Value))
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in defGroups)
        {
            var entries = group.ToList();
            var stems = entries
                .Select(kv => StripTrailingDigits(kv.Key))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (stems.Count != 1) continue;

            var canonicalStem = stems[0];
            var winner = entries.FirstOrDefault(kv => kv.Key == canonicalStem);
            if (winner.Key is null) winner = entries.OrderBy(kv => kv.Key.Length).First();

            foreach (var loser in entries)
            {
                if (loser.Key == winner.Key) continue;
                refsRedirected += RedirectReferences(document, loser.Value, winner.Value);
                document.Definitions.Remove(loser.Key);
                defsRemoved++;
            }
        }

        // Pass 1b: word-suffix canonical-name dedup.
        // For groups of structurally-identical Definitions whose names share a
        // PascalCase-token suffix (e.g. GetDocumentResponseDocumentValidation
        // and GetDocumentsResponseDocumentValidation both end in
        // [Document, Validation]), rename one to the suffix-derived canonical
        // (DocumentValidation) and redirect refs from the others. This is the
        // generalization of pass 1 — same-stem is the special case where the
        // entire name is the suffix.
        var renamesViaSuffix = 0;
        var redirectsViaSuffix = 0;
        var suffixGroups = document.Definitions
            .Where(kv => !IsPolymorphismVariant(kv.Value))
            .GroupBy(kv => Fingerprint(kv.Value))
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in suffixGroups)
        {
            var entries = group.ToList();
            var canonical = LongestCommonWordSuffix(entries.Select(kv => kv.Key));
            if (string.IsNullOrEmpty(canonical)) continue;

            // Don't clobber a structurally-different existing definition.
            if (document.Definitions.TryGetValue(canonical, out var existing)
                && !ReferenceEquals(existing, entries[0].Value)
                && Fingerprint(existing) != group.Key)
                continue;

            // If one entry already has the canonical name, it wins as-is.
            // Otherwise pick the first and rename it.
            var winnerKv = entries.FirstOrDefault(kv => kv.Key == canonical);
            JsonSchema winnerSchema;
            if (winnerKv.Key is not null)
            {
                winnerSchema = winnerKv.Value;
            }
            else
            {
                var firstEntry = entries[0];
                document.Definitions.Remove(firstEntry.Key);
                firstEntry.Value.Title = canonical;
                document.Definitions[canonical] = firstEntry.Value;
                winnerSchema = firstEntry.Value;
                renamesViaSuffix++;
            }

            foreach (var loser in entries)
            {
                if (ReferenceEquals(loser.Value, winnerSchema)) continue;
                redirectsViaSuffix += RedirectReferences(document, loser.Value, winnerSchema);
                document.Definitions.Remove(loser.Key);
                defsRemoved++;
            }
        }

        // Pass 2: collapse inline operation bodies that share a structural fingerprint.
        var inlineSites = new List<InlineSite>();
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, operation) in pathItem.ActualPathItem)
            {
                if (operation.RequestBody?.Content.TryGetValue("application/json", out var rc) == true
                    && rc.Schema is { Reference: null } reqSchema)
                {
                    var localRc = rc;
                    inlineSites.Add(new InlineSite(reqSchema, s => localRc.Schema = s));
                }
                foreach (var (_, response) in operation.Responses)
                {
                    if (response.Content.TryGetValue("application/json", out var sc)
                        && sc.Schema is { Reference: null } respSchema)
                    {
                        var localSc = sc;
                        inlineSites.Add(new InlineSite(respSchema, s => localSc.Schema = s));
                    }
                }
            }
        }

        var inlineGroups = inlineSites
            .GroupBy(s => Fingerprint(s.Schema))
            .Where(g => g.Count() >= 2);

        foreach (var group in inlineGroups)
        {
            var sites = group.ToList();
            var canonical = sites[0].Schema;
            var name = ChooseName(canonical);
            if (name is null) continue;
            if (document.Definitions.ContainsKey(name)) continue;

            canonical.Title = name;
            document.Definitions[name] = canonical;
            defsHoisted++;

            foreach (var site in sites)
            {
                site.Replace(new JsonSchema { Reference = canonical });
                inlineRewrites++;
            }
        }

        Console.WriteLine(
            $"Structural deduplicator: removed {defsRemoved} duplicate definitions " +
            $"({refsRedirected + redirectsViaSuffix} refs redirected, {renamesViaSuffix} renames via word-suffix); " +
            $"hoisted {defsHoisted} shared definitions from {inlineRewrites} inline body sites.");
    }

    private string? ChooseName(JsonSchema schema)
    {
        foreach (var rule in _rules)
            if (rule.Match(schema)) return rule.Name;
        return null;
    }

    // ---------- structural fingerprint ----------
    //
    // We can't use JsonSchema.ToJson() because it tries to resolve $ref paths
    // across the whole document and throws when we're holding refs to schemas
    // that haven't been written into Definitions yet (e.g. the polymorphism
    // normalizer's allOf references). Instead, emit a deterministic structural
    // string ourselves; refs are encoded by stable per-fingerprint object id.

    private static string Fingerprint(JsonSchema schema)
    {
        var sb = new StringBuilder();
        var refIds = new Dictionary<JsonSchema, int>(ReferenceEqualityComparer.Instance);
        Emit(schema, sb, refIds);
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void Emit(JsonSchema schema, StringBuilder sb, Dictionary<JsonSchema, int> refIds)
    {
        if (schema.Reference is { } target)
        {
            if (!refIds.TryGetValue(target, out var id))
            {
                id = refIds.Count;
                refIds[target] = id;
            }
            sb.Append("$ref(").Append(id).Append(')');
            return;
        }

        sb.Append('{');
        sb.Append("type:").Append((int)schema.Type).Append(';');
        if (!string.IsNullOrEmpty(schema.Format)) sb.Append("format:").Append(schema.Format).Append(';');

        if (schema.Enumeration is { Count: > 0 } enumValues)
        {
            sb.Append("enum:[");
            foreach (var v in enumValues.Select(x => x?.ToString() ?? "null").OrderBy(x => x, StringComparer.Ordinal))
                sb.Append(v).Append(',');
            sb.Append("];");
        }

        // JSON Schema 2020-12 `const` is stored by NJsonSchema 11.6 in
        // ExtensionData["const"] (no first-class property). Two schemas with
        // the same shape but different `const` values are functionally distinct
        // (e.g. discriminator tags), so they MUST fingerprint differently.
        if (schema.ExtensionData is { } ext && ext.TryGetValue("const", out var constValue) && constValue is not null)
        {
            sb.Append("const:").Append(constValue).Append(';');
        }

        if (schema.Properties is { Count: > 0 })
        {
            sb.Append("props:[");
            foreach (var (k, v) in schema.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append(k).Append('=');
                Emit(v, sb, refIds);
                sb.Append(',');
            }
            sb.Append("];");
        }

        if (schema.RequiredProperties.Count > 0)
        {
            sb.Append("req:[");
            foreach (var r in schema.RequiredProperties.OrderBy(x => x, StringComparer.Ordinal))
                sb.Append(r).Append(',');
            sb.Append("];");
        }

        EmitList("allOf", schema.AllOf, sb, refIds);
        EmitList("anyOf", schema.AnyOf, sb, refIds);
        EmitList("oneOf", schema.OneOf, sb, refIds);

        if (schema.Item is not null) { sb.Append("items:"); Emit(schema.Item, sb, refIds); sb.Append(';'); }
        if (schema.AdditionalPropertiesSchema is not null)
        {
            sb.Append("addl:");
            Emit(schema.AdditionalPropertiesSchema, sb, refIds);
            sb.Append(';');
        }

        sb.Append('}');
    }

    private static void EmitList(string label, ICollection<JsonSchema> members, StringBuilder sb, Dictionary<JsonSchema, int> refIds)
    {
        if (members.Count == 0) return;
        sb.Append(label).Append(":[");
        foreach (var sub in members) { Emit(sub, sb, refIds); sb.Append(','); }
        sb.Append("];");
    }

    private static string StripTrailingDigits(string name)
    {
        var i = name.Length;
        while (i > 0 && char.IsDigit(name[i - 1])) i--;
        return name.Substring(0, i);
    }

    /// <summary>
    /// Longest PascalCase-token-aligned common suffix. Returns "" if names
    /// share no trailing token. Tokenization splits on uppercase boundaries:
    /// "GetDocumentResponseDocumentValidation" → [Get, Document, Response,
    /// Document, Validation].
    /// </summary>
    private static string LongestCommonWordSuffix(IEnumerable<string> names)
    {
        var tokenized = names.Select(TokenizePascal).ToList();
        if (tokenized.Count < 2) return string.Empty;

        var minLength = tokenized.Min(t => t.Count);
        var matchingFromEnd = 0;
        for (var i = 1; i <= minLength; i++)
        {
            var firstTail = tokenized[0][tokenized[0].Count - i];
            if (tokenized.All(t => t[t.Count - i] == firstTail))
                matchingFromEnd++;
            else
                break;
        }

        if (matchingFromEnd == 0) return string.Empty;

        var first = tokenized[0];
        return string.Concat(first.Skip(first.Count - matchingFromEnd));
    }

    private static List<string> TokenizePascal(string name)
    {
        var tokens = new List<string>();
        if (name.Length == 0) return tokens;

        var current = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsUpper(ch) && current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    // ---------- ref redirection ----------

    private static int RedirectReferences(OpenApiDocument document, JsonSchema from, JsonSchema to)
    {
        var count = 0;
        foreach (var schema in EnumerateAllSchemas(document))
        {
            if (ReferenceEquals(schema.Reference, from))
            {
                schema.Reference = to;
                count++;
            }
        }
        return count;
    }

    private static IEnumerable<JsonSchema> EnumerateAllSchemas(OpenApiDocument document)
    {
        var visited = new HashSet<JsonSchema>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<JsonSchema>();

        foreach (var s in document.Definitions.Values) stack.Push(s);
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, op) in pathItem.ActualPathItem)
            {
                if (op.RequestBody?.Content.TryGetValue("application/json", out var rc) == true && rc.Schema is { } reqS)
                    stack.Push(reqS);
                foreach (var (_, resp) in op.Responses)
                    if (resp.Content.TryGetValue("application/json", out var sc) && sc.Schema is { } respS)
                        stack.Push(respS);
            }
        }

        while (stack.Count > 0)
        {
            var s = stack.Pop();
            if (!visited.Add(s)) continue;
            yield return s;

            if (s.Properties is not null)
                foreach (var p in s.Properties.Values) stack.Push(p);
            foreach (var sub in s.AllOf) stack.Push(sub);
            foreach (var sub in s.AnyOf) stack.Push(sub);
            foreach (var sub in s.OneOf) stack.Push(sub);
            if (s.Item is not null) stack.Push(s.Item);
            if (s.AdditionalPropertiesSchema is not null) stack.Push(s.AdditionalPropertiesSchema);
        }
    }

    // ---------- types ----------

    private sealed record InlineSite(JsonSchema Schema, Action<JsonSchema> Replace);

    public sealed record NamingRule(Func<JsonSchema, bool> Match, string Name);
}
