// Production back rims: variable-length screen-space boundary edge data.
// The 128-edge texture is a documented complexity budget, not a shape enum.
#if OPENGL
#define PS_MODEL ps_3_0
#else
#define PS_MODEL ps_4_0_level_9_3
#endif
sampler SpriteSampler : register(s0);
texture PolygonEdges;
sampler EdgeSampler : register(s1) = sampler_state {
    Texture = <PolygonEdges>; MinFilter = Point; MagFilter = Point;
    MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
texture VisibilityMap;
sampler VisibilitySampler : register(s2) = sampler_state {
    Texture = <VisibilityMap>; MinFilter = Point; MagFilter = Point;
    MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
float2 ViewportSize;
float EdgeCount;
float4 LightPositionRadius;
float4 LightShapeDirection;
float4 LightExtra;
float4 LightEnergy;
float4 RimSettings;
float2 InsideCenter;

float4 RimPS(float4 tint : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 art = tex2D(SpriteSampler, uv);
    if (art.a <= 0.0001) return 0;
    float2 p = uv * ViewportSize;
    float2 source = LightPositionRadius.xy;
    float2 axis = LightShapeDirection.yz;
    float kind = LightShapeDirection.x;
    float nearest = 1e10, nearestDark = 1e10, facing = 0, parity = 0;
    [loop] for (int i=0;i<128;i++)
    {
        if (i >= EdgeCount) break;
        float4 edge = tex2Dlod(EdgeSampler,float4((i+0.5)/128.0,0.25,0,0));
        float2 a=edge.xy, b=edge.zw, d=b-a;
        float len2=max(dot(d,d),0.000001);
        float2 q=a+d*saturate(dot(p-a,d)/len2);
        float distance=length(p-q);
        float2 outward=float2(d.y,-d.x)*rsqrt(len2);
        float2 toward=source-q;
        if (kind > 0.5 && kind < 1.5) toward=-axis;
        else if (RimSettings.z > 0.5 &&
            abs(tex2Dlod(EdgeSampler,float4((i+0.5)/128.0,0.75,0,0)).x-RimSettings.z)<0.5)
            toward=source-InsideCenter;
        else if (kind > 2.5) toward=source+axis*clamp(dot(q-source,axis),-LightExtra.y,LightExtra.y)-q;
        float f=dot(outward,toward)/max(length(toward),0.00001);
        if (f<=0.15) nearestDark=min(nearestDark,distance);
        if (distance<nearest) { nearest=distance; facing=f; }
        // Even/odd test across disjoint simple contours; excludes transparent gaps.
        if ((a.y>p.y)!=(b.y>p.y))
        {
            float x=a.x+(p.y-a.y)*(b.x-a.x)/(b.y-a.y);
            if (p.x<x) parity=1-parity;
        }
    }
    if (parity<0.5 || nearestDark<=nearest+2 && nearestDark<=RimSettings.x+2) return 0;
    float rim=(1-smoothstep(0,RimSettings.x,nearest))*smoothstep(0.20,0.50,facing);
    float2 delta=p-source;
    if (kind>2.5) delta-=axis*clamp(dot(delta,axis),-LightExtra.y,LightExtra.y);
    float distanceToLight=length(delta);
    float falloff=saturate(1-distanceToLight/max(0.001,LightPositionRadius.z));
    falloff*=falloff;
    if (kind>0.5 && kind<1.5) falloff=1;
    if (kind>1.5 && kind<2.5 && distanceToLight>0.00001)
        falloff*=smoothstep(LightShapeDirection.w,max(LightShapeDirection.w+0.00001,LightExtra.x),dot(delta/distanceToLight,axis));
    float visibility=tex2D(VisibilitySampler,uv).r;
    return float4(LightEnergy.rgb*LightEnergy.a*RimSettings.y*rim*falloff*visibility*art.a*tint.rgb,0);
}
technique PolygonRim { pass P0 { PixelShader=compile PS_MODEL RimPS(); } }
