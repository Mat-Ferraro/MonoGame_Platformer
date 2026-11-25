// Rotate reflected screen-space XY tangent vectors, preserve Z and edge eligibility alpha.
#if OPENGL
#define PS_MODEL ps_3_0
#else
#define PS_MODEL ps_4_0_level_9_3
#endif
sampler MapSampler : register(s0);
float4 MapTransform; // cos, sin, mirror X, mirror Y
float4 TransformPS(float4 tint : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 encoded = tex2D(MapSampler,uv);
    float2 xy = (encoded.rg*2-1)*MapTransform.zw;
    float2 rotated = float2(MapTransform.x*xy.x-MapTransform.y*xy.y,
                            MapTransform.y*xy.x+MapTransform.x*xy.y);
    return float4(saturate(rotated*.5+.5),encoded.b,encoded.a)*tint;
}
technique TangentTransform { pass P0 { PixelShader=compile PS_MODEL TransformPS(); } }
