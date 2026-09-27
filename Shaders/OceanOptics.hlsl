// Shared optical depth: km, unexposed linear RGB. No celestial disks in the sky cache.
Texture2D<float4> OpticalDepth : register(t17);
SamplerState OpticalSampler : register(s3);
float3 OpticalTransmission(float3 p, float3 direction, float aerosol)
{
    float r=length(p), mu=dot(p,direction)/r;
    if(mu<0 && r*r*(1-mu*mu)<6360.0*6360.0) return 0;
    float2 uv=float2(.5+.5*sign(mu)*sqrt(abs(mu)),sqrt(saturate((r-6360.0028)/100)));
    uv=(uv*float2(255,63)+.5)/float2(256,64);
    float3 depth=OpticalDepth.SampleLevel(OpticalSampler,uv,0).rgb;
    return exp(-float3(.0058,.0135,.0331)*depth.x-aerosol*depth.y-float3(.00065,.001881,.000085)*depth.z);
}
float3 GroundTransmission(float apparentElevation, float aerosol)
{
    return OpticalTransmission(float3(0,6360.0028,0),float3(cos(apparentElevation),sin(apparentElevation),0),aerosol);
}
