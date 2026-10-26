namespace TheSingularityWorkshop.CiAi;

/// <summary>Finite lifecycle states understood by CiAi.</summary>
public enum CiRunStatus
{
    Unknown = 0,
    Queued = 1,
    InProgress = 2,
    Completed = 3,
    Pending = 4
}

/// <summary>Finite conclusions understood by CiAi. None is valid only before completion.</summary>
public enum CiRunConclusion
{
    None = 0,
    Success = 1,
    Failure = 2,
    Cancelled = 3,
    TimedOut = 4,
    ActionRequired = 5,
    Neutral = 6,
    Skipped = 7,
    Stale = 8,
    Unknown = 9
}
