using System.Security.Claims;
using Kart.AiAssistant.Application.Common.Registry;
using Kart.AiAssistant.Application.Features.SubmitQuery;
using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Infrastructure.Security;
using Kart.Shared.Domain;
using MediatR;

namespace Kart.AiAssistant.Api.Endpoints;

/// <summary>api-contract.yaml `POST /v1/ai-assistant/query` — the one inbound endpoint this
/// service exposes (ADR-0024). Reached only through the Gateway (never bypassed, per
/// architecture.md), which forwards the client's original JWT unchanged (ADR-0023); this
/// endpoint's own <see cref="AuthenticationExtensions.AiAssistantQueryPolicy"/> re-validates it
/// (FR-010's three-check model, check 2).</summary>
public static class AiAssistantEndpoints
{
    public static IEndpointRouteBuilder MapAiAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/ai-assistant/query", async (
                SubmitQueryRequest request,
                ClaimsPrincipal user,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var userId = user.FindFirstValue("sub") ?? "unknown";
                var role = ResolveRole(user);

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return Results.Problem(
                        title: "Invalid request",
                        detail: "message is required.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var command = new SubmitQueryCommand(request.ConversationId, request.Message, userId, role);
                var result = await sender.Send(command, cancellationToken);

                return result.IsSuccess
                    ? MapToHttpResult(result.Value)
                    : Results.Problem(
                        title: "Unable to process request",
                        detail: result.Error.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
            })
            .RequireAuthorization(AuthenticationExtensions.AiAssistantQueryPolicy)
            .RequireRateLimiting("ai-assistant-query")
            .WithName("SubmitAssistantQuery")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    /// <summary>Both the Gateway's coarse check and this service's own scope policy (check 2)
    /// already guarantee a "roles" claim of admin/support_agent is present by the time a request
    /// reaches this handler — this only picks the first matching one for the audit trail (§20),
    /// it is not itself an authorization decision.</summary>
    private static PlatformRole ResolveRole(ClaimsPrincipal user)
    {
        var roleClaims = user.FindAll("roles").Select(c => c.Value).ToList();
        if (roleClaims.Contains(PlatformRoleClaimValues.Admin))
        {
            return PlatformRole.Admin;
        }

        return PlatformRole.SupportAgent;
    }

    private static IResult MapToHttpResult(SubmitQueryResult result) => result.Kind switch
    {
        SubmitQueryResultKind.Answer => Results.Ok(new
        {
            conversationId = result.ConversationId,
            answer = result.Answer!.AnswerText,
            data = new
            {
                columns = result.Answer.Data.Columns,
                rows = result.Answer.Data.Rows,
            },
            visualization = new
            {
                type = result.Answer.Visualization.Type.ToWire(),
                title = result.Answer.Visualization.Title,
                xAxis = result.Answer.Visualization.XAxis,
                yAxis = result.Answer.Visualization.YAxis,
            },
            metadata = new
            {
                intent = result.Answer.Metadata.Intent.ToWireObject(),
                source = result.Answer.Metadata.Source,
                isProvisional = result.Answer.Metadata.IsProvisional,
                reconciledThrough = result.Answer.Metadata.ReconciledThrough,
                generatedAt = result.Answer.Metadata.GeneratedAt,
            },
        }),
        SubmitQueryResultKind.Clarification => Results.Ok(new
        {
            conversationId = result.ConversationId,
            clarification = new
            {
                question = result.Clarification!.Question,
                options = result.Clarification.Options,
            },
            data = (object?)null,
            visualization = (object?)null,
        }),
        SubmitQueryResultKind.Error => Results.Ok(new
        {
            conversationId = result.ConversationId,
            error = new
            {
                type = ToWireErrorType(result.Error!.Type),
                message = result.Error.Message,
            },
        }),
        _ => Results.Problem("Unhandled result kind.", statusCode: StatusCodes.Status500InternalServerError),
    };

    private static string ToWireErrorType(ResponseErrorType type) => type switch
    {
        ResponseErrorType.Unsupported => "unsupported",
        ResponseErrorType.Unavailable => "unavailable",
        ResponseErrorType.ExpiredSession => "expired-session",
        _ => "unavailable",
    };
}

/// <summary>api-contract.yaml request body — `conversationId` omitted starts a new conversation.</summary>
public sealed record SubmitQueryRequest(Guid? ConversationId, string Message);
