namespace CmsEventService.Api.Application;

/// <summary>The result of processing one event.</summary>
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

/// <summary>The outcome of one event in a batch.</summary>
public sealed record EventResult(int Index, string? Id, string? Type, EventOutcome Outcome, string? Reason);

/// <summary>Per-event results of one batch, with the totals derived from them.</summary>
public sealed record BatchResult(IReadOnlyList<EventResult> Results)
{
    /// <summary>Number of events in the batch.</summary>
    public int Total => Results.Count;

    /// <summary>Events that changed the stored state.</summary>
    public int Applied => Count(EventOutcome.Applied);

    /// <summary>Valid events that were obsolete and dropped.</summary>
    public int Ignored => Count(EventOutcome.Ignored);

    /// <summary>Events that failed validation.</summary>
    public int Rejected => Count(EventOutcome.Rejected);

    /// <summary>Valid events whose processing threw.</summary>
    public int Failed => Count(EventOutcome.Failed);

    private int Count(EventOutcome outcome) => Results.Count(r => r.Outcome == outcome);
}
