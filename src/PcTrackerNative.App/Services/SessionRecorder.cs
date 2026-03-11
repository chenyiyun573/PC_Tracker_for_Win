using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
    private readonly Channel<WriteWorkItem> _channel;
    private readonly StreamWriter _streamWriter;
    private readonly List<ActionRecord> _records = new();
    private readonly Task _writerTask;

    public SessionRecorder(string sessionFolder)
    {
        _sessionFolder = sessionFolder;
        _imagesFolder = Path.Combine(_sessionFolder, "images");
        _jsonlPath = Path.Combine(_sessionFolder, "trajectory.jsonl");

        Directory.CreateDirectory(_sessionFolder);
        Directory.CreateDirectory(_imagesFolder);

        _channel = Channel.CreateUnbounded<WriteWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _streamWriter = new StreamWriter(File.Open(_jsonlPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

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
        await _writerTask;
        await _streamWriter.FlushAsync();
        _streamWriter.Dispose();
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
                    var imageName = $"{step:D6}.png";
                    var imagePath = Path.Combine(_imagesFolder, imageName);
                    workItem.Screenshot.Save(imagePath, ImageFormat.Png);
                    record.ScreenshotPath = $"images/{imageName}";
                }

                _records.Add(record);
                await _streamWriter.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));
            }
            finally
            {
                workItem.Screenshot?.Dispose();
            }
        }
    }

    private sealed record WriteWorkItem(ActionRecordDescriptor Descriptor, Bitmap? Screenshot);
}
