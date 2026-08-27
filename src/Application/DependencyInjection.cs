using FluentValidation;
using Kart.AiAssistant.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Kart.AiAssistant.Application;

/// <summary>Application-layer composition root — registers every MediatR handler/validator
/// discovered in this assembly. Referenced once from the Infrastructure/Api composition root
/// (`Program.cs`), never re-registered per-feature.</summary>
public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationDependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
