namespace AnimatedWallPaper.Services;

internal sealed record AethelisAudioProfile(float Bass, float Mids, float Highs, float Energy, float Complexity)
{
    public static AethelisAudioProfile Analyze(float[] bands, float sensitivity)
    {
        if (bands.Length == 0) return new(0, 0, 0, 0, 0);

        var bassEnd = Math.Max(1, bands.Length / 4);
        var midsEnd = Math.Max(bassEnd + 1, bands.Length * 3 / 4);
        var bass = BandEnergy(bands, 0, bassEnd, sensitivity);
        var mids = BandEnergy(bands, bassEnd, midsEnd, sensitivity);
        var highs = BandEnergy(bands, midsEnd, bands.Length, sensitivity);
        var energy = Math.Clamp(bass * 0.50f + mids * 0.32f + highs * 0.18f, 0, 1);
        var complexity = Math.Clamp(Math.Min(bass, highs) * 0.65f + mids * 0.35f, 0, 1);
        return new(bass, mids, highs, energy, complexity);
    }

    private static float BandEnergy(float[] bands, int start, int end, float sensitivity)
    {
        var sum = 0f;
        var peak = 0f;
        for (var index = start; index < end; index++)
        {
            var value = Math.Clamp(bands[index] * sensitivity, 0, 1);
            sum += value;
            peak = Math.Max(peak, value);
        }

        var average = sum / Math.Max(1, end - start);
        return Math.Clamp(average * 0.62f + peak * 0.38f, 0, 1);
    }
}
