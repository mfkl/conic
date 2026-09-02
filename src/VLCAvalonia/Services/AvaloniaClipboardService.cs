using Avalonia.Input.Platform;
using Player.Application;

namespace VLCAvalonia.Services;

internal sealed class AvaloniaClipboardService(Func<IClipboard?> clipboard) : IClipboardService
{
    public async Task<string?> SetTextAsync(string text)
    {
        IClipboard? target = clipboard();
        if (target is null) return "clipboard unavailable";
        try { await target.SetTextAsync(text); return null; }
        catch (Exception exception) { return string.IsNullOrWhiteSpace(exception.Message) ? "clipboard write failed" : exception.Message; }
    }
}
