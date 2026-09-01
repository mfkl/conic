namespace Player.Application;

/// <summary>Defines when secondary player chrome moves out of the primary dock.</summary>
public static class PlayerLayoutPolicy
{
    public const double CompactWidth = 760;
    public static bool IsCompact(double availableWidth) => availableWidth < CompactWidth;
}
