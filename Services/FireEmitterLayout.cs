namespace AnimatedWallPaper.Services;

internal static class FireEmitterLayout
{
    public const int MaxCount=48;
    public const int GridWidth=288;
    public const float EdgePadding=12;
    // Match the approved 16:9 composition at normal size. Height, not monitor width,
    // controls how tall a flame appears; the horizontal bed always fits its viewport.
    public const float VerticalPixelsPerHeight=(16f/9)/(GridWidth-2*EdgePadding-(GridWidth-2*EdgePadding)/14);

    // Preserve visual density across resolutions and preview sizes. Wider aspect
    // ratios get more sources; a 4K and 1080p 16:9 monitor show the same composition.
    public static int CountForViewport(int width,int height,float scale=1)
    {
        if(width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width));
        var size=float.IsFinite(scale)?Math.Clamp(scale,.3f,3):1;
        // Smaller flames need more sources, rather than a narrower band of fire.
        // Clamp before converting so even pathological aspect ratios stay within the GPU budget.
        return (int)Math.Clamp(Math.Ceiling(width/(double)height*8/size),6,MaxCount);
    }
}
