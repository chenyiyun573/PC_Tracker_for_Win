using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services;

public static class SessionArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync(SessionDraft draft, SessionSaveInfo saveInfo)
    {
        var metadata = BuildMetadata(draft, saveInfo);
        var sessionJsonPath = Path.Combine(draft.Context.SessionFolder, "session.json");
        var markdownPath = Path.Combine(draft.Context.SessionFolder, "trajectory.md");

        await File.WriteAllTextAsync(sessionJsonPath, JsonSerializer.Serialize(metadata, JsonOptions));
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(draft, metadata), Encoding.UTF8);
    }

    public static Task DiscardAsync(SessionDraft draft)
    {
        if (Directory.Exists(draft.Context.SessionFolder))
        {
            Directory.Delete(draft.Context.SessionFolder, true);
        }

        return Task.CompletedTask;
    }

    private static SessionMetadata BuildMetadata(SessionDraft draft, SessionSaveInfo saveInfo)
    {
        return new SessionMetadata
        {
            SessionId = draft.Context.SessionId,
            Mode = draft.Context.Mode,
            Outcome = saveInfo.Outcome,
            CaptureBackend = draft.Context.CaptureBackend,
            WaitIntervalSeconds = draft.Context.Options.WaitIntervalSeconds,
            CaptureFrameIntervalMs = draft.Context.Options.CaptureFrameIntervalMs,
            StartedAt = draft.Context.StartedAt,
            EndedAt = draft.Context.EndedAt,
            StepCount = draft.Actions.Count,
            TaskCategory = draft.Context.GivenTask?.Category,
            OriginalTaskDescription = draft.Context.GivenTask?.Description,
            FinalDescription = saveInfo.FinalDescription,
            Difficulty = saveInfo.Difficulty,
            Notes = saveInfo.Notes,
        };
    }

    private static string BuildMarkdown(SessionDraft draft, SessionMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PC Tracker Session");
        builder.AppendLine();
        builder.AppendLine($"- **Session ID:** `{metadata.SessionId}`");
        builder.AppendLine($"- **Mode:** `{metadata.Mode}`");
        builder.AppendLine($"- **Outcome:** `{metadata.Outcome}`");
        builder.AppendLine($"- **Capture backend:** `{metadata.CaptureBackend}`");
        builder.AppendLine($"- **Started:** {metadata.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- **Ended:** {metadata.EndedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- **Wait interval:** {metadata.WaitIntervalSeconds}s");
        builder.AppendLine($"- **Capture frame interval:** {metadata.CaptureFrameIntervalMs} ms");
        if (!string.IsNullOrWhiteSpace(metadata.TaskCategory))
        {
            builder.AppendLine($"- **Task category:** {Escape(metadata.TaskCategory)}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.OriginalTaskDescription))
        {
            builder.AppendLine($"- **Original task:** {Escape(metadata.OriginalTaskDescription)}");
        }

        builder.AppendLine($"- **Final description:** {Escape(metadata.FinalDescription)}");
        builder.AppendLine($"- **Difficulty:** `{metadata.Difficulty}`");
        if (!string.IsNullOrWhiteSpace(metadata.Notes))
        {
            builder.AppendLine($"- **Notes:** {Escape(metadata.Notes)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Timeline");
        builder.AppendLine();

        foreach (var action in draft.Actions)
        {
            builder.AppendLine($"### {action.Step:D4} - {action.Timestamp:HH:mm:ss.fff}");
            builder.AppendLine();
            builder.AppendLine($"- **Action:** {Escape(action.Description)}");
            if (!string.IsNullOrWhiteSpace(action.WindowTitle))
            {
                builder.AppendLine($"- **Window:** {Escape(action.WindowTitle)}");
            }

            if (!string.IsNullOrWhiteSpace(action.ProcessName))
            {
                builder.AppendLine($"- **Process:** `{EscapeInline(action.ProcessName)}`");
            }

            if (action.X.HasValue && action.Y.HasValue)
            {
                builder.AppendLine($"- **Pointer:** ({action.X.Value}, {action.Y.Value})");
            }

            if (action.WheelDelta.HasValue)
            {
                builder.AppendLine($"- **Wheel delta:** {action.WheelDelta.Value}");
            }

            if (!string.IsNullOrWhiteSpace(action.KeyChord))
            {
                builder.AppendLine($"- **Keys:** `{EscapeInline(action.KeyChord)}`");
            }

            if (!string.IsNullOrWhiteSpace(action.TextValue))
            {
                builder.AppendLine($"- **Typed text:** `{EscapeInline(action.TextValue.Replace("\n", "↵").Replace("\r", string.Empty).Replace("\t", "⇥"))}`");
            }

            if (action.WaitSeconds.HasValue)
            {
                builder.AppendLine($"- **Waited:** {action.WaitSeconds.Value:0.##} seconds");
            }

            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(action.ScreenshotPath))
            {
                builder.AppendLine($"![step {action.Step:D4}]({action.ScreenshotPath})");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", string.Empty).Replace("\n", "<br/>");
    }

    private static string EscapeInline(string value)
    {
        return value.Replace("`", "'");
    }
}
