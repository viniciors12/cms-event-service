namespace CmsEventService.Api.Application;

public enum EventOutcome
{
    /// <summary>The event changed the stored state.</summary>
    Applied,

    /// <summary>Valid, but obsolete (older version or timestamp); safe to drop.</summary>
    Ignored,

    /// <summary>Failed validation.</summary>
    Rejected,

    /// <summary>Valid, but processing threw; the CMS may retry.</summary>
    Failed
}

public sealed record EventResult(int Index, string? Id, string? Type, EventOutcome Outcome, string? Reason);

/// <summary>Per-event results of one batch, with the totals derived from them.</summary>
public sealed record BatchResult(IReadOnlyList<EventResult> Results)
{
    public int Total => Results.Count;

    public int Applied => Count(EventOutcome.Applied);

    public int Ignored => Count(EventOutcome.Ignored);

    public int Rejected => Count(EventOutcome.Rejected);

    public int Failed => Count(EventOutcome.Failed);

    private int Count(EventOutcome outcome) => Results.Count(r => r.Outcome == outcome);
}
