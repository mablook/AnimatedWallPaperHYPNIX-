namespace AnimatedWallPaper.Services;

// A monitor owns its own clock. Paused monitors never accumulate catch-up work.
internal sealed class FireFrameStepper
{
    double? last;
    double remainder;
    bool wasFrozen;
    public int Advance(double time,bool frozen)
    {
        if(last is null||frozen||wasFrozen) {
            last=time;remainder=0;wasFrozen=frozen;return 0;
        }
        double elapsed=time-last.Value;last=time;
        if(!double.IsFinite(elapsed)||elapsed<0||elapsed>.2) {remainder=0;return 0;}
        remainder+=elapsed;
        int count=Math.Min(8,(int)((remainder+1e-9)*60));
        remainder-=count/60.0;
        if(count==8)remainder=0;
        return count;
    }
}
