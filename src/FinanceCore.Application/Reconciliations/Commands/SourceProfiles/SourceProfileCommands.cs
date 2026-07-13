using FluentValidation;
using MediatR;
using FinanceCore.Application.Common.Models;
using FinanceCore.Domain.Entities;
using FinanceCore.Domain.Enums;
using FinanceCore.Domain.Exceptions;
using FinanceCore.Domain.Repositories;

namespace FinanceCore.Application.Reconciliations.Commands.SourceProfiles;

#region DTO

/// <summary>
/// Perfil de conciliación por fuente/pasarela para el matching N:1.
/// </summary>
public record SourceProfileDto
{
    public Guid Id { get; init; }
    public Guid? AccountId { get; init; }
    public string SourceKey { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public string PayoutPattern { get; init; } = null!;
    public string InternalMatchField { get; init; } = null!;
    public string InternalMatchPattern { get; init; } = null!;
    public decimal ExpectedFeePercent { get; init; }
    public decimal FeeTolerancePercent { get; init; }
    public int GroupingWindowDays { get; init; }
    public bool IsActive { get; init; }

    internal static SourceProfileDto FromEntity(ReconciliationSourceProfile p) => new()
    {
        Id = p.Id,
        AccountId = p.AccountId,
        SourceKey = p.SourceKey,
        DisplayName = p.DisplayName,
        PayoutPattern = p.PayoutPattern,
        InternalMatchField = p.InternalMatchField.ToString(),
        InternalMatchPattern = p.InternalMatchPattern,
        ExpectedFeePercent = p.ExpectedFeePercent,
        FeeTolerancePercent = p.FeeTolerancePercent,
        GroupingWindowDays = p.GroupingWindowDays,
        IsActive = p.IsActive
    };
}

#endregion

#region ListSourceProfiles

public record ListSourceProfilesQuery : IRequest<Result<IReadOnlyList<SourceProfileDto>>>;

public class ListSourceProfilesQueryHandler
    : IRequestHandler<ListSourceProfilesQuery, Result<IReadOnlyList<SourceProfileDto>>>
{
    private readonly IUnitOfWork _unitOfWork;

    public ListSourceProfilesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<IReadOnlyList<SourceProfileDto>>> Handle(
        ListSourceProfilesQuery request,
        CancellationToken cancellationToken)
    {
        var profiles = await _unitOfWork.SourceProfiles.GetAllAsync(cancellationToken);
        return Result<IReadOnlyList<SourceProfileDto>>.Success(
            profiles.Select(SourceProfileDto.FromEntity).ToList());
    }
}

#endregion

#region CreateSourceProfile

public record CreateSourceProfileCommand : IRequest<Result<SourceProfileDto>>
{
    public Guid? AccountId { get; init; }
    public required string SourceKey { get; init; }
    public required string DisplayName { get; init; }
    public required string PayoutPattern { get; init; }
    public required string InternalMatchField { get; init; }
    public required string InternalMatchPattern { get; init; }
    public decimal ExpectedFeePercent { get; init; }
    public decimal FeeTolerancePercent { get; init; }
    public int GroupingWindowDays { get; init; }
}

public class CreateSourceProfileCommandValidator : AbstractValidator<CreateSourceProfileCommand>
{
    public CreateSourceProfileCommandValidator()
    {
        RuleFor(x => x.SourceKey).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PayoutPattern).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InternalMatchPattern).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InternalMatchField)
            .NotEmpty()
            .Must(v => Enum.TryParse<InternalMatchField>(v, ignoreCase: true, out _))
            .WithMessage("InternalMatchField inválido. Valores: ExternalIdSource, Category, CounterpartyName.");
        RuleFor(x => x.ExpectedFeePercent).InclusiveBetween(0m, 0.49999m);
        RuleFor(x => x.FeeTolerancePercent).InclusiveBetween(0m, 0.19999m);
        RuleFor(x => x.GroupingWindowDays).InclusiveBetween(1, 62);
    }
}

public class CreateSourceProfileCommandHandler
    : IRequestHandler<CreateSourceProfileCommand, Result<SourceProfileDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateSourceProfileCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SourceProfileDto>> Handle(
        CreateSourceProfileCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = ReconciliationSourceProfile.Create(
                request.AccountId,
                request.SourceKey,
                request.DisplayName,
                request.PayoutPattern,
                Enum.Parse<InternalMatchField>(request.InternalMatchField, ignoreCase: true),
                request.InternalMatchPattern,
                request.ExpectedFeePercent,
                request.FeeTolerancePercent,
                request.GroupingWindowDays);

            _unitOfWork.SourceProfiles.Add(profile);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<SourceProfileDto>.Success(SourceProfileDto.FromEntity(profile));
        }
        catch (DomainException ex)
        {
            return Result<SourceProfileDto>.Failure(ex.Message);
        }
    }
}

#endregion

#region UpdateSourceProfile

public record UpdateSourceProfileCommand : IRequest<Result<SourceProfileDto>>
{
    public Guid Id { get; init; }
    public required string SourceKey { get; init; }
    public required string DisplayName { get; init; }
    public required string PayoutPattern { get; init; }
    public required string InternalMatchField { get; init; }
    public required string InternalMatchPattern { get; init; }
    public decimal ExpectedFeePercent { get; init; }
    public decimal FeeTolerancePercent { get; init; }
    public int GroupingWindowDays { get; init; }
    public bool IsActive { get; init; } = true;
}

public class UpdateSourceProfileCommandValidator : AbstractValidator<UpdateSourceProfileCommand>
{
    public UpdateSourceProfileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.SourceKey).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PayoutPattern).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InternalMatchPattern).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InternalMatchField)
            .NotEmpty()
            .Must(v => Enum.TryParse<InternalMatchField>(v, ignoreCase: true, out _))
            .WithMessage("InternalMatchField inválido. Valores: ExternalIdSource, Category, CounterpartyName.");
        RuleFor(x => x.ExpectedFeePercent).InclusiveBetween(0m, 0.49999m);
        RuleFor(x => x.FeeTolerancePercent).InclusiveBetween(0m, 0.19999m);
        RuleFor(x => x.GroupingWindowDays).InclusiveBetween(1, 62);
    }
}

public class UpdateSourceProfileCommandHandler
    : IRequestHandler<UpdateSourceProfileCommand, Result<SourceProfileDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateSourceProfileCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SourceProfileDto>> Handle(
        UpdateSourceProfileCommand request,
        CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.SourceProfiles.GetByIdAsync(request.Id, cancellationToken);
        if (profile == null)
            return Result<SourceProfileDto>.Failure($"No existe el perfil {request.Id}");

        try
        {
            profile.Update(
                request.SourceKey,
                request.DisplayName,
                request.PayoutPattern,
                Enum.Parse<InternalMatchField>(request.InternalMatchField, ignoreCase: true),
                request.InternalMatchPattern,
                request.ExpectedFeePercent,
                request.FeeTolerancePercent,
                request.GroupingWindowDays,
                request.IsActive);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<SourceProfileDto>.Success(SourceProfileDto.FromEntity(profile));
        }
        catch (DomainException ex)
        {
            return Result<SourceProfileDto>.Failure(ex.Message);
        }
    }
}

#endregion
