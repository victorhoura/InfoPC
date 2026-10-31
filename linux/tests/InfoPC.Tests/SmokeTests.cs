using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using InfoPC.Linux;
using Xunit;

namespace InfoPC.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void MainWindow_renders()
    {
        var w = new MainWindow();
        w.Show();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame!.PixelSize.Width > 0);
    }
}
