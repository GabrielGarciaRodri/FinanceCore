using FluentAssertions;
using FinanceCore.Domain.Services;
using Xunit;

namespace FinanceCore.Domain.Tests.Services;

public class GroupMatcherTests
{
    private static readonly DateOnly D1 = new(2026, 7, 1);
    private static readonly DateOnly D2 = new(2026, 7, 2);
    private static readonly DateOnly D3 = new(2026, 7, 3);
    private static readonly DateOnly PayoutDate = new(2026, 7, 5);

    private static GroupCandidate Sale(decimal amount, DateOnly date) =>
        new(Guid.NewGuid(), amount, date);

    [Fact]
    public void WholeWindow_InsideFeeBand_Matches()
    {
        // Bruto 1000, comisión 3.5% → payout 965. Banda [3%, 4%].
        var pool = new[] { Sale(100, D1), Sale(200, D1), Sale(300, D2), Sale(400, D3) };

        var result = GroupMatcher.FindGroup(965m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().NotBeNull();
        result.Match!.Items.Should().HaveCount(4);
        result.Match.GroupedAmount.Should().Be(1000m);
        result.Match.FeeAmount.Should().Be(35m);
        result.Match.FeePercent.Should().Be(0.035m);
        result.Match.WindowStart.Should().Be(D1);
        result.Match.WindowEnd.Should().Be(D3);
    }

    [Fact]
    public void ContiguousSubrange_Matches_AndPrefersRangeEndingClosestToPayout()
    {
        // d1 (corte anterior) suma 500 y TAMBIÉN caería en banda para este
        // payout — pero [d2..d3] termina más cerca del payout y debe ganar.
        var pool = new[] { Sale(500, D1), Sale(300, D2), Sale(200, D3) };

        // payout por el bruto 500 de [d2..d3] con 3.5%: 482.50
        var result = GroupMatcher.FindGroup(482.5m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().NotBeNull();
        result.Match!.GroupedAmount.Should().Be(500m);
        result.Match.WindowStart.Should().Be(D2);
        result.Match.WindowEnd.Should().Be(D3);
        result.Match.Items.Select(i => i.Amount).Should().BeEquivalentTo(new[] { 300m, 200m });
    }

    [Fact]
    public void RefundInsideWindow_NetsIntoTheGroup()
    {
        // 600 − 100 (devolución) + 500 = bruto neto 1000 → payout 965.
        var pool = new[] { Sale(600, D1), Sale(-100, D2), Sale(500, D3) };

        var result = GroupMatcher.FindGroup(965m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().NotBeNull();
        result.Match!.Items.Should().HaveCount(3);
        result.Match.GroupedAmount.Should().Be(1000m);
    }

    [Fact]
    public void FeeOutsideBand_ReturnsNearMissNote_NotMatch()
    {
        // Bruto 1000, payout 900 → comisión implícita 10%, banda [3%, 4%].
        var pool = new[] { Sale(300, D1), Sale(300, D2), Sale(400, D3) };

        var result = GroupMatcher.FindGroup(900m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().BeNull();
        result.NearMissNote.Should().NotBeNullOrEmpty();
        result.NearMissNote.Should().Contain("1000");
    }

    [Fact]
    public void ZeroFeeProfile_RequiresExactSum()
    {
        var pool = new[] { Sale(250, D1), Sale(250, D2) };

        var exact = GroupMatcher.FindGroup(500m, PayoutDate, pool, 0m, 0m);
        exact.Match.Should().NotBeNull();
        exact.Match!.FeeAmount.Should().Be(0m);

        var off = GroupMatcher.FindGroup(499m, PayoutDate, pool, 0m, 0m);
        off.Match.Should().BeNull();
        off.NearMissNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PoolSmallerThanPayout_ReturnsNone_WithoutNote()
    {
        var pool = new[] { Sale(100, D1) };

        var result = GroupMatcher.FindGroup(500m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().BeNull();
        result.NearMissNote.Should().BeNull();
    }

    [Fact]
    public void EmptyPool_OrNonPositivePayout_ReturnsNone()
    {
        GroupMatcher.FindGroup(100m, PayoutDate, Array.Empty<GroupCandidate>(), 0.035m, 0.005m)
            .Should().Be(GroupMatchResult.None);

        GroupMatcher.FindGroup(0m, PayoutDate, new[] { Sale(100, D1) }, 0.035m, 0.005m)
            .Should().Be(GroupMatchResult.None);

        GroupMatcher.FindGroup(-5m, PayoutDate, new[] { Sale(100, D1) }, 0.035m, 0.005m)
            .Should().Be(GroupMatchResult.None);
    }

    [Fact]
    public void SameEndRange_PrefersLargerSumInsideBand()
    {
        // Dos rangos terminan en d3 y ambos caen en banda con el mismo payout:
        // [d2..d3] = 1000 (fee 3.5%) y [d3] = 1000 no — construimos el caso:
        // d2 = 0.5 (ruido chico), d3 = 1000. payout 965: [d3] fee 3.5% ✓,
        // [d2..d3] = 1000.5 → fee 3.548% ✓ también. Debe elegir la suma mayor.
        var pool = new[] { Sale(0.5m, D2), Sale(1000m, D3) };

        var result = GroupMatcher.FindGroup(965m, PayoutDate, pool, 0.035m, 0.005m);

        result.Match.Should().NotBeNull();
        result.Match!.GroupedAmount.Should().Be(1000.5m);
        result.Match.WindowStart.Should().Be(D2);
    }
}
