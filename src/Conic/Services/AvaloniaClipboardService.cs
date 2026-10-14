using Avalonia.Input.Platform;
using Player.Application;

namespace Conic.Services;

internal sealed class AvaloniaClipboardService(Func<IClipboard?> clipboard) : IClipboardService
{
    public async Task<string?> SetTextAsync(string text)
    {
        IClipboard? target = clipboard();
        if (target is null) return "clipboard unavailable";
        try { await target.SetTextAsync(text); return null; }
        catch (Exception exception) { return string.IsNullOrWhiteSpace(exception.Message) ? "clipboard write failed" : exception.Message; }
    }

    public async Task<string?> GetTextAsync()
    {
        IClipboard? source = clipboard();
        if (source is null) return null;
        try { return await source.TryGetTextAsync(); }
        catch (Exception) { return null; }
    }
}
