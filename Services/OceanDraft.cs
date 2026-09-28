namespace AnimatedWallPaper.Services;

internal sealed class OceanDraft
{
    private OceanPreferences _baseline;
    private OceanRuntime _baselineRuntime;
    public OceanPreferences Preferences { get; private set; }
    public OceanRuntime Runtime { get; private set; }
    public bool IsDirty => Preferences != _baseline;

    public OceanDraft(OceanPreferences preferences, OceanRuntime? applied = null)
    {
        Preferences = _baseline = preferences.Normalize();
        Runtime = applied?.Fork() ?? new(Preferences);
        _baselineRuntime = Runtime.Fork();
    }
    public void Edit(OceanPreferences preferences) { Preferences=preferences.Normalize(); Runtime.Update(Preferences); }
    public void ChooseMoment(OceanMoment moment)
    {
        Edit(Preferences with { Moment=moment, SkyUtc=OceanPreferences.MomentUtc(moment) });
        // A selected card is also a seek command when the cycle has already advanced.
        Runtime.SeekSky(Preferences.SkyUtc!.Value);
    }
    public void SetCycle(bool cycle)
        => Edit(cycle ? Preferences with { DayCycle=true } : Preferences with
        { DayCycle=false, Moment=null, SkyUtc=Runtime.Frame().Snapshot.SkyUtc });
    public void Discard() { Preferences=_baseline; Runtime=_baselineRuntime.Fork(); }
    public void MarkApplied(OceanPreferences submitted, OceanRuntime runtime)
    { _baseline=submitted; _baselineRuntime=runtime.Fork(); }
}
