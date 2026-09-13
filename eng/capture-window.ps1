#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int] $ProcessId,
    [Parameter(Mandatory)][string] $OutputFile,
    [switch] $Pause,
    [ValidateRange(400, 8192)][int] $Width = 1280,
    [ValidateRange(300, 8192)][int] $Height = 800
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native window capture requires Windows.' }
Add-Type -AssemblyName System.Drawing
if (-not ('ContestWindowCapture' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ContestWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    public delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static IntPtr Find(uint process) {
        IntPtr result = IntPtr.Zero;
        long largest = 0;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == process && IsWindowVisible(window) && GetWindowRect(window, out Rect rect)) {
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > largest) { largest = area; result = window; }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
}
Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
# Match physical capture pixels to window bounds on scaled displays.
# https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context
$previousDpi = [ContestWindowCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
$window = [ContestWindowCapture]::Find($ProcessId)
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ($window -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
    Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
    Start-Sleep -Milliseconds 250
    $window = [ContestWindowCapture]::Find($ProcessId)
}
if ($window -eq [IntPtr]::Zero) { throw 'No visible window belongs to that process.' }
if (-not [ContestWindowCapture]::SetWindowPos($window, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x0014)) { throw 'Cannot size the review window.' }
Start-Sleep -Milliseconds 500
if ($Pause) {
    [ContestWindowCapture]::PostMessage($window, 0x100, [IntPtr]0x20, [IntPtr]0) | Out-Null
    [ContestWindowCapture]::PostMessage($window, 0x101, [IntPtr]0x20, [IntPtr]0) | Out-Null
    Start-Sleep -Milliseconds 600
}
$rect = [ContestWindowCapture+Rect]::new()
if (-not [ContestWindowCapture]::GetWindowRect($window, [ref]$rect)) { throw 'Cannot measure the window.' }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 1 -or $height -lt 1 -or $width -gt 16384 -or $height -gt 16384) { throw 'Invalid capture dimensions.' }
$path = [IO.Path]::GetFullPath($OutputFile)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
$bitmap = [Drawing.Bitmap]::new($width, $height)
try {
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { if (-not [ContestWindowCapture]::PrintWindow($window, $dc, 2)) { throw 'Native window capture failed.' } }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
}
finally { $bitmap.Dispose() }
Write-Output "Captured ${width}x${height}: $path"
}
finally { if ($previousDpi -ne [IntPtr]::Zero) { [ContestWindowCapture]::SetThreadDpiAwarenessContext($previousDpi) | Out-Null } }
# Inspect the resulting PNG: a successful API call alone does not prove GPU video was captured.
