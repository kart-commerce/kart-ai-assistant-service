using FluentValidation;

namespace Kart.AiAssistant.Application.Features.SubmitQuery;

/// <summary>FR-001's own input bound ("UTF-8 text, ≤ 1,000 characters") plus §21.1's
/// <c>AssistantQueryRequest.message</c> schema (`minLength: 1, maxLength: 1000`) — enforced here so
/// an empty/oversized question never reaches NL→intent translation at all (§19: "do not call the
/// LLM for empty input").</summary>
public sealed class SubmitQueryCommandValidator : AbstractValidator<SubmitQueryCommand>
{
    public SubmitQueryCommandValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("A question is required.")
            .MaximumLength(1000).WithMessage("A question may not exceed 1,000 characters.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("An authenticated user id is required.");
    }
}
