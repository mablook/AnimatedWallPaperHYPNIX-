// Original radix-2 inverse FFT. Frequencies use unshifted DFT order; inverse sign +.
// Four array slices pack eight complex fields: h,Dx / Dz,hx / hz,Dxx / Dxz,Dzz.
cbuffer SpectrumFrame : register(b0) { float4 Parameters; } // local time, N, length, unused
Texture2D<float4> Seed : register(t0);
Texture2DArray<float4> Input : register(t1);
RWTexture2DArray<float4> Output : register(u0);
RWTexture2D<float4> Displacement : register(u1);
RWTexture2D<float4> DerivativesA : register(u2);
RWTexture2D<float4> DerivativesB : register(u3);
static const float TAU = 6.28318530718;

float2 ComplexMultiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }
float2 TimesI(float2 a) { return float2(-a.y, a.x); }

[numthreads(8, 8, 1)]
void Evolve(uint3 id : SV_DispatchThreadID)
{
    float4 seed = Seed.Load(int3(id.xy, 0));
    float4 k = Seed.Load(int3(id.x, id.y + 256, 0));
    float s, c; sincos(k.z * Parameters.x, s, c);
    float2 h = ComplexMultiply(seed.xy, float2(c,s)) + ComplexMultiply(seed.zw, float2(c,-s));
    float2 ih = TimesI(h);
    // +i k/|k| compresses the surface at positive height crests for positive chop.
    Output[uint3(id.xy,0)] = float4(h, ih * k.x * k.w);
    Output[uint3(id.xy,1)] = float4(ih * k.y * k.w, ih * k.x);
    Output[uint3(id.xy,2)] = float4(ih * k.y, -h * k.x * k.x * k.w);
    Output[uint3(id.xy,3)] = float4(-h * k.x * k.y * k.w, -h * k.y * k.y * k.w);
}

groupshared float4 Values[256];

void Butterfly(uint lane)
{
    [unroll] for (uint size = 2; size <= 256; size <<= 1)
    {
        uint halfSize = size >> 1;
        uint offset = lane % halfSize;
        uint first = (lane / size) * size + offset;
        float4 a = Values[first], b = Values[first + halfSize];
        float s, c; sincos(TAU * offset / size, s, c);
        float4 rotated = float4(ComplexMultiply(b.xy, float2(c,s)), ComplexMultiply(b.zw, float2(c,s)));
        float4 result = a + ((lane % size) < halfSize ? rotated : -rotated);
        GroupMemoryBarrierWithGroupSync(); // complete all reads before any lane writes
        Values[lane] = result;
        GroupMemoryBarrierWithGroupSync();
    }
}

[numthreads(256, 1, 1)]
void InverseRows(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    Values[lane] = Input.Load(int4(reversebits(lane) >> 24, group.x, group.y, 0));
    GroupMemoryBarrierWithGroupSync();
    Butterfly(lane);
    Output[uint3(lane, group.x, group.y)] = Values[lane] / 256;
}

[numthreads(256, 1, 1)]
void InverseColumns(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    Values[lane] = Input.Load(int4(group.x, reversebits(lane) >> 24, group.y, 0));
    GroupMemoryBarrierWithGroupSync();
    Butterfly(lane);
    Output[uint3(group.x, lane, group.y)] = Values[lane] / 256;
}

[numthreads(8, 8, 1)]
void Assemble(uint3 id : SV_DispatchThreadID)
{
    float4 a = Input.Load(int4(id.xy,0,0));
    float4 b = Input.Load(int4(id.xy,1,0));
    float4 c = Input.Load(int4(id.xy,2,0));
    float4 d = Input.Load(int4(id.xy,3,0));
    Displacement[id.xy] = float4(a.z, a.x, b.x, 0);
    DerivativesA[id.xy] = float4(b.z, c.x, c.z, d.x);
    DerivativesB[id.xy] = float4(d.x, d.z, b.z*b.z, c.x*c.x);
}
