using System.Text.RegularExpressions;
using Kart.AiAssistant.Domain.Shared;

namespace Kart.AiAssistant.Infrastructure.ModelGateway;

/// <summary>
/// Resolves a date phrase inside a user's message to an explicit <see cref="DateRange"/> — every
/// relative phrase is turned into explicit UTC instants at this step, never left as text past
/// planning (<see cref="DateRange"/>'s own doc comment). Distinguishes three outcomes, per
/// §15.1/§26-UX-2:
/// <list type="bullet">
/// <item><see cref="DateResolutionKind.Resolved"/> — a concrete, deterministically-resolvable phrase
/// ("last 7 days," "this week," "last month," "today," an explicit ISO date).</item>
/// <item><see cref="DateResolutionKind.Unresolvable"/> — a *stated* but vague phrase ("recently,"
/// "lately") that must trigger a clarification, never a silent default.</item>
/// <item><see cref="DateResolutionKind.Absent"/> — no date phrase at all, which callers may default
/// (e.g. to a rolling last-7-days window, §26-UX-2's own stated default) — the one case silently
/// defaulting is correct.</item>
/// </list>
/// </summary>
public static class DatePhraseResolver
{
    private static readonly Regex LastNDays = new(@"\blast\s+(\d+)\s+day", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Today = new(@"\btoday\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThisWeek = new(@"\bthis\s+week\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LastWeek = new(@"\blast\s+week\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThisMonth = new(@"\bthis\s+month\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LastMonth = new(@"\blast\s+month\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LastNWeeks = new(@"\blast\s+(\d+)\s+week", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LastNMonths = new(@"\blast\s+(\d+)\s+month", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IsoDate = new(@"\b(\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex Unresolvable = new(@"\b(recently|lately)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DateResolution Resolve(string message, DateTimeOffset now)
    {
        var isoMatches = IsoDate.Matches(message);
        if (isoMatches.Count >= 2)
        {
            var from = DateTimeOffset.Parse(isoMatches[0].Groups[1].Value + "T00:00:00Z");
            var to = DateTimeOffset.Parse(isoMatches[1].Groups[1].Value + "T00:00:00Z");
            return from < to
                ? new DateResolution(DateResolutionKind.Resolved, new DateRange(from, to))
                : new DateResolution(DateResolutionKind.Resolved, new DateRange(to, from));
        }

        if (isoMatches.Count == 1)
        {
            var from = DateTimeOffset.Parse(isoMatches[0].Groups[1].Value + "T00:00:00Z");
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(from, now));
        }

        var lastNDaysMatch = LastNDays.Match(message);
        if (lastNDaysMatch.Success)
        {
            var days = int.Parse(lastNDaysMatch.Groups[1].Value);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(now.AddDays(-days), now));
        }

        var lastNWeeksMatch = LastNWeeks.Match(message);
        if (lastNWeeksMatch.Success)
        {
            var weeks = int.Parse(lastNWeeksMatch.Groups[1].Value);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(now.AddDays(-7 * weeks), now));
        }

        var lastNMonthsMatch = LastNMonths.Match(message);
        if (lastNMonthsMatch.Success)
        {
            var months = int.Parse(lastNMonthsMatch.Groups[1].Value);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(now.AddMonths(-months), now));
        }

        if (Today.IsMatch(message))
        {
            var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(startOfDay, now));
        }

        if (LastWeek.IsMatch(message))
        {
            var mondayThisWeek = StartOfWeek(now);
            var mondayLastWeek = mondayThisWeek.AddDays(-7);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(mondayLastWeek, mondayThisWeek));
        }

        if (ThisWeek.IsMatch(message))
        {
            var mondayThisWeek = StartOfWeek(now);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(mondayThisWeek, now));
        }

        if (LastMonth.IsMatch(message))
        {
            var startOfThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            var startOfLastMonth = startOfThisMonth.AddMonths(-1);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(startOfLastMonth, startOfThisMonth));
        }

        if (ThisMonth.IsMatch(message))
        {
            var startOfThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            return new DateResolution(DateResolutionKind.Resolved, new DateRange(startOfThisMonth, now));
        }

        if (Unresolvable.IsMatch(message))
        {
            return new DateResolution(DateResolutionKind.Unresolvable, null);
        }

        return new DateResolution(DateResolutionKind.Absent, null);
    }

    /// <summary>§26-UX-2's own assumed default for a totally-absent date phrase.</summary>
    public static DateRange DefaultRollingWindow(DateTimeOffset now, int days = 7) => new(now.AddDays(-days), now);

    /// <summary>Monday-start calendar week containing <paramref name="now"/> (ISO-8601 week start).</summary>
    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var diff = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var date = now.Date.AddDays(-diff);
        return new DateTimeOffset(date, now.Offset);
    }
}

public enum DateResolutionKind
{
    Resolved,
    Unresolvable,
    Absent,
}

public sealed record DateResolution(DateResolutionKind Kind, DateRange? Range);
