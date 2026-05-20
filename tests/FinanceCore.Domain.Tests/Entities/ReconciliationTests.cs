using FluentAssertions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using Xunit;

namespace FinanceCore.Domain.Tests.Entities;

public class ReconciliationTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void Start_InitializesInProgress()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        r.Status.Should().Be(ReconciliationStatus.InProgress);
        r.ProcessedBy.Should().Be("SYSTEM");
        r.StartedAt.Should().NotBeNull();
        r.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public void Start_EmptyAccountId_Throws()
    {
        var act = () => Reconciliation.Start(Guid.Empty, Date, "SYSTEM");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_EmptyProcessedBy_Throws()
    {
        var act = () => Reconciliation.Start(AccountId, Date, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_WithoutDiscrepancies_TransitionsToCompleted()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        r.Complete(
            totalInternal: 5, totalExternal: 5, matched: 5,
            unmatchedInternal: 0, unmatchedExternal: 0,
            totalInternalAmount: 100m, totalExternalAmount: 100m,
            discrepancyAmount: 0m);

        r.Status.Should().Be(ReconciliationStatus.Completed);
        r.CompletedAt.Should().NotBeNull();
        r.DurationMs.Should().NotBeNull();
    }

    [Fact]
    public void Complete_WithDiscrepancies_TransitionsToCompletedWithDiscrepancies()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        r.AddDiscrepancy(
            DiscrepancyType.AmountMismatch,
            internalTransactionId: null,
            externalReference: "X",
            internalAmount: 100m, externalAmount: 99m,
            internalDate: Date, externalDate: Date);

        r.Complete(
            totalInternal: 5, totalExternal: 5, matched: 4,
            unmatchedInternal: 0, unmatchedExternal: 0,
            totalInternalAmount: 100m, totalExternalAmount: 99m,
            discrepancyAmount: -1m);

        r.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
        r.Discrepancies.Should().HaveCount(1);
    }

    [Fact]
    public void Complete_WithUnmatchedRecords_TransitionsToCompletedWithDiscrepancies()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        r.Complete(
            totalInternal: 5, totalExternal: 7, matched: 5,
            unmatchedInternal: 0, unmatchedExternal: 2,
            totalInternalAmount: 100m, totalExternalAmount: 120m,
            discrepancyAmount: 20m);

        r.Status.Should().Be(ReconciliationStatus.CompletedWithDiscrepancies);
    }

    [Fact]
    public void Fail_MovesToFailed_AndAppendsReason()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM", notes: "initial");

        r.Fail("DB timeout");

        r.Status.Should().Be(ReconciliationStatus.Failed);
        r.Notes.Should().Contain("DB timeout").And.Contain("initial");
    }

    [Fact]
    public void AddDiscrepancy_ComputesDifferenceWhenBothAmountsPresent()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        var disc = r.AddDiscrepancy(
            DiscrepancyType.AmountMismatch,
            internalTransactionId: null,
            externalReference: "X",
            internalAmount: 100m, externalAmount: 95m,
            internalDate: Date, externalDate: Date);

        disc.DifferenceAmount.Should().Be(-5m); // external - internal
    }

    [Fact]
    public void Approve_OnTerminalStatus_RecordsApprover()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");
        r.Complete(0, 0, 0, 0, 0, 0m, 0m, 0m);

        r.Approve("auditor@example.com", "approved with note");

        r.ApprovedBy.Should().Be("auditor@example.com");
        r.ApprovedAt.Should().NotBeNull();
        r.ResolutionNotes.Should().Be("approved with note");
    }

    [Fact]
    public void Approve_OnInProgress_Throws()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");

        var act = () => r.Approve("auditor@example.com");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resolve_TerminalResolution_MarksAsResolved()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");
        var disc = r.AddDiscrepancy(
            DiscrepancyType.AmountMismatch, null, "X",
            100m, 99m, Date, Date);

        disc.Resolve(ResolutionType.AdjustmentCreated, "user");

        disc.IsResolved.Should().BeTrue();
        disc.ResolvedBy.Should().Be("user");
        disc.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void Resolve_PendingOrUnderInvestigation_StaysUnresolved()
    {
        var r = Reconciliation.Start(AccountId, Date, "SYSTEM");
        var disc = r.AddDiscrepancy(
            DiscrepancyType.AmountMismatch, null, "X",
            100m, 99m, Date, Date);

        disc.Resolve(ResolutionType.UnderInvestigation, "user");

        disc.IsResolved.Should().BeFalse();
        disc.ResolvedAt.Should().BeNull();
    }
}
