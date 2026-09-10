using Avalonia;
using Player.Application;
using VLCAvalonia.Controls;
using VLCAvalonia.Interop;
using VLCAvalonia.ViewModels;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class VideoAdjustmentsTests
{
    [Theory]
    [InlineData(1, 960, 720)]
    [InlineData(4d / 3, 1280, 720)]
    public void FitHonorsSampleAspect(double sar, double width, double height)
    {
        var layout = VideoSurfaceLayout.Calculate(new(1280, 720), new(640, 480), new() { Active = 1, Width = 1, Height = 1, SampleAspect = sar, AutoScale = 1 });
        Assert.Equal(width, layout.Destination.Width, 6);
        Assert.Equal(height, layout.Destination.Height, 6);
    }
    [Fact]
    public void CenterCropUsesSamePixelRegionForCpuAndGpu()
    {
        var layout = VideoSurfaceLayout.Calculate(new(1280, 720), new(640, 480), new() { Active = 1, X = .125, Width = .75, Height = 1, SampleAspect = 1, AutoScale = 1 });
        Assert.Equal(new Rect(80, 0, 480, 480), layout.Source);
        Assert.Equal(new Rect(280, 0, 720, 720), layout.Destination);
        Assert.Equal(new Rect(-120, 0, 960, 720), layout.FullImage);
    }
    [Theory]
    [InlineData(1, 320, 240)]
    [InlineData(2, 160, 120)]
    public void FixedZoomUsesPhysicalPixels(double dpi, double width, double height)
    {
        var layout = VideoSurfaceLayout.Calculate(new(1280, 720), new(640, 480), new() { Active = 1, Width = 1, Height = 1, SampleAspect = 1, Zoom = .5 }, dpi);
        Assert.Equal(width, layout.Destination.Width);
        Assert.Equal(height, layout.Destination.Height);
    }
    [Fact]
    public void LargerThanWindowZoomRemainsCenteredForClipping()
    {
        var layout = VideoSurfaceLayout.Calculate(new(320, 180), new(640, 480), new() { Active = 1, Width = 1, Height = 1, SampleAspect = 1, Zoom = 2 });
        Assert.Equal(new Rect(-480, -390, 1280, 960), layout.Destination);
    }
    [Fact]
    public void SelectionUsesSnapshotAndWaitsForReadback()
    {
        var transport = new Transport(); var vm = new VideoAdjustmentsViewModel(transport);
        vm.Aspect = new("bogus", "Bogus");
        Assert.Empty(transport.Requests);
        vm.Aspect = vm.Aspects[1];
        Assert.Equal("4:3", Assert.Single(transport.Requests).Value);
        Assert.Equal("", vm.Aspect!.Value);
        Assert.Equal(transport.VideoAdjustments, transport.Requests[0].State);
        vm.Apply(transport.VideoAdjustments with { Aspect = "4:3" });
        Assert.Equal("4:3", vm.Aspect!.Value);
    }
    [Fact]
    public void StaleReadbackCannotRestorePreviousInputAndStopClearsChoices()
    {
        var transport = new Transport(); var vm = new VideoAdjustmentsViewModel(transport);
        vm.Apply(transport.VideoAdjustments with { MediaGeneration = 2, Aspect = "4:3" });
        vm.Apply(transport.VideoAdjustments);
        Assert.Equal("4:3", vm.Aspect!.Value);
        vm.Apply(VideoAdjustmentState.Unavailable with { QueueGeneration = 2 });
        vm.Apply(transport.VideoAdjustments);
        vm.Reset();
        Assert.False(vm.Available);
        Assert.Empty(transport.Requests);
    }
    [Fact]
    public void BindingUpdatesDoNotDispatchMutations()
    {
        var transport = new Transport(); var vm = new VideoAdjustmentsViewModel(transport);
        vm.PropertyChanged += (_, _) => vm.Aspect = vm.Aspects.LastOrDefault();
        vm.Apply(transport.VideoAdjustments);
        Assert.Empty(transport.Requests);
    }
    private sealed class Transport : IVideoAdjustmentTransport
    {
        public VideoAdjustmentState VideoAdjustments { get; } = new(1, 1, 42, [new("", "Default"), new("4:3", "4:3")], [new("", "Default")], [new("fit", "Fit")], "", "", "fit");
        public event EventHandler<VideoAdjustmentState>? VideoAdjustmentsChanged { add { } remove { } }
        public List<(VideoAdjustmentState State, string Setting, string Value)> Requests { get; } = [];
        public void SetVideoAdjustment(VideoAdjustmentState expected, string setting, string value) => Requests.Add((expected, setting, value));
    }
}
