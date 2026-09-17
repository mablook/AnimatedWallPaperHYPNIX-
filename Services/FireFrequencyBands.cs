namespace AnimatedWallPaper.Services;

internal static class FireFrequencyBands
{
    public const int Count=9;

    // The input FFT bands run low-to-high logarithmically. Flame positions run
    // left-to-right, so reverse the non-overlapping frequency groups.
    public static float[] Analyze(float[] bands,float sensitivity,int count=Count)
    {
        if(count<1||count>FireEmitterLayout.MaxCount)throw new ArgumentOutOfRangeException(nameof(count));
        var result=new float[count];
        if(bands.Length==0)return result;
        for(int flame=0;flame<count;flame++)
        {
            int group=count-1-flame;
            int start=group*bands.Length/count,end=(group+1)*bands.Length/count;
            float sum=0,peak=0;
            for(int bin=start;bin<end;bin++)
            {
                float value=AethelisAudioProfile.ApplyGain(bands[bin],sensitivity);
                sum+=value;peak=Math.Max(peak,value);
            }
            if(end>start)result[flame]=.62f*sum/(end-start)+.38f*peak;
        }
        return result;
    }
}
