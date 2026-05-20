using FluentAssertions;
using FinanceCore.Domain.ValueObjects;
using Xunit;

namespace FinanceCore.Domain.Tests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Create_AppliesBankersRoundingToFourDecimalPlaces()
    {
        // 0.00005 → .ToEven → 0.0000
        var money = Money.Create(0.00005m, "USD");
        money.Amount.Should().Be(0.0000m);

        // 0.00015 → .ToEven → 0.0002
        var moneyUp = Money.Create(0.00015m, "USD");
        moneyUp.Amount.Should().Be(0.0002m);
    }

    [Fact]
    public void Add_OnSameCurrency_ReturnsSum()
    {
        var a = Money.Create(10m, "USD");
        var b = Money.Create(5m, "USD");

        (a + b).Amount.Should().Be(15m);
    }

    [Fact]
    public void Add_OnDifferentCurrency_Throws()
    {
        var usd = Money.Create(10m, "USD");
        var eur = Money.Create(5m, "EUR");

        var act = () => usd.Add(eur);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*USD*EUR*");
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        var money = Money.Create(100m, "USD");

        var act = () => money.Divide(0m);

        act.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void ConvertTo_SameCurrency_ReturnsSameInstance()
    {
        var money = Money.Create(100m, "USD");

        var converted = money.ConvertTo(Currency.FromCode("USD"), 4000m);

        converted.Should().BeSameAs(money);
    }

    [Fact]
    public void ConvertTo_AppliesRate()
    {
        var money = Money.Create(100m, "USD");

        var converted = money.ConvertTo(Currency.FromCode("COP"), 4150m);

        converted.Amount.Should().Be(415_000m);
        converted.Currency.Code.Should().Be("COP");
    }

    [Fact]
    public void Allocate_DistributesRemainderCentByCent()
    {
        // 1.0000 / 3 = 0.3333 con residuo 0.0001
        var money = Money.Create(1.0000m, "USD");

        var parts = money.Allocate(3);

        parts.Should().HaveCount(3);
        parts.Sum(p => p.Amount).Should().Be(1.0000m);
        parts[0].Amount.Should().Be(0.3334m); // recibe el residuo
        parts[1].Amount.Should().Be(0.3333m);
        parts[2].Amount.Should().Be(0.3333m);
    }

    [Fact]
    public void Equality_ConsiderAmountAndCurrency()
    {
        Money.Create(10m, "USD").Should().Be(Money.Create(10m, "USD"));
        Money.Create(10m, "USD").Should().NotBe(Money.Create(10m, "EUR"));
        Money.Create(10m, "USD").Should().NotBe(Money.Create(11m, "USD"));
    }

    [Theory]
    [InlineData("10.5", "USD", true)]
    [InlineData("", "USD", false)]
    [InlineData("abc", "USD", false)]
    public void TryParse_HandlesEdgeCases(string input, string code, bool expected)
    {
        var ok = Money.TryParse(input, code, out var result);

        ok.Should().Be(expected);
        if (expected) result.Should().NotBeNull();
    }
}
