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
        // Spectral contrast. Real music energizes every band at once and ApplyGain's soft-knee
        // compression flattens the differences, so the flame would rise as one wall. Emphasize each
        // band's deviation above the current average: loud frequencies form distinct columns (fire
        // as an equalizer) while a smaller level term keeps overall responsiveness. A flat spectrum
        // reacts gently and evenly; a peaky one visibly separates into bands.
        float mean=0;for(int i=0;i<count;i++)mean+=result[i];mean/=count;
        for(int i=0;i<count;i++)
            result[i]=Math.Clamp(result[i]*.45f+Math.Max(0,result[i]-mean)*2.8f,0,1);
        return result;
    }
}
