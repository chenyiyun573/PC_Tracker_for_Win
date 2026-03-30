using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services;

public sealed class SessionRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _sessionFolder;
    private readonly string _imagesFolder;
    private readonly string _jsonlPath;
    private readonly string _markdownPath;
    private readonly Channel<WriteWorkItem> _channel;
    private readonly StreamWriter _jsonlWriter;
    private readonly StreamWriter _markdownWriter;
    private readonly List<ActionRecord> _records = new();
    private readonly Task _writerTask;

    public SessionRecorder(SessionContext context)
    {
        _sessionFolder = context.SessionFolder;
        _imagesFolder = Path.Combine(_sessionFolder, "images");
        _jsonlPath = Path.Combine(_sessionFolder, "trajectory.jsonl");
        _markdownPath = Path.Combine(_sessionFolder, "trajectory.md");

        Directory.CreateDirectory(_sessionFolder);
        Directory.CreateDirectory(_imagesFolder);

        _channel = Channel.CreateUnbounded<WriteWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _jsonlWriter = new StreamWriter(File.Open(_jsonlPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

        _markdownWriter = new StreamWriter(File.Open(_markdownPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

        _markdownWriter.Write(SessionArtifactWriter.BuildLiveMarkdownHeader(context));
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public void Enqueue(ActionRecordDescriptor descriptor, Bitmap? screenshot)
    {
        if (!_channel.Writer.TryWrite(new WriteWorkItem(descriptor, screenshot)))
        {
            screenshot?.Dispose();
            throw new InvalidOperationException("Recorder queue is not accepting more items.");
        }
    }

    public async Task CompleteAsync()
    {
        _channel.Writer.TryComplete();

        try
        {
            await _writerTask;
        }
        catch (Exception ex)
        {
            AppLogger.Error("The session writer loop failed during completion.", ex);
        }

        await SafeFlushAsync(_jsonlWriter, "jsonl");
        await SafeFlushAsync(_markdownWriter, "markdown");
        _jsonlWriter.Dispose();
        _markdownWriter.Dispose();
    }

    public SessionDraft BuildDraft(SessionContext context)
    {
        return new SessionDraft(context, _records.ToList());
    }

    private async Task WriterLoopAsync()
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync())
        {
            try
            {
                var step = _records.Count + 1;
                var record = new ActionRecord
                {
                    Step = step,
                    Timestamp = workItem.Descriptor.Timestamp,
                    Type = workItem.Descriptor.Type,
                    Description = workItem.Descriptor.Description,
                    X = workItem.Descriptor.X,
                    Y = workItem.Descriptor.Y,
                    WheelDelta = workItem.Descriptor.WheelDelta,
                    WaitSeconds = workItem.Descriptor.WaitSeconds,
                    TextValue = workItem.Descriptor.TextValue,
                    KeyChord = workItem.Descriptor.KeyChord,
                    MouseButton = workItem.Descriptor.MouseButton,
                    WindowTitle = workItem.Descriptor.WindowTitle,
                    ProcessName = workItem.Descriptor.ProcessName,
                };

                if (workItem.Screenshot is not null)
                {
                    try
                    {
                        var imageName = $"{step:D6}.png";
                        var imagePath = Path.Combine(_imagesFolder, imageName);
                        workItem.Screenshot.Save(imagePath, ImageFormat.Png);
                        record.ScreenshotPath = $"images/{imageName}";
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"Failed to save screenshot for step {step}.", ex);
                    }
                }

                _records.Add(record);

                try
                {
                    await _jsonlWriter.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to append JSONL for step {step}.", ex);
                }

                try
                {
                    await _markdownWriter.WriteAsync(SessionArtifactWriter.BuildLiveMarkdownEntry(record));
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to append markdown for step {step}.", ex);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unexpected exception inside the session writer loop.", ex);
            }
            finally
            {
                workItem.Screenshot?.Dispose();
            }
        }
    }

    private static async Task SafeFlushAsync(StreamWriter writer, string name)
    {
        try
        {
            await writer.FlushAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to flush {name} writer.", ex);
        }
    }

    private sealed record WriteWorkItem(ActionRecordDescriptor Descriptor, Bitmap? Screenshot);
}
