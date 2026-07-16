using FluentAssertions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Services;
using Xunit;

namespace FinanceCore.Domain.Tests.Services;

public class AlertRuleEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 14);

    private static AlertRule MissingPayoutRule(int lookbackDays) =>
        AlertRule.Create(
            "Payout PayU", AlertRuleType.MissingPayout, null, Guid.NewGuid(),
            null, null, lookbackDays, AlertChannels.Email, null);

    private static AlertRule DiscrepancyRule(decimal? amount, decimal? percent = null) =>
        AlertRule.Create(
            "Descuadre", AlertRuleType.DiscrepancyThreshold, null, null,
            amount, percent, null, AlertChannels.Email, null);

    private static AlertRule LowBalanceRule(decimal threshold) =>
        AlertRule.Create(
            "Saldo bajo", AlertRuleType.LowBalance, Guid.NewGuid(), null,
            threshold, null, null, AlertChannels.Email, null);

    // ─── MissingPayout ───────────────────────────────────────────────────────

    [Fact]
    public void MissingPayout_WithinWindow_DoesNotTrigger()
    {
        var rule = MissingPayoutRule(lookbackDays: 7);

        var trigger = AlertRuleEvaluator.EvaluateMissingPayout(
            rule, Today.AddDays(-7), Today, "PayU");

        trigger.Should().BeNull();
    }

    [Fact]
    public void MissingPayout_PastWindow_TriggersError()
    {
        var rule = MissingPayoutRule(lookbackDays: 7);

        var trigger = AlertRuleEvaluator.EvaluateMissingPayout(
            rule, Today.AddDays(-8), Today, "PayU");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Error);
        trigger.Title.Should().Contain("PayU");
        trigger.Message.Should().Contain("8 días");
    }

    [Fact]
    public void MissingPayout_WayPastWindow_TriggersCritical()
    {
        var rule = MissingPayoutRule(lookbackDays: 7);

        var trigger = AlertRuleEvaluator.EvaluateMissingPayout(
            rule, Today.AddDays(-15), Today, "PayU");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Critical);
    }

    [Fact]
    public void MissingPayout_NeverSeen_UsesRuleCreationAsBaseline()
    {
        // Regla recién creada y sin payouts históricos: no dispara (baseline = hoy).
        var rule = MissingPayoutRule(lookbackDays: 7);

        var trigger = AlertRuleEvaluator.EvaluateMissingPayout(
            rule, lastPayoutDate: null, DateOnly.FromDateTime(DateTime.UtcNow), "PayU");

        trigger.Should().BeNull();
    }

    // ─── DiscrepancyThreshold ────────────────────────────────────────────────

    [Fact]
    public void Discrepancy_BelowThreshold_DoesNotTrigger()
    {
        var rule = DiscrepancyRule(amount: 100_000m);

        var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
            rule, discrepancyAmount: -99_999m, totalExternalAmount: 5_000_000m, Today, "Cta");

        trigger.Should().BeNull();
    }

    [Fact]
    public void Discrepancy_OverAmountThreshold_TriggersError()
    {
        var rule = DiscrepancyRule(amount: 100_000m);

        var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
            rule, -150_000m, 5_000_000m, Today, "Cta Bancolombia");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Error);
        trigger.Title.Should().Contain("Cta Bancolombia");
    }

    [Fact]
    public void Discrepancy_TenTimesThreshold_TriggersCritical()
    {
        var rule = DiscrepancyRule(amount: 100_000m);

        var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
            rule, 1_000_000m, 5_000_000m, Today, "Cta");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Critical);
    }

    [Fact]
    public void Discrepancy_OverPercentThreshold_Triggers()
    {
        // 3% de descuadre con umbral del 2%.
        var rule = DiscrepancyRule(amount: null, percent: 0.02m);

        var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
            rule, 150_000m, 5_000_000m, Today, "Cta");

        trigger.Should().NotBeNull();
        trigger!.Message.Should().Contain("%");
    }

    [Fact]
    public void Discrepancy_PercentRule_WithZeroExternalTotal_DoesNotTrigger()
    {
        var rule = DiscrepancyRule(amount: null, percent: 0.02m);

        var trigger = AlertRuleEvaluator.EvaluateDiscrepancy(
            rule, 150_000m, totalExternalAmount: 0m, Today, "Cta");

        trigger.Should().BeNull();
    }

    // ─── LowBalance ──────────────────────────────────────────────────────────

    [Fact]
    public void LowBalance_AboveThreshold_DoesNotTrigger()
    {
        var rule = LowBalanceRule(threshold: 1_000_000m);

        AlertRuleEvaluator.EvaluateLowBalance(rule, 1_000_000m, "COP", "Cta")
            .Should().BeNull("el umbral es exclusivo: igual no dispara");
    }

    [Fact]
    public void LowBalance_BelowThreshold_TriggersWarning()
    {
        var rule = LowBalanceRule(1_000_000m);

        var trigger = AlertRuleEvaluator.EvaluateLowBalance(rule, 999_999m, "COP", "Cta");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Warning);
        trigger.Message.Should().Contain("COP");
    }

    [Fact]
    public void LowBalance_NegativeBalance_TriggersCritical()
    {
        var rule = LowBalanceRule(1_000_000m);

        var trigger = AlertRuleEvaluator.EvaluateLowBalance(rule, -50_000m, "COP", "Cta");

        trigger.Should().NotBeNull();
        trigger!.Severity.Should().Be(AlertTriggerSeverity.Critical);
    }
}
