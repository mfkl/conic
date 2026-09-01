namespace Player.Application;

/// <summary>Defines when secondary player chrome moves out of the primary dock.</summary>
public static class PlayerLayoutPolicy
{
    public const double CompactWidth = 760;
    public static PlayerLayoutResult ForWidth(double availableWidth) =>
        availableWidth < CompactWidth
            ? new(true, 2, 0, 3, 2)
            : new(false, 1, 2, 1, 8);
}

public sealed record PlayerLayoutResult(
    bool IsCompact,
    int RightRow,
    int RightColumn,
    int RightColumnSpan,
    double Spacing);
