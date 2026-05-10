using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Recommand.Client.Webhooks;

/// <summary>
/// Verifies HMAC-SHA256 signatures on webhook deliveries.
/// </summary>
/// <remarks>
/// <para>
/// The Recommand API sends an <c>X-Signature</c> header
/// (format: <c>sha256=&lt;hex digest&gt;</c>, GitHub-style) for any webhook
/// subscription that has a shared secret configured. To verify a delivery is
/// authentic and untampered, recompute the HMAC over the raw request body
/// using the same secret and compare in constant time.
/// </para>
/// <para>
/// <b>Use the raw body bytes.</b> The signature is computed over the exact
/// bytes the server sent. Re-serialising a parsed object produces different
/// bytes (whitespace, key ordering, number formatting) and will fail
/// verification. If you're using ASP.NET Core, read the request body before
/// model-binding (e.g. by enabling <c>EnableBuffering</c> and reading the
/// stream into a byte array first) — then verify, then deserialise.
/// </para>
/// <para>
/// <b>This SDK does not currently know your subscription's shared secret.</b>
/// The <c>POST /v1/webhooks</c> endpoint does not return a secret on creation,
/// and the spec does not (yet) describe how the secret is configured. Until
/// that's resolved upstream, treat the webhook URL itself as a shared secret
/// (HTTPS only, keep it out of logs and public repos) and pass the secret you
/// configured via whatever side channel exists.
/// </para>
/// </remarks>
public static class WebhookSignature
{
    /// <summary>HTTP header name used for the HMAC signature.</summary>
    public const string SignatureHeader = "X-Signature";

    /// <summary>HTTP header name used for the per-delivery idempotency key.</summary>
    public const string IdempotencyHeader = "X-Idempotency-Key";

    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Verify the <c>X-Signature</c> header value against the raw request body
    /// using the subscription's shared secret.
    /// </summary>
    /// <param name="rawBody">
    /// Exact bytes of the HTTP request body. Do not pass a re-serialised object.
    /// </param>
    /// <param name="signatureHeader">
    /// The value of the <c>X-Signature</c> header, e.g. <c>"sha256=abcd…"</c>.
    /// May be <c>null</c> or empty (returns <c>false</c>).
    /// </param>
    /// <param name="secret">
    /// The shared secret configured for this webhook subscription.
    /// </param>
    /// <returns>
    /// <c>true</c> only when the signature is well-formed, valid for this
    /// body, and computed with the supplied secret. <c>false</c> for missing,
    /// malformed, or mismatched signatures.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="rawBody"/> or <paramref name="secret"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="secret"/> is empty.
    /// </exception>
    public static bool Verify(byte[] rawBody, string? signatureHeader, string secret)
    {
        if (rawBody is null) throw new ArgumentNullException(nameof(rawBody));
        if (secret is null) throw new ArgumentNullException(nameof(secret));
        if (secret.Length == 0) throw new ArgumentException("Secret cannot be empty.", nameof(secret));
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal)) return false;

        var providedHex = signatureHeader.Substring(SignaturePrefix.Length);
        if (providedHex.Length == 0 || providedHex.Length % 2 != 0) return false;
        if (!TryParseHex(providedHex, out var providedBytes)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);

        return CryptographicOperations.FixedTimeEquals(computed, providedBytes);
    }

    /// <summary>
    /// Compute the signature header value for a body + secret, matching the
    /// format Recommand sends (<c>sha256=&lt;hex digest&gt;</c>). Useful when
    /// implementing test doubles, replaying deliveries, or signing your own
    /// outbound webhooks with the same convention.
    /// </summary>
    public static string Compute(byte[] rawBody, string secret)
    {
        if (rawBody is null) throw new ArgumentNullException(nameof(rawBody));
        if (secret is null) throw new ArgumentNullException(nameof(secret));
        if (secret.Length == 0) throw new ArgumentException("Secret cannot be empty.", nameof(secret));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(rawBody);
        return SignaturePrefix + ToHexLower(digest);
    }

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(
                    hex.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var b))
            {
                bytes = Array.Empty<byte>();
                return false;
            }
            bytes[i] = b;
        }
        return true;
    }

    private static string ToHexLower(byte[] bytes)
    {
        // Avoid Convert.ToHexString — not available on netstandard2.1.
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
