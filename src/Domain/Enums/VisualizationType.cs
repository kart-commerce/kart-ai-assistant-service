namespace Kart.AiAssistant.Domain.Enums;

/// <summary>Closed enum (genai-business-assistant-spec.md §13.2) — the wire value is the
/// lowercase snake_case form (see Api layer JSON converter), this is the in-process type.</summary>
public enum VisualizationType
{
    BarChart,
    HorizontalBarChart,
    LineChart,
    DonutChart,
    FunnelChart,
    SingleStat,
    TableOnly,
}
