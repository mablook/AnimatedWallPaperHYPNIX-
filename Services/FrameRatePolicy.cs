namespace AnimatedWallPaper.Services;

internal static class FrameRatePolicy
{
    private static readonly int[] SupportedValues = [15, 30, 60];

    public static int Normalize(int requested) => SupportedValues.Contains(requested) ? requested : 30;
}
