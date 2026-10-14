using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Player.Application;

namespace Conic.Views;

/// <summary>
/// How the Messages window colours a message. Each module keeps one of
/// <see cref="ModuleHues"/> hues, so a module reads the same on every line;
/// the core ("main") stays neutral and the app's own lines use the accent.
/// Quoted values in the text (module names, codecs, paths) stand out. The
/// colours are styles of <see cref="MessagesWindow"/> (Run.hue0 to hue5,
/// Run.value), one set per theme.
/// </summary>
internal static class MessageColors
{
    public const int ModuleHues = 6;

    /// <summary>The style class of <paramref name="module"/>'s name: core, app or hue0 to hue5.</summary>
    public static string ModuleClass(string module) => module switch
    {
        "main" or "" => "core",
        "conic" => "app",
        _ => $"hue{Hue(module)}",
    };

    // FNV-1a: the same hue for a module in every run, unlike string.GetHashCode.
    internal static int Hue(string module)
    {
        uint hash = 2166136261;
        foreach (char character in module) hash = (hash ^ character) * 16777619;
        return (int)(hash % ModuleHues);
    }

    /// <summary>The text split into plain parts and quoted values ("…" or '…'), quotes kept.</summary>
    public static IReadOnlyList<(string Text, bool IsValue)> Segments(string text)
    {
        List<(string, bool)> segments = [];
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char quote = text[index];
            if (quote is not ('"' or '\'')) continue;
            // An apostrophe inside a word (can't, VLC's) is not a quote.
            if (quote == '\'' && index > 0 && char.IsLetter(text[index - 1])) continue;
            int end = text.IndexOf(quote, index + 1);
            if (end < 0) break;
            if (index > start) segments.Add((text[start..index], false));
            segments.Add((text[index..(end + 1)], true));
            start = end + 1;
            index = end;
        }
        if (start < text.Length) segments.Add((text[start..], false));
        return segments;
    }

    /// <summary>"module type": the module in its colour, the object type dimmed.</summary>
    public static FuncValueConverter<EngineMessage?, InlineCollection?> Source { get; } = new(message =>
    {
        if (message is null) return null;
        Run module = new(message.Module);
        module.Classes.Add("module");
        module.Classes.Add(ModuleClass(message.Module));
        Run type = new(" " + message.ObjectType);
        type.Classes.Add("objectType");
        return [module, type];
    });

    /// <summary>The message text with its quoted values highlighted.</summary>
    public static FuncValueConverter<EngineMessage?, InlineCollection?> Text { get; } = new(message =>
    {
        if (message is null) return null;
        InlineCollection inlines = [];
        foreach ((string text, bool isValue) in Segments(message.Text))
        {
            Run run = new(text);
            if (isValue) run.Classes.Add("value");
            inlines.Add(run);
        }
        return inlines;
    });
}
