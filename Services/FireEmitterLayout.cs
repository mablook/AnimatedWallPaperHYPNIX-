namespace AnimatedWallPaper.Services;

internal static class FireEmitterLayout
{
    public const int MaxCount=48;
    public const float EdgePadding=12;

    // Preserve visual density across resolutions and preview sizes. Wider aspect
    // ratios get more sources; a 4K and 1080p 16:9 monitor show the same composition.
    public static int CountForViewport(int width,int height)
    {
        if(width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width));
        return Math.Clamp((int)Math.Ceiling(width/(double)height*8),6,MaxCount);
    }
}
