using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Conic.Services;

namespace Conic.Controls;

public sealed class MediaThumbnail : UserControl
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<MediaThumbnail, string?>(nameof(Source));
    public static readonly StyledProperty<double> PlaceholderSizeProperty = AvaloniaProperty.Register<MediaThumbnail, double>(nameof(PlaceholderSize), 48);
    // Extensions shown with the music placeholder while there is no artwork.
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".alac", ".ape", ".mka", ".mid", ".midi" };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly VlcIcon _placeholder = new();
    private IDisposable? _placeholderIcon;
    private CancellationTokenSource? _loading;
    private Bitmap? _bitmap;
    private bool _attached;
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public double PlaceholderSize { get => GetValue(PlaceholderSizeProperty); set => SetValue(PlaceholderSizeProperty, value); }
    internal bool HasThumbnail => _bitmap is not null;

    public MediaThumbnail()
    {
        ClipToBounds = true;
        _placeholder.Bind(WidthProperty, this.GetObservable(PlaceholderSizeProperty));
        _placeholder.Bind(HeightProperty, this.GetObservable(PlaceholderSizeProperty));
        UpdatePlaceholderIcon();
        _placeholder.Bind(VlcIcon.ForegroundProperty, _placeholder.GetResourceObservable("PlayerSecondaryText"));
        Content = new Grid { Children = { _placeholder, _image } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _attached = true; Reload(); }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _attached = false; Clear(); base.OnDetachedFromVisualTree(e); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SourceProperty) return;
        UpdatePlaceholderIcon();
        if (_attached) Reload();
    }

    /// <summary>True when the source's extension is a common audio format.</summary>
    internal static bool LooksLikeAudio(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        string path = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? uri.AbsolutePath : source;
        return AudioExtensions.Contains(Path.GetExtension(path));
    }

    private void UpdatePlaceholderIcon()
    {
        _placeholderIcon?.Dispose();
        _placeholderIcon = _placeholder.Bind(VlcIcon.IconProperty, _placeholder.GetResourceObservable(LooksLikeAudio(Source) ? "Icon.Music" : "Icon.Play"));
    }

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
