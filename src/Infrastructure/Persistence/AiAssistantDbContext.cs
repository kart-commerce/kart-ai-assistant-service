using Kart.AiAssistant.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kart.AiAssistant.Infrastructure.Persistence;

/// <summary>
/// Owns exactly the one local write-model table this service's database-design.md allows —
/// <c>ai_assistant_audit_records</c> (FR-011, one row per turn, append-only). No second table:
/// <c>ConversationSession</c> is Redis-backed, never Postgres (design-decisions.md's "Conversation-
/// Session Storage" decision).
/// </summary>
public sealed class AiAssistantDbContext : DbContext
{
    public DbSet<AuditRecordRow> AuditRecords => Set<AuditRecordRow>();

    public AiAssistantDbContext(DbContextOptions<AiAssistantDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditRecordRow>(builder =>
        {
            builder.ToTable("ai_assistant_audit_records");

            builder.HasKey(row => row.TurnId);
            builder.Property(row => row.TurnId).HasColumnName("turn_id").ValueGeneratedNever();

            builder.Property(row => row.ConversationId).HasColumnName("conversation_id").IsRequired();
            builder.Property(row => row.UserId).HasColumnName("user_id").IsRequired();

            builder.Property(row => row.Role).HasColumnName("role").HasConversion<string>().IsRequired();

            builder.Property(row => row.UserQuestion).HasColumnName("user_question").IsRequired();

            // JSON columns (database-design.md's AuditRecord shape) — jsonb, not a Postgres TEXT[]
            // or a CHECK-constrained scalar, since each already carries its own nested structure.
            builder.Property(row => row.ResolvedIntentJson).HasColumnName("resolved_intent").HasColumnType("jsonb");
            builder.Property(row => row.ToolsInvokedJson).HasColumnName("tools_invoked").HasColumnType("jsonb");
            builder.Property(row => row.QueryParametersJson).HasColumnName("query_parameters").HasColumnType("jsonb");
            builder.Property(row => row.LlmTokenUsageJson).HasColumnName("llm_token_usage").HasColumnType("jsonb");
            builder.Property(row => row.ErrorsJson).HasColumnName("errors").HasColumnType("jsonb");

            builder.Property(row => row.ClarificationIssued).HasColumnName("clarification_issued").IsRequired();
            builder.Property(row => row.ClarificationQuestion).HasColumnName("clarification_question");

            builder.Property(row => row.DataSource).HasColumnName("data_source");

            builder.Property(row => row.ExecutionTimeMs).HasColumnName("execution_time_ms");
            builder.Property(row => row.LlmPlanLatencyMs).HasColumnName("llm_plan_latency_ms");
            builder.Property(row => row.LlmExplainLatencyMs).HasColumnName("llm_explain_latency_ms");

            builder.Property(row => row.ResultSize).HasColumnName("result_size");
            builder.Property(row => row.IsProvisional).HasColumnName("is_provisional");
            builder.Property(row => row.ReconciledThrough).HasColumnName("reconciled_through").HasColumnType("date");

            // FR-009/§19's plan-vs-explain-vs-not-applicable distinction (Domain/Enums/ResponseErrorType.cs's
            // own doc comment) — stored as its string name, not an int, so the column stays readable
            // for an ad-hoc compliance query without a lookup table.
            builder.Property(row => row.GroundingCheckResult).HasColumnName("grounding_check_result").HasConversion<string>().IsRequired();

            builder.Property(row => row.FinalResponseSummary).HasColumnName("final_response_summary");

            builder.Property(row => row.Timestamp).HasColumnName("timestamp").IsRequired();

            // "Reconstruct this conversation's full transcript, in order" (ddd-model.md).
            builder.HasIndex(row => new { row.ConversationId, row.Timestamp })
                .HasDatabaseName("idx_ai_assistant_audit_records_conversation");

            // "What did this user ask, over what window" — FR-011's compliance-review pattern.
            builder.HasIndex(row => new { row.UserId, row.Timestamp })
                .HasDatabaseName("idx_ai_assistant_audit_records_user");

            // FR-011 states userId and date-range as two independently queryable dimensions — the
            // (user_id, timestamp) index above cannot serve a userId-agnostic range scan on its own
            // (user_id is its leading column), so date-range gets its own standalone index
            // (database-design.md's self-critiqued gap-closure).
            builder.HasIndex(row => row.Timestamp)
                .HasDatabaseName("idx_ai_assistant_audit_records_timestamp");
        });
    }
}
