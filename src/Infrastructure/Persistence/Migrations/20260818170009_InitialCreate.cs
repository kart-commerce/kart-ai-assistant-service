using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kart.AiAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_assistant_audit_records",
                columns: table => new
                {
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    user_question = table.Column<string>(type: "text", nullable: false),
                    resolved_intent = table.Column<string>(type: "jsonb", nullable: true),
                    clarification_issued = table.Column<bool>(type: "boolean", nullable: false),
                    clarification_question = table.Column<string>(type: "text", nullable: true),
                    tools_invoked = table.Column<string>(type: "jsonb", nullable: true),
                    data_source = table.Column<string>(type: "text", nullable: true),
                    query_parameters = table.Column<string>(type: "jsonb", nullable: true),
                    execution_time_ms = table.Column<long>(type: "bigint", nullable: true),
                    llm_plan_latency_ms = table.Column<long>(type: "bigint", nullable: true),
                    llm_explain_latency_ms = table.Column<long>(type: "bigint", nullable: true),
                    llm_token_usage = table.Column<string>(type: "jsonb", nullable: true),
                    result_size = table.Column<int>(type: "integer", nullable: true),
                    is_provisional = table.Column<bool>(type: "boolean", nullable: true),
                    reconciled_through = table.Column<DateOnly>(type: "date", nullable: true),
                    grounding_check_result = table.Column<string>(type: "text", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: true),
                    final_response_summary = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_assistant_audit_records", x => x.turn_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_ai_assistant_audit_records_conversation",
                table: "ai_assistant_audit_records",
                columns: new[] { "conversation_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_ai_assistant_audit_records_timestamp",
                table: "ai_assistant_audit_records",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_ai_assistant_audit_records_user",
                table: "ai_assistant_audit_records",
                columns: new[] { "user_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_assistant_audit_records");
        }
    }
}
