using FluentAssertions;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Events;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.ValueObjects;
using Xunit;

namespace FinanceCore.Domain.Tests.Entities;

public class TransactionTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void CreateDebit_ForcesNegativeAmount_RegardlessOfInputSign()
    {
        var positive = Transaction.CreateDebit("ext-1", "TEST", AccountId, 150m, "USD", Today);
        var negative = Transaction.CreateDebit("ext-2", "TEST", AccountId, -150m, "USD", Today);

        positive.Amount.Amount.Should().Be(-150m);
        negative.Amount.Amount.Should().Be(-150m);
        positive.Type.Should().Be(TransactionType.Debit);
    }

    [Fact]
    public void CreateCredit_ForcesPositiveAmount()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, -200m, "USD", Today);

        tx.Amount.Amount.Should().Be(200m);
        tx.Type.Should().Be(TransactionType.Credit);
    }

    [Fact]
    public void Create_EmptyAccountId_Throws()
    {
        var act = () => Transaction.CreateCredit("ext-1", "TEST", Guid.Empty, 10m, "USD", Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*ID de cuenta es requerido*");
    }

    [Fact]
    public void Create_EmptyExternalId_Throws()
    {
        var act = () => Transaction.CreateCredit("", "TEST", AccountId, 10m, "USD", Today);

        act.Should().Throw<DomainException>()
           .WithMessage("*ID externo*");
    }

    [Fact]
    public void Create_RaisesTransactionCreatedDomainEvent()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, 10m, "USD", Today);

        tx.DomainEvents.Should().HaveCount(1);
        tx.DomainEvents.Single().Should().BeOfType<TransactionCreatedEvent>();
    }

    [Fact]
    public void StateTransitions_FollowAllowedPath()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, 10m, "USD", Today);

        tx.Status.Should().Be(TransactionStatus.Pending);

        tx.StartProcessing();
        tx.Status.Should().Be(TransactionStatus.Processing);

        tx.MarkAsValidated();
        tx.Status.Should().Be(TransactionStatus.Validated);

        tx.Post();
        tx.Status.Should().Be(TransactionStatus.Posted);
        tx.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void Post_FromPending_Throws()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, 10m, "USD", Today);

        var act = () => tx.Post();

        act.Should().Throw<DomainException>()
           .WithMessage("*Solo se pueden contabilizar transacciones validadas*");
    }

    [Fact]
    public void Reconcile_RequiresPostedStatus()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, 10m, "USD", Today);

        var act = () => tx.Reconcile(Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reconcile_OnPosted_RecordsReconciliationId()
    {
        var tx = Transaction.CreateCredit("ext-1", "TEST", AccountId, 10m, "USD", Today);
        tx.StartProcessing();
        tx.MarkAsValidated();
        tx.Post();

        var reconciliationId = Guid.NewGuid();
        tx.Reconcile(reconciliationId);

        tx.Status.Should().Be(TransactionStatus.Reconciled);
        tx.ReconciliationId.Should().Be(reconciliationId);
        tx.ReconciledAt.Should().NotBeNull();
    }

    [Fact]
    public void Hash_ChangesWhenAmountChanges()
    {
        var tx1 = Transaction.CreateCredit("ext-1", "TEST", AccountId, 100m, "USD", Today);
        var tx2 = Transaction.CreateCredit("ext-1", "TEST", AccountId, 200m, "USD", Today);

        tx1.Hash.Should().NotBe(tx2.Hash);
    }

    [Fact]
    public void Hash_StableForSameInputs()
    {
        var tx1 = Transaction.CreateCredit("ext-1", "TEST", AccountId, 100m, "USD", Today);
        var tx2 = Transaction.CreateCredit("ext-1", "TEST", AccountId, 100m, "USD", Today);

        tx1.Hash.Should().Be(tx2.Hash);
    }
}
