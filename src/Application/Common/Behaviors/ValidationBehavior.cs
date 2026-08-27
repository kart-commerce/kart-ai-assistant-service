using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Kart.AiAssistant.Application.Common.Behaviors;

/// <summary>
/// Runs every registered `AbstractValidator&lt;TRequest&gt;` (e.g.
/// <c>SubmitQueryCommandValidator</c>) before the handler — mirrors every other Kart service's own
/// `ValidationBehavior` (`kart-order-service`, `kart-payment-service`, `kart-inventory-service`,
/// `kart-category-service`): throws FluentValidation's own <see cref="ValidationException"/>,
/// which `Kart.Shared.ErrorHandling.KartExceptionHandler` special-cases to a `400` with a per-field
/// error map at the Api layer. Without this behavior registered, `AddValidatorsFromAssembly`
/// merely makes the validators resolvable — nothing in the MediatR pipeline would ever invoke them.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
        {
            var requestName = typeof(TRequest).Name;

            logger.LogWarning(
                "Stage {Stage}: {RequestName} rejected — {Errors}",
                $"{requestName}ValidationFailed",
                requestName,
                string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

            throw new ValidationException(failures);
        }

        return await next();
    }
}
