// Criado por Marcelo Bossle. Copyright (c) 2026 Marcelo Bossle. Todos os direitos reservados.
// LicenseRef-HYPNIX-Proprietary. Reduced combustion model, no borrowed shader code.
cbuffer Frame : register(b0) { float4 grid; float4 view; float4 scene; float4 layout; float4 paletteLow; float4 paletteHigh; float4 frequencyBands[12]; float4 emitters; float4 backgroundColor; float4 backgroundSize; }
// grid.xyz dimensions, grid.w fixed dt; view.xy pixels, z time, w source strength.
// Independent groups ordered treble (left) -> bass (right); count follows viewport aspect ratio.
Texture3D<float4> velocity : register(t0);
Texture3D<float4> material : register(t1); // fuel, normalized heat, soot, reaction
Texture3D<float> pressure : register(t2);
Texture3D<float> divergence : register(t3);
Texture3D<float4> curlField : register(t4);
Texture2D<float4> particles : register(t5); // row 0: position/age; row 1: velocity/heat
Texture3D<float4> transported : register(t6);
Texture2D<float4> fireImage : register(t7);
Texture2D<float4> backgroundImage : register(t8);
RWTexture3D<float4> outVelocity : register(u0);
RWTexture3D<float4> outMaterial : register(u1);
RWTexture3D<float> outPressure : register(u2);
RWTexture3D<float> outDivergence : register(u3);
RWTexture3D<float4> outCurl : register(u4);
RWTexture2D<float4> outParticles : register(u5);
SamplerState linearClamp : register(s0);
int3 bounds(int3 p) { return clamp(p, int3(0,0,0), (int3)grid.xyz-1); }
float3 V(int3 p) { return velocity.Load(int4(bounds(p),0)).xyz; }
float P(int3 p) { return pressure.Load(int4(bounds(p),0)); }
float C(int3 p) { return curlField.Load(int4(bounds(p),0)).w; }
float hash(float3 p) { p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
float flameBand(int index) { return frequencyBands[index/4][index%4]; }
int flameCount() { return (int)emitters.x; }
float sourceSpacing() { return (grid.x-2*emitters.y)/max(1,emitters.x-1); }
float4 burner(int index) {
    float seed=index+1;
    float x=emitters.y+sourceSpacing()*index;
    float z=grid.z*.5+(hash(float3(seed,2,3))-.5)*8;
    return float4(x,z,sourceSpacing()*(.72+hash(float3(seed,7,2))*.18),.65+hash(float3(seed,4,1))*.5);
}
// Map the source bed to the screen with a small horizontal overscan (half a source spacing past
// each border, so it scales with the source count for any aspect ratio). The visible edges are then
// covered by sources with neighbors on both sides, giving a full-width bed instead of a lower
// "arch" at the far left/right.
float2 pixelScale() {
    // Size controls each flame, never the total width of the source bed. More sources
    // are supplied for wider viewports/smaller flames. Keep vertical geometry based
    // on height so ultrawide monitors do not stretch the flames upwards.
    if(scene.x>.5)return float2(view.x/(grid.x-2*emitters.y-sourceSpacing()),view.y*emitters.z*layout.x);
    float single=view.y*.0082*layout.x;
    return float2(single,single);
}
// Base sits near the very bottom of the monitor by default so the flame roots at the edge;
// position Y (layout.z) still moves it up/down from there.
float2 origin() { return float2(view.x*(.5+layout.y*.5),view.y*(.99-layout.z*.5)); }
float noise(float3 p) {
    float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
                lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);
}
[numthreads(8,8,4)] void Transport(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return;
    float3 back=(id+.5-grid.w*V(id))/grid.xyz;
    outMaterial[id]=material.SampleLevel(linearClamp,back,0);
}
[numthreads(8,8,4)] void Advect(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return;
    float3 p=id+.5, uv=p/grid.xyz;
    float3 v=V(id);
    float3 back=(p-grid.w*v)/grid.xyz;
    // Reduced entrainment drag prevents an ever-accelerating chimney in this small domain.
    v=velocity.SampleLevel(linearClamp,back,0).xyz*exp(-.8*grid.w);
    // Limited MacCormack transport: recover detail lost by trilinear advection,
    // but never invent a new extremum outside the eight source-cell neighbors.
    float4 forward=transported.Load(int4(id,0));
    float4 reverse=transported.SampleLevel(linearClamp,(p+grid.w*V(id))/grid.xyz,0);
    float4 m=forward+.5*(material.Load(int4(id,0))-reverse);
    int3 base=(int3)floor(back*grid.xyz-.5);
    float4 lo=1e9, hi=-1e9;
    [unroll] for(int z=0;z<2;z++) [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++) {
        float4 neighbor=material.Load(int4(bounds(base+int3(x,y,z)),0));
        lo=min(lo,neighbor);hi=max(hi,neighbor);
    }
    m=clamp(m,lo,hi);
    // Reduced oxygen availability inside a fuel-rich core, stronger burning at its edges.
    float oxygen=1-.8*smoothstep(.15,.85,m.x);
    float burn=min(m.x,grid.w*2.3*oxygen*smoothstep(.12,.35,m.y));
    m.x-=burn; m.y+=burn*1.5; m.z+=burn*.17;
    m.y*=exp(-1.25*grid.w); m.z*=exp(-.5*grid.w); m.w=burn/grid.w;
    // A localized irregular burner. Velocity is in voxels/second.
    float3 q=p-float3(grid.x*.5,4.5,grid.z*.5);
    float r=length(q.xz/float2(16,9));
    float source=exp(-r*r*2.4)*exp(-q.y*q.y*.16)*view.w;
    float pulse=.8+.18*sin(view.z*5.7+q.x*.22)+.17*sin(view.z*9.1+q.x*.55+q.z*.4);
    float singleEnergy=(scene.y+scene.z+scene.w)/3;
    source*=1+singleEnergy*.85;
    float sourceLift=source*(1+singleEnergy*.3);
    if(scene.x>.5) {
        source=0;sourceLift=0;
        int nearest=(int)floor((p.x-emitters.y)/sourceSpacing()+.5);
        // Gaussian tails beyond two source intervals are negligible. Constant
        // neighborhood cost avoids looping over every source in an ultrawide monitor.
        [unroll] for(int neighbor=-2;neighbor<=2;neighbor++) {
            int i=nearest+neighbor;
            if(i<0||i>=flameCount())continue;
            float4 b=burner(i);
            float2 radial=(p.xz-b.xy)/float2(b.z,7);
            float rhythm=.82+.13*sin(view.z*(3.1+i*.17)+i*2.7)+.1*sin(view.z*7.3+p.x*.48+i);
            // Audio drive must clearly beat the flame's own procedural flicker (rhythm ~+/-28%),
            // so it is amplified well past the old +85% fuel / +30% lift. Lift dominates because a
            // taller flame reads as "reacting" far more than a slightly brighter bed.
            float energy=flameBand(i);
            float localSource=exp(-dot(radial,radial)*2.4)*b.w*rhythm*(1+energy*1.7);
            source+=localSource;sourceLift+=localSource*(1+energy*3.2);
        }
        source*=exp(-q.y*q.y*.22)*view.w;
        sourceLift*=exp(-q.y*q.y*.22)*view.w;
        pulse=1;
    }
    m.x=max(m.x,source*pulse); m.y=max(m.y,source*.85);
    v.y+=grid.w*(m.y*55-m.z*3+sourceLift*90);
    v.x+=grid.w*m.y*(sin(p.y*.22-view.z*4.3+p.z*.27)*20+sin(p.z*.35+view.z*2.1)*12);
    v.z+=grid.w*m.y*(cos(p.y*.19-view.z*3.7+p.x*.28)*18);
    if(id.x<2||id.x>grid.x-3)v.x=0;
    if(id.z<2||id.z>grid.z-3)v.z=0;
    if(id.y<2)v.y=max(v.y,0);
    if(id.y>grid.y-4)m*=.85;
    outVelocity[id]=float4(v,0); outMaterial[id]=max(m,0);
}
[numthreads(8,8,4)] void Curl(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return; int3 p=id;
    float3 x=(V(p+int3(1,0,0))-V(p-int3(1,0,0)))*.5;
    float3 y=(V(p+int3(0,1,0))-V(p-int3(0,1,0)))*.5;
    float3 z=(V(p+int3(0,0,1))-V(p-int3(0,0,1)))*.5;
    float3 c=float3(y.z-z.y,z.x-x.z,x.y-y.x); outCurl[id]=float4(c,length(c));
}
[numthreads(8,8,4)] void Confine(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return; int3 p=id;
    float3 n=float3(C(p+int3(1,0,0))-C(p-int3(1,0,0)),C(p+int3(0,1,0))-C(p-int3(0,1,0)),C(p+int3(0,0,1))-C(p-int3(0,0,1)));
    n/=max(length(n),1e-5);
    float3 force=cross(n,curlField.Load(int4(p,0)).xyz)*3.1;
    outVelocity[id]=float4(clamp(V(p)+grid.w*force,-100,100),0);
}
[numthreads(8,8,4)] void Diverge(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return; int3 p=id;
    outDivergence[id]=.5*(V(p+int3(1,0,0)).x-V(p-int3(1,0,0)).x+V(p+int3(0,1,0)).y-V(p-int3(0,1,0)).y+V(p+int3(0,0,1)).z-V(p-int3(0,0,1)).z);
}
[numthreads(8,8,4)] void Jacobi(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return; int3 p=id;
    outPressure[id]=(P(p+int3(1,0,0))+P(p-int3(1,0,0))+P(p+int3(0,1,0))+P(p-int3(0,1,0))+P(p+int3(0,0,1))+P(p-int3(0,0,1))-divergence.Load(int4(p,0)))/6;
}
[numthreads(8,8,4)] void Project(uint3 id:SV_DispatchThreadID) {
    if(any(id>=uint3(grid.xyz)))return; int3 p=id;
    float3 grad=.5*float3(P(p+int3(1,0,0))-P(p-int3(1,0,0)),P(p+int3(0,1,0))-P(p-int3(0,1,0)),P(p+int3(0,0,1))-P(p-int3(0,0,1)));
    outVelocity[id]=float4(V(p)-grad,0);
}
struct Vertex { float4 pos:SV_Position; float2 uv:TEXCOORD; };
Vertex VSMain(uint id:SV_VertexID) { Vertex o; o.uv=float2((id<<1)&2,id&2); o.pos=float4(o.uv*float2(2,-2)+float2(-1,1),0,1); return o; }
float3 hotColor(float heat) {
    // Artistic approximation of incandescent soot; not a spectral temperature measurement.
    float h=saturate(heat);
    return float3(1, lerp(.035,.5,pow(h,.85)), lerp(.0005,.06,h*h));
}
float4 PSMain(Vertex input):SV_Target {
    float2 pixel=input.uv*view.xy;
    float2 scale=pixelScale();
    float2 cell=float2((pixel.x-origin().x)/scale.x+grid.x*.5,(origin().y-pixel.y)/scale.y);
    if(any(cell<0)||any(cell>grid.xy))return float4(.001,.001,.001,1);
    float3 col=0; float trans=1;
    const int steps=72; float ds=grid.z/steps;
    for(int i=0;i<steps;i++) {
        float3 p=float3(cell, (i+.5)*ds);
        float4 m=material.SampleLevel(linearClamp,p/grid.xyz,0);
        float detail=noise(p*.39+float3(0,-view.z*7,0))*.65+noise(p*.83-float3(view.z,view.z*11,0))*.35;
        float heat=max(0,m.y-.07-(detail-.45)*min(m.y,.42));
        float luminous=smoothstep(.1,.55,heat)*(1-.75*smoothstep(.2,.85,m.x));
        float extinction=m.z*.38+luminous*.035;
        float a=1-exp(-extinction*ds);
        float3 emission=hotColor(heat*.95)*luminous*heat*.15*smoothstep(0,5,p.y);
        col+=trans*emission*(extinction>1e-5?a/extinction:ds);
        trans*=1-a; if(trans<.015)break;
    }
    // Exposure and display transfer only. No bloom.
    col*=1.5;
    col/=1+max(col.r,max(col.g,col.b));
    col=lerp(col,lerp(paletteLow.rgb,paletteHigh.rgb,saturate(col.r))*col.r,paletteLow.w);
    col=pow(max(col,0),1/2.2)*layout.w;
    return float4(col,lerp(1,trans,layout.w));
}

