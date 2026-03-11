namespace PcTrackerNative.App.Models;

public enum TrackingMode
{
    GivenTask,
    FreeTask,
    NonTask,
}

public enum SessionOutcome
{
    Saved,
    Failed,
    Discarded,
}

public enum TaskDifficulty
{
    Easy,
    Medium,
    Hard,
}

public sealed class TrackingOptions
{
    public int WaitIntervalSeconds { get; set; } = 6;

    public int CaptureFrameIntervalMs { get; set; } = 180;

    public bool PreferModernCapture { get; set; } = true;
}

public sealed class SessionContext
{
    public string SessionId { get; set; } = string.Empty;

    public string SessionFolder { get; set; } = string.Empty;

    public TrackingMode Mode { get; set; }

    public TaskItem? GivenTask { get; set; }

    public string CaptureBackend { get; set; } = string.Empty;

    public TrackingOptions Options { get; set; } = new();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset EndedAt { get; set; }
}

public sealed class SessionDraft
{
    public SessionDraft(SessionContext context, IReadOnlyList<ActionRecord> actions)
    {
        Context = context;
        Actions = actions;
    }

    public SessionContext Context { get; }

    public IReadOnlyList<ActionRecord> Actions { get; }
}

public sealed class SessionSaveInfo
{
    public SessionOutcome Outcome { get; set; } = SessionOutcome.Saved;

    public string FinalDescription { get; set; } = string.Empty;

    public TaskDifficulty Difficulty { get; set; } = TaskDifficulty.Medium;

    public string Notes { get; set; } = string.Empty;
}

public sealed class SessionMetadata
{
    public string SessionId { get; set; } = string.Empty;

    public TrackingMode Mode { get; set; }

    public SessionOutcome Outcome { get; set; }

    public string CaptureBackend { get; set; } = string.Empty;

    public int WaitIntervalSeconds { get; set; }

    public int CaptureFrameIntervalMs { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset EndedAt { get; set; }

    public int StepCount { get; set; }

    public string? TaskCategory { get; set; }

    public string? OriginalTaskDescription { get; set; }

    public string FinalDescription { get; set; } = string.Empty;

    public TaskDifficulty Difficulty { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string JsonlPath { get; set; } = "trajectory.jsonl";

    public string MarkdownPath { get; set; } = "trajectory.md";
}
