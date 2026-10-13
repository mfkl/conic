using System.Globalization;
using System.Runtime.CompilerServices;

namespace VLCAvalonia.UITests;

// Expected texts use en-US numbers ("1.5×", "8.0 dB"); the app shows the
// user's culture, so tests do not depend on the machine's region. Tests of
// another culture set and restore it themselves.
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize() =>
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = new CultureInfo("en-US");
}
