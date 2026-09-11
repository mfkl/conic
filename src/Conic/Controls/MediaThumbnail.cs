using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Conic.Services;

namespace Conic.Controls;

public sealed class MediaThumbnail : UserControl
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<MediaThumbnail, string?>(nameof(Source));
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly PathIcon _placeholder = new()
    {
        Width = 32, Height = 32, Foreground = new SolidColorBrush(Color.Parse("#999999")),
        Data = Geometry.Parse("M7,3 L22,12 L7,21 Z")
    };
    private CancellationTokenSource? _loading;
    private Bitmap? _bitmap;
    private bool _attached;
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    internal bool HasThumbnail => _bitmap is not null;

    public MediaThumbnail()
    {
        ClipToBounds = true;
        Content = new Grid { Children = { _placeholder, _image } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _attached = true; Reload(); }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _attached = false; Clear(); base.OnDetachedFromVisualTree(e); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == SourceProperty && _attached) Reload(); }

    private void Clear()
    {
        _loading?.Cancel(); _loading?.Dispose(); _loading = null;
        _image.Source = null;
        _bitmap?.Dispose(); _bitmap = null;
        _placeholder.IsVisible = true;
    }

    private async void Reload()
    {
        Clear();
        if (string.IsNullOrWhiteSpace(Source)) return;
        var loading = _loading = new CancellationTokenSource();
        try
        {
            byte[]? bytes = await MediaThumbnailCache.Shared.GetAsync(Source, loading.Token);
            if (!_attached || !ReferenceEquals(_loading, loading) || bytes is null) return;
            using MemoryStream stream = new(bytes, writable: false);
            _bitmap = new Bitmap(stream);
            _image.Source = _bitmap;
            _placeholder.IsVisible = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Keep the fallback if a cached image cannot be decoded. */ }
    }
}
