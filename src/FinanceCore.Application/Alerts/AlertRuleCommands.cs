using FluentValidation;
using MediatR;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Alerts;

#region DTO

/// <summary>
/// Regla de alerta de negocio configurable (SCRUM-45).
/// </summary>
public record AlertRuleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Type { get; init; } = null!;
    public Guid? AccountId { get; init; }
    public Guid? SourceProfileId { get; init; }
    public decimal? ThresholdAmount { get; init; }
    public decimal? ThresholdPercent { get; init; }
    public int? LookbackDays { get; init; }
    public string Channels { get; init; } = null!;
    public string? EmailTo { get; init; }
    public int CooldownHours { get; init; }
    public bool IsEnabled { get; init; }
    public DateTimeOffset? LastTriggeredAt { get; init; }

    internal static AlertRuleDto FromEntity(AlertRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Type = r.Type.ToString(),
        AccountId = r.AccountId,
        SourceProfileId = r.SourceProfileId,
        ThresholdAmount = r.ThresholdAmount,
        ThresholdPercent = r.ThresholdPercent,
        LookbackDays = r.LookbackDays,
        Channels = r.Channels.ToString(),
        EmailTo = r.EmailTo,
        CooldownHours = r.CooldownHours,
        IsEnabled = r.IsEnabled,
        LastTriggeredAt = r.LastTriggeredAt
    };
}

internal static class AlertRuleParsing
{
    internal static bool TryParseChannels(string value, out AlertChannels channels)
        => Enum.TryParse(value, ignoreCase: true, out channels) && channels != AlertChannels.None;
}

#endregion

#region ListAlertRules

public record ListAlertRulesQuery : IRequest<Result<IReadOnlyList<AlertRuleDto>>>;

public class ListAlertRulesQueryHandler
    : IRequestHandler<ListAlertRulesQuery, Result<IReadOnlyList<AlertRuleDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public ListAlertRulesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<AlertRuleDto>>> Handle(
        ListAlertRulesQuery request,
        CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.AlertRules.GetAllAsync(cancellationToken);
        return Result<IReadOnlyList<AlertRuleDto>>.Success(
            rules.Select(AlertRuleDto.FromEntity).ToList());
    }
}

#endregion

#region CreateAlertRule

public record CreateAlertRuleCommand : IRequest<Result<AlertRuleDto>>
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? SourceProfileId { get; init; }
    public decimal? ThresholdAmount { get; init; }
    public decimal? ThresholdPercent { get; init; }
    public int? LookbackDays { get; init; }
    public required string Channels { get; init; }
    public string? EmailTo { get; init; }
    public int CooldownHours { get; init; } = 24;
}

public class CreateAlertRuleCommandValidator : AbstractValidator<CreateAlertRuleCommand>
{
    public CreateAlertRuleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(v => Enum.TryParse<AlertRuleType>(v, ignoreCase: true, out _))
            .WithMessage("Type inválido. Valores: MissingPayout, DiscrepancyThreshold, LowBalance.");
        RuleFor(x => x.Channels)
            .NotEmpty()
            .Must(v => AlertRuleParsing.TryParseChannels(v, out _))
            .WithMessage("Channels inválido. Valores: Email, Webhook, o 'Email, Webhook'.");
        RuleFor(x => x.EmailTo).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.EmailTo));
        RuleFor(x => x.CooldownHours).InclusiveBetween(1, 168);
    }
}

