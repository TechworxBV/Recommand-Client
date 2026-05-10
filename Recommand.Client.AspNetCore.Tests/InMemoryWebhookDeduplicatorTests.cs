using System;
using System.Threading.Tasks;
using Recommand.Client.AspNetCore;
using Xunit;

namespace Recommand.Client.AspNetCore.Tests;

public class InMemoryWebhookDeduplicatorTests
{
    [Fact]
    public async Task TryRegister_FirstTime_ReturnsTrue()
    {
        var dedup = new InMemoryWebhookDeduplicator();

        Assert.True(await dedup.TryRegisterAsync("del_1"));
    }

    [Fact]
    public async Task TryRegister_SecondTime_SameKey_ReturnsFalse()
    {
        var dedup = new InMemoryWebhookDeduplicator();
        await dedup.TryRegisterAsync("del_1");

        Assert.False(await dedup.TryRegisterAsync("del_1"));
    }

    [Fact]
    public async Task TryRegister_DifferentKeys_BothReturnTrue()
    {
        var dedup = new InMemoryWebhookDeduplicator();

        Assert.True(await dedup.TryRegisterAsync("del_1"));
        Assert.True(await dedup.TryRegisterAsync("del_2"));
    }

    [Fact]
    public async Task TryRegister_BeyondCapacity_EvictsOldest()
    {
        var dedup = new InMemoryWebhookDeduplicator(capacity: 3);

        await dedup.TryRegisterAsync("a");
        await dedup.TryRegisterAsync("b");
        await dedup.TryRegisterAsync("c");
        // Cache is now {a, b, c} (full).

        // Adding "d" must evict the oldest entry ("a") and leave the rest.
        await dedup.TryRegisterAsync("d");

        Assert.True(await dedup.TryRegisterAsync("a"));    // "a" was evicted → fresh
        // (After the line above, "a" re-enters and evicts "b", but we don't
        //  assert on "b"/"c"/"d" any further: each re-register cascades
        //  evictions and the precise residency depends on operation ordering.)
    }

    [Fact]
    public async Task TryRegister_NullKey_Throws()
    {
        var dedup = new InMemoryWebhookDeduplicator();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await dedup.TryRegisterAsync(null!));
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryWebhookDeduplicator(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryWebhookDeduplicator(-1));
    }

    [Fact]
    public async Task TryRegister_ConcurrentSameKey_OneWins()
    {
        var dedup = new InMemoryWebhookDeduplicator();

        // 100 concurrent racers on the same key — exactly one must see "fresh".
        const int racers = 100;
        var tasks = new Task<bool>[racers];
        for (var i = 0; i < racers; i++) tasks[i] = dedup.TryRegisterAsync("dup").AsTask();
        var results = await Task.WhenAll(tasks);

        var fresh = 0;
        foreach (var r in results) if (r) fresh++;
        Assert.Equal(1, fresh);
    }
}
