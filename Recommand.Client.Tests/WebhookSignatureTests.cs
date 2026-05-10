using System;
using System.Security.Cryptography;
using System.Text;
using Recommand.Client.Webhooks;
using Xunit;

namespace Recommand.Client.Tests;

public class WebhookSignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"eventType":"document.received","documentId":"doc_xxx","teamId":"t","companyId":"c"}""");
    private const string Secret = "whsec_test_secret_value";

    [Fact]
    public void Verify_RoundTripsAgainstCompute()
    {
        var sig = WebhookSignature.Compute(Body, Secret);

        Assert.StartsWith("sha256=", sig);
        Assert.True(WebhookSignature.Verify(Body, sig, Secret));
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        var sig = WebhookSignature.Compute(Body, Secret);
        var tampered = (byte[])Body.Clone();
        tampered[0] ^= 0x01;

        Assert.False(WebhookSignature.Verify(tampered, sig, Secret));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var sig = WebhookSignature.Compute(Body, Secret);

        Assert.False(WebhookSignature.Verify(Body, sig, "different_secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("md5=abc123")]              // wrong algorithm prefix
    [InlineData("sha256=")]                  // empty digest
    [InlineData("sha256=zzzz")]              // non-hex
    [InlineData("sha256=abc")]               // odd-length hex
    public void Verify_MalformedHeader_ReturnsFalse(string? header)
    {
        Assert.False(WebhookSignature.Verify(Body, header, Secret));
    }

    [Fact]
    public void Verify_NullBody_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => WebhookSignature.Verify(null!, "sha256=00", Secret));
    }

    [Fact]
    public void Verify_EmptySecret_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => WebhookSignature.Verify(Body, "sha256=00", ""));
    }

    [Fact]
    public void Verify_MatchesIndependentlyComputedHmac()
    {
        // Independent reference: compute the HMAC ourselves with raw .NET
        // primitives. Catches accidental algorithm/format drift.
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(Body);
        var hex = string.Concat(Array.ConvertAll(hash, b => b.ToString("x2")));
        var header = $"sha256={hex}";

        Assert.True(WebhookSignature.Verify(Body, header, Secret));
        Assert.Equal(header, WebhookSignature.Compute(Body, Secret));
    }

    [Fact]
    public void Headers_AreCorrectStrings()
    {
        Assert.Equal("X-Signature", WebhookSignature.SignatureHeader);
        Assert.Equal("X-Idempotency-Key", WebhookSignature.IdempotencyHeader);
    }
}
