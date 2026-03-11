using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;

namespace PcTrackerNative.App.Services.Capture;

internal static class FrameExtensions
{
    public static Bitmap ToClonedBitmap(this Direct3D11CaptureFrame frame)
    {
        using var sourceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
        var d3dDevice = sourceTexture.Device;

        using var stagingTexture = new SharpDX.Direct3D11.Texture2D(d3dDevice, new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = frame.ContentSize.Width,
            Height = frame.ContentSize.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = sourceTexture.Description.Format,
            Usage = SharpDX.Direct3D11.ResourceUsage.Staging,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            BindFlags = SharpDX.Direct3D11.BindFlags.None,
            CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read,
            OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None,
        });

        d3dDevice.ImmediateContext.CopyResource(sourceTexture, stagingTexture);
        var dataBox = d3dDevice.ImmediateContext.MapSubresource(stagingTexture, 0, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var stream);
        try
        {
            using var temporaryBitmap = new Bitmap(stagingTexture.Description.Width, stagingTexture.Description.Height, dataBox.RowPitch, PixelFormat.Format32bppArgb, dataBox.DataPointer);
            return temporaryBitmap.Clone(new Rectangle(0, 0, temporaryBitmap.Width, temporaryBitmap.Height), PixelFormat.Format32bppArgb);
        }
        finally
        {
            stream?.Dispose();
            d3dDevice.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }
    }
}
