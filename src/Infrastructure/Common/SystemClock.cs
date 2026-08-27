using Kart.AiAssistant.Application.Common.Interfaces;

namespace Kart.AiAssistant.Infrastructure.Common;

/// <summary>The only place a bare <see cref="DateTimeOffset.UtcNow"/> call is allowed to
/// originate from in this service — every other call site depends on <see cref="IClock"/>
/// instead, so tests can pin time (IClock's own doc comment).</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