// Persistent embers integrate the same projected 3D velocity field as the fire.
[numthreads(64,1,1)] void Sparks(uint3 id:SV_DispatchThreadID) {
    if(id.x>=64)return;
    float seed=id.x+1;
    float4 a=particles.Load(int3(id.x,0,0)), b=particles.Load(int3(id.x,1,0));
    float lifetime=2.1+hash(float3(seed,4,8))*1.9;
    if(view.z<grid.w*.5) {a=float4(0,0,0,-seed*.14);b=0;}
    a.w+=grid.w;
    if(a.w>lifetime||any(a.xyz>grid.xyz-1)||any(a.xyz<0)) {
        float localEnergy=scene.x>.5?flameBand(id.x%flameCount()):(scene.y+scene.z+scene.w)/3;
        a=float4(0,0,0,-(4+hash(float3(seed,view.z,3))*4)/(1+localEnergy*.65)); b=0;
    }
    if(a.w>=0&&b.w==0) {
        if(view.w<=0) a.w=-grid.w;
        else {
            a.xyz=float3(grid.x*.5+(hash(float3(seed,view.z,1))-.5)*25,6,grid.z*.5+(hash(float3(seed,view.z,2))-.5)*10);
            if(scene.x>.5) {
                float4 burnerPosition=burner(id.x%flameCount());
                a.x=burnerPosition.x+(hash(float3(seed,view.z,1))-.5)*burnerPosition.z;
                a.z=burnerPosition.y+(hash(float3(seed,view.z,2))-.5)*6;
            }
            b=float4((hash(float3(seed,2,7))-.5)*15,36+hash(float3(seed,5,3))*14,0,1);
        }
    }
    if(a.w>=0) {
        float3 flow=velocity.SampleLevel(linearClamp,a.xyz/grid.xyz,0).xyz;
        b.xyz=lerp(b.xyz,flow+float3(0,16,0),1-exp(-grid.w*1.6));
        b.y-=grid.w*3;
        a.xyz+=b.xyz*grid.w;
        b.w=max(.001,1-a.w/lifetime);
    }
    outParticles[uint2(id.x,0)]=a;outParticles[uint2(id.x,1)]=b;
}
struct SparkVertex {float4 pos:SV_Position;float2 uv:TEXCOORD0;float heat:TEXCOORD1;float depth:TEXCOORD2;};
SparkVertex SparksVS(uint id:SV_VertexID,uint instance:SV_InstanceID) {
    const float2 corners[6]={float2(-1,-1),float2(1,-1),float2(-1,1),float2(-1,1),float2(1,-1),float2(1,1)};
    float4 a=particles.Load(int3(instance,0,0)),b=particles.Load(int3(instance,1,0));
    float2 scale=pixelScale();
    float2 center=float2(origin().x+(a.x-grid.x*.5)*scale.x,origin().y-a.y*scale.y);
    float2 direction=normalize(float2(b.x*scale.x,-b.y*scale.y)+float2(0,-.001));
    float2 normal=float2(-direction.y,direction.x);
    float2 corner=corners[id];
    float radius=.6+hash(float3(instance,1,9))*.55;
    float tail=1.4+min(length(b.xy*scale)*.022,5);
    float2 pixel=center+normal*corner.x*radius+direction*corner.y*tail;
    SparkVertex o;o.pos=float4(pixel/view.xy*float2(2,-2)+float2(-1,1),0,1);
    o.uv=corner;o.heat=a.w>=0?b.w:0;o.depth=a.z;return o;
}
float4 SparksPS(SparkVertex i):SV_Target {
    float shape=pow(saturate(1-dot(i.uv,i.uv)),1.7);
    float fade=smoothstep(0,.2,i.heat)*shape;
    float2 scale=pixelScale();
    float2 cell=float2((i.pos.x-origin().x)/scale.x+grid.x*.5,(origin().y-i.pos.y)/scale.y);
    float soot=material.SampleLevel(linearClamp,float3(cell,i.depth*.5)/grid.xyz,0).z;
    float3 color=lerp(float3(.8,.07,.001),float3(1,.62,.15),i.heat);
    color=lerp(color,lerp(paletteLow.rgb,paletteHigh.rgb,i.heat),paletteLow.w);
    return float4(color*fade*exp(-soot*i.depth*.3)*layout.w,0);
}
float4 CompositePS(Vertex i):SV_Target {
    float4 fire=fireImage.SampleLevel(linearClamp,i.uv,0);
    float3 color=fire.rgb;
    float3 spread=0;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) {
        float3 sample=fireImage.SampleLevel(linearClamp,i.uv+float2(x,y)*3/view.xy,0).rgb;
        spread+=max(sample-.45,0)/9;
    }
    color=saturate(color+spread*paletteHigh.w*.22);
    if(backgroundColor.w>.5) {
        float3 backdrop=backgroundColor.rgb;
        if(backgroundColor.w>1.5) {
            float imageAspect=backgroundSize.x/max(backgroundSize.y,1);
            float screenAspect=view.x/view.y;
            float2 crop=float2(min(1,screenAspect/imageAspect),min(1,imageAspect/screenAspect));
            float4 sample=backgroundImage.SampleLevel(linearClamp,(i.uv-.5)*crop+.5,0);
            backdrop=sample.rgb*sample.a;
        }
        // Transmittance comes from the volume integral, never from black-pixel keying.
        color=pow(saturate(pow(color,2.2)+pow(backdrop,2.2)*fire.a),1/2.2);
    }
    return float4(color,1);
}
