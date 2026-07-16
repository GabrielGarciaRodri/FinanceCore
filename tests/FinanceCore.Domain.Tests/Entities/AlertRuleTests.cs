using FluentAssertions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using Xunit;

namespace FinanceCore.Domain.Tests.Entities;

public class AlertRuleTests
{
    private static AlertRule MissingPayoutRule(int lookbackDays = 7, int cooldownHours = 24) =>
        AlertRule.Create(
            "Payout PayU no llegó",
            AlertRuleType.MissingPayout,
            accountId: null,
            sourceProfileId: Guid.NewGuid(),
            thresholdAmount: null,
            thresholdPercent: null,
            lookbackDays: lookbackDays,
            AlertChannels.Email,
            emailTo: null,
            cooldownHours);

    [Fact]
    public void Create_MissingPayout_Valid()
    {
        var rule = MissingPayoutRule();

        rule.IsEnabled.Should().BeTrue();
        rule.Type.Should().Be(AlertRuleType.MissingPayout);
        rule.LookbackDays.Should().Be(7);
        rule.LastTriggeredAt.Should().BeNull();
    }

    [Fact]
    public void Create_MissingPayout_RequiresSourceProfile()
    {
        var act = () => AlertRule.Create(
            "x", AlertRuleType.MissingPayout, null, sourceProfileId: null,
            null, null, lookbackDays: 7, AlertChannels.Email, null);

        act.Should().Throw<DomainException>().WithMessage("*perfil de fuente*");
    }

    [Fact]
    public void Create_MissingPayout_RequiresLookbackDays()
    {
        var act = () => AlertRule.Create(
            "x", AlertRuleType.MissingPayout, null, Guid.NewGuid(),
            null, null, lookbackDays: null, AlertChannels.Email, null);

        act.Should().Throw<DomainException>().WithMessage("*LookbackDays*");
    }

    [Fact]
    public void Create_DiscrepancyThreshold_RequiresSomeThreshold()
    {
        var act = () => AlertRule.Create(
            "x", AlertRuleType.DiscrepancyThreshold, null, null,
            thresholdAmount: null, thresholdPercent: null, null, AlertChannels.Email, null);

        act.Should().Throw<DomainException>().WithMessage("*umbral*");
    }

    [Fact]
    public void Create_LowBalance_RequiresAccountAndThreshold()
    {
        var noAccount = () => AlertRule.Create(
            "x", AlertRuleType.LowBalance, accountId: null, null,
            thresholdAmount: 100m, null, null, AlertChannels.Email, null);
        noAccount.Should().Throw<DomainException>().WithMessage("*cuenta*");

        var noThreshold = () => AlertRule.Create(
            "x", AlertRuleType.LowBalance, Guid.NewGuid(), null,
            thresholdAmount: null, null, null, AlertChannels.Email, null);
        noThreshold.Should().Throw<DomainException>().WithMessage("*umbral*");
    }

    [Fact]
    public void Create_RequiresAtLeastOneChannel()
    {
        var act = () => AlertRule.Create(
            "x", AlertRuleType.DiscrepancyThreshold, null, null,
            100m, null, null, AlertChannels.None, null);

        act.Should().Throw<DomainException>().WithMessage("*canal*");
    }

    [Fact]
    public void CanTrigger_RespectsCooldown()
    {
        var rule = MissingPayoutRule(cooldownHours: 24);
        var now = DateTimeOffset.UtcNow;

        rule.CanTrigger(now).Should().BeTrue("recién creada, nunca disparó");

        rule.MarkTriggered(now);
        rule.CanTrigger(now.AddHours(23)).Should().BeFalse("dentro del cooldown");
        rule.CanTrigger(now.AddHours(25)).Should().BeTrue("pasado el cooldown");
    }

    [Fact]
    public void CanTrigger_DisabledRule_NeverTriggers()
    {
        var rule = MissingPayoutRule();
        rule.Update(
            rule.Name, rule.AccountId, rule.SourceProfileId, rule.ThresholdAmount,
            rule.ThresholdPercent, rule.LookbackDays, rule.Channels, rule.EmailTo,
            rule.CooldownHours, isEnabled: false);

        rule.CanTrigger(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Update_KeepsType_AndAppliesChanges()
    {
        var rule = MissingPayoutRule(lookbackDays: 7);

        rule.Update(
            "Nuevo nombre", null, rule.SourceProfileId, null, null,
            lookbackDays: 10, AlertChannels.Email | AlertChannels.Webhook,
            "ops@acme.co", cooldownHours: 48, isEnabled: true);

        rule.Type.Should().Be(AlertRuleType.MissingPayout);
        rule.Name.Should().Be("Nuevo nombre");
        rule.LookbackDays.Should().Be(10);
        rule.Channels.Should().Be(AlertChannels.Email | AlertChannels.Webhook);
        rule.EmailTo.Should().Be("ops@acme.co");
        rule.CooldownHours.Should().Be(48);
    }
}