public class CreateAlertRuleCommandHandler
    : IRequestHandler<CreateAlertRuleCommand, Result<AlertRuleDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateAlertRuleCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<AlertRuleDto>> Handle(
        CreateAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var referenceError = await ValidateReferencesAsync(
            _unitOfWork, request.AccountId, request.SourceProfileId, cancellationToken);
        if (referenceError != null)
            return Result<AlertRuleDto>.Failure(referenceError);

        AlertRuleParsing.TryParseChannels(request.Channels, out var channels);

        try
        {
            var rule = AlertRule.Create(
                request.Name,
                Enum.Parse<AlertRuleType>(request.Type, ignoreCase: true),
                request.AccountId,
                request.SourceProfileId,
                request.ThresholdAmount,
                request.ThresholdPercent,
                request.LookbackDays,
                channels,
                request.EmailTo,
                request.CooldownHours);

            _unitOfWork.AlertRules.Add(rule);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<AlertRuleDto>.Success(AlertRuleDto.FromEntity(rule));
        }
        catch (DomainException ex)
        {
            return Result<AlertRuleDto>.Failure(ex.Message);
        }
    }

    internal static async Task<string?> ValidateReferencesAsync(
        IUnitOfWork unitOfWork,
        Guid? accountId,
        Guid? sourceProfileId,
        CancellationToken cancellationToken)
    {
        if (accountId is Guid acct &&
            await unitOfWork.Accounts.GetByIdAsync(acct, cancellationToken) == null)
            return $"No existe la cuenta {acct}";

        if (sourceProfileId is Guid profile &&
            await unitOfWork.SourceProfiles.GetByIdAsync(profile, cancellationToken) == null)
            return $"No existe el perfil de fuente {profile}";

        return null;
    }
}

#endregion

#region UpdateAlertRule

public record UpdateAlertRuleCommand : IRequest<Result<AlertRuleDto>>
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? SourceProfileId { get; init; }
    public decimal? ThresholdAmount { get; init; }
    public decimal? ThresholdPercent { get; init; }
    public int? LookbackDays { get; init; }
    public required string Channels { get; init; }
    public string? EmailTo { get; init; }
    public int CooldownHours { get; init; } = 24;
    public bool IsEnabled { get; init; } = true;
}

public class UpdateAlertRuleCommandValidator : AbstractValidator<UpdateAlertRuleCommand>
{
    public UpdateAlertRuleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Channels)
            .NotEmpty()
            .Must(v => AlertRuleParsing.TryParseChannels(v, out _))
            .WithMessage("Channels inválido. Valores: Email, Webhook, o 'Email, Webhook'.");
        RuleFor(x => x.EmailTo).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.EmailTo));
        RuleFor(x => x.CooldownHours).InclusiveBetween(1, 168);
    }
}

public class UpdateAlertRuleCommandHandler
    : IRequestHandler<UpdateAlertRuleCommand, Result<AlertRuleDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAlertRuleCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<AlertRuleDto>> Handle(
        UpdateAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await _unitOfWork.AlertRules.GetByIdAsync(request.Id, cancellationToken);
        if (rule == null)
            return Result<AlertRuleDto>.Failure($"No existe la regla {request.Id}");

        var referenceError = await CreateAlertRuleCommandHandler.ValidateReferencesAsync(
            _unitOfWork, request.AccountId, request.SourceProfileId, cancellationToken);
        if (referenceError != null)
            return Result<AlertRuleDto>.Failure(referenceError);

        AlertRuleParsing.TryParseChannels(request.Channels, out var channels);

        try
        {
            rule.Update(
                request.Name,
                request.AccountId,
                request.SourceProfileId,
                request.ThresholdAmount,
                request.ThresholdPercent,
                request.LookbackDays,
                channels,
                request.EmailTo,
                request.CooldownHours,
                request.IsEnabled);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AlertRuleDto>.Success(AlertRuleDto.FromEntity(rule));
        }
        catch (DomainException ex)
        {
            return Result<AlertRuleDto>.Failure(ex.Message);
        }
    }
}

#endregion

#region DeleteAlertRule

public record DeleteAlertRuleCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteAlertRuleCommandHandler
    : IRequestHandler<DeleteAlertRuleCommand, Result<bool>>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteAlertRuleCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<bool>> Handle(
        DeleteAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await _unitOfWork.AlertRules.GetByIdAsync(request.Id, cancellationToken);
        if (rule == null)
            return Result<bool>.Failure($"No existe la regla {request.Id}");

        _unitOfWork.AlertRules.Remove(rule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}

#endregion
