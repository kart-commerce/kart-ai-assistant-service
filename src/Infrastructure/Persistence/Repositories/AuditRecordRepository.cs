using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Domain.AuditRecords;

namespace Kart.AiAssistant.Infrastructure.Persistence.Repositories;

/// <summary>Append-only (FR-011) — one row inserted per turn, never updated afterward.</summary>
public sealed class AuditRecordRepository : IAuditRecordRepository
{
    private readonly AiAssistantDbContext _dbContext;

    public AuditRecordRepository(AiAssistantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var row = AuditRecordRowMapper.ToRow(record);
        _dbContext.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
