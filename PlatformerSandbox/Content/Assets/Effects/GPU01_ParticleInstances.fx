// Optional presentation-only GPU instance adapter. Compatible CPU particle pools
// remain authoritative; analytic motion is confined to the explicit stress fixture.
#if OPENGL
#define VS_MODEL vs_3_0
#define PS_MODEL ps_3_0
#define POSITION_SEM POSITION0
#else
#define VS_MODEL vs_4_0
#define PS_MODEL ps_4_0
#define POSITION_SEM SV_POSITION
#endif
float4x4 GPU01Projection;
float4 GPU01Stress; // enabled, time seconds, viewport width, viewport height
Texture2D GPU01ParticleTexture;
sampler2D GPU01ParticleSampler = sampler_state
{
    Texture=<GPU01ParticleTexture>;
    MinFilter=LINEAR; MagFilter=LINEAR; MipFilter=NONE;
    AddressU=CLAMP; AddressV=CLAMP;
};
struct VSIn
{
    float3 Position : POSITION0;
    float2 UV : TEXCOORD0;
    float4 Instance : TEXCOORD1; // center.xy, diameter, rotation or stress seed
    float4 Tint : COLOR1;
};
struct VSOut
{
    float4 Position : POSITION_SEM;
    float2 UV : TEXCOORD0;
    float4 Tint : COLOR0;
};
VSOut ParticleVS(VSIn input)
{
    VSOut o;
    float2 center=input.Instance.xy;
    float angle=input.Instance.w;
    if(GPU01Stress.x>0.5)
    {
        float phase=input.Instance.w;
        center=frac(center+GPU01Stress.y*float2(0.025+phase*0.035,0.04+phase*0.05));
        center*=GPU01Stress.zw;
        angle=phase*6.2831853+GPU01Stress.y*0.6;
    }
    float si=sin(angle),co=cos(angle);
    float2 p=input.Position.xy*input.Instance.z;
    p=float2(p.x*co-p.y*si,p.x*si+p.y*co)+center;
    o.Position=mul(float4(p,0,1),GPU01Projection);
    o.UV=input.UV; o.Tint=input.Tint;
    return o;
}
float4 ParticlePS(float4 tint : COLOR0,float2 uv : TEXCOORD0) : COLOR0 {return tex2D(GPU01ParticleSampler,uv)*tint;}
technique GPU01Particles
{
    pass P0 {VertexShader=compile VS_MODEL ParticleVS(); PixelShader=compile PS_MODEL ParticlePS();}
}
