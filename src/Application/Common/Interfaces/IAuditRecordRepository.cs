using Kart.AiAssistant.Domain.AuditRecords;

namespace Kart.AiAssistant.Application.Common.Interfaces;

/// <summary>Append-only (FR-011) — Postgres-backed (database-design.md), this service's own
/// dedicated table, not a generic Kart.Shared.Auditing writer (that package's AuditLogEntry shape
/// is too narrow for this service's rich per-turn schema — see database-design.md's own note).</summary>
public interface IAuditRecordRepository
{
    Task AddAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <summary>Testability seam — every timestamp in this service flows through this, never a bare
/// `DateTimeOffset.UtcNow` call, so tests can pin time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
