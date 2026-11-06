using Compositor.Core.IO;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class WindowPlacementTests
{
    private static readonly WindowPlacement.Area[] Screens = [new(0, 0, 1920, 1040), new(1920, 0, 2560, 1400)];

    [Theory]
    [InlineData(100, 100, 1200, true)]
    [InlineData(2100, 200, 1200, true)]
    [InlineData(1850, 40, 1200, true)]
    [InlineData(1900, 2000, 1200, false)]
    [InlineData(-1300, 40, 1200, false)]
    [InlineData(5000, 40, 1200, false)]
    [InlineData(100, -300, 1200, false)]
    public void WindowComesBackOnlyWhereItCanBeReached(double x, double y, double width, bool expected) =>
        Assert.Equal(expected, WindowPlacement.IsOnScreen(x, y, width, Screens));

    [Fact]
    public void WindowPlaceIsSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new ToolDefaults { WindowWidth = 1200, WindowHeight = 800, WindowX = -40, WindowY = 12, WindowMaximized = true }.Save(path);
            var read = ToolDefaults.Load(path);
            Assert.Equal((1200d, 800d, -40d, 12d, true), (read.WindowWidth, read.WindowHeight, read.WindowX, read.WindowY, read.WindowMaximized));
        }
        finally { File.Delete(path); }
    }
}