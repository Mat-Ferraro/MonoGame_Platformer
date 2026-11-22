// L03A GPU material workbench -- DesktopGL-safe SpriteBatch pixel shader.
// The standard SpriteBatch vertex shader supplies COLOR0 and TEXCOORD0.
// IMPORTANT: Never access POSITION / SV_POSITION from a ps_3_0 pixel shader.
// Screen position is reconstructed from the authored panel rectangle and UV.
#if OPENGL
    #define PS_SHADERMODEL ps_3_0
#else
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

Texture2D NormalMap;
sampler2D NormalSampler = sampler_state
{
    Texture = <NormalMap>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

Texture2D EmissionMap;
sampler2D EmissionSampler = sampler_state
{
    Texture = <EmissionMap>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

Texture2D MaskMap;
sampler2D MaskSampler = sampler_state
{
    Texture = <MaskMap>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

Texture2D EdgeMap;
sampler2D EdgeSampler = sampler_state
{
    Texture = <EdgeMap>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

// SurfaceMode: Unlit=0, Flat=1, NormalMapped=2, Rim=3, Hybrid=4
float SurfaceMode;
float3 AmbientColor;
float NormalStrength;
float RimStrength;
float SpecularStrength;
float SpecularPower;
float EmissionStrength;
float LightMaskStrength;
float FlipTangentX;
float RotationRadians;

// Screen-space panel bounds (x, y, width, height), updated per Draw call.
// Using interpolated UVs here avoids the illegal ps_3_0 POSITION input.
float4 SpriteRectangle;

// (screen x, screen y, radius, virtual height)
float4 Light0PositionRadius;
float4 Light1PositionRadius;
// (red, green, blue, intensity)
float4 Light0ColorIntensity;
float4 Light1ColorIntensity;

float3 DecodeNormal(float2 uv)
{
    float3 encoded = tex2D(NormalSampler, uv).rgb * 2.0 - 1.0;
    encoded.x *= FlipTangentX;
    float c = cos(RotationRadians);
    float s = sin(RotationRadians);
    float2 rotated = float2(encoded.x*c - encoded.y*s, encoded.x*s + encoded.y*c);
    float3 adjusted = normalize(float3(rotated * NormalStrength, max(0.001, encoded.z)));
    return adjusted;
}

// One light's contribution, including optional normal and specular response.
// Returns illumination and independent specular energy.
void EvaluateLight(
    float2 pixelPosition,
    float3 surfaceNormal,
    float4 lightPositionRadius,
    float4 lightColorIntensity,
    float useNormal,
    out float3 diffuse,
    out float3 specular,
    out float2 lightDirection,
    out float falloff)
{
    float2 toLight = lightPositionRadius.xy - pixelPosition;
    float distance = length(toLight);
    float radius = max(1.0, lightPositionRadius.z);
    float attenuation = saturate(1.0 - distance/radius);
    falloff = attenuation * attenuation;
    float3 lightVector = normalize(float3(toLight, max(0.001,lightPositionRadius.w)));
    lightDirection = lightVector.xy;
    float directionFactor = lerp(1.0, saturate(dot(surfaceNormal, lightVector)), useNormal);
    float3 lightEnergy = lightColorIntensity.rgb * (lightColorIntensity.a * falloff);
    diffuse = lightEnergy * directionFactor;
    float3 halfVector = normalize(lightVector + float3(0,0,1));
    float specTerm = pow(saturate(dot(surfaceNormal,halfVector)), max(1.0, SpecularPower));
    specular = lightEnergy * specTerm * SpecularStrength * useNormal;
}

float4 MainPS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 albedo = tex2D(SpriteTextureSampler,uv) * vertexColor;
    float3 emitted = tex2D(EmissionSampler,uv).rgb * EmissionStrength;

    // An unlit material is independent of ambient and both dynamic lights.
    if (SurfaceMode < 0.5)
        return float4(saturate(albedo.rgb + emitted), albedo.a);

    // SpriteEffects.FlipHorizontally reverses UVs, not rectangle screen position.
    float2 physicalUV = uv;
    if (FlipTangentX < 0.0)
        physicalUV.x = 1.0 - physicalUV.x;
    float2 pixelPosition = SpriteRectangle.xy + physicalUV * SpriteRectangle.zw;

    float3 surfaceNormal = DecodeNormal(uv);
    float usesNormal = (SurfaceMode > 1.5 && SurfaceMode < 2.5) || (SurfaceMode > 3.5) ? 1.0 : 0.0;
    float usesDiffuse = (SurfaceMode < 2.5 || SurfaceMode > 3.5) ? 1.0 : 0.0;
    float usesRim = (SurfaceMode > 2.5) ? 1.0 : 0.0;

    float3 diffuse0, spec0, diffuse1, spec1;
    float2 dir0, dir1;
    float falloff0, falloff1;
    EvaluateLight(pixelPosition,surfaceNormal,Light0PositionRadius,Light0ColorIntensity,
        usesNormal,diffuse0,spec0,dir0,falloff0);
    EvaluateLight(pixelPosition,surfaceNormal,Light1PositionRadius,Light1ColorIntensity,
        usesNormal,diffuse1,spec1,dir1,falloff1);

    float mask = lerp(1.0,tex2D(MaskSampler,uv).r,saturate(LightMaskStrength));
    float3 surfaceEnergy = AmbientColor + mask * usesDiffuse * (diffuse0 + diffuse1);
    float3 outputRgb = albedo.rgb * surfaceEnergy + mask * (spec0 + spec1);

    // Edge map: RG encodes local edge facing direction, A encodes edge eligibility.
    float4 edge = tex2D(EdgeSampler,uv);
    float2 edgeNormal = edge.rg*2.0 - 1.0;
    edgeNormal.x *= FlipTangentX;
    edgeNormal = normalize(edgeNormal + float2(0.0001,0.0001));
    float rim0 = saturate(dot(edgeNormal,normalize(dir0+float2(0.0001,0.0001))));
    float rim1 = saturate(dot(edgeNormal,normalize(dir1+float2(0.0001,0.0001))));
    float3 rimEnergy = Light0ColorIntensity.rgb * (Light0ColorIntensity.a * falloff0 * rim0)
                     + Light1ColorIntensity.rgb * (Light1ColorIntensity.a * falloff1 * rim1);
    outputRgb += rimEnergy * edge.a * RimStrength * mask * usesRim;

    outputRgb += emitted;
    return float4(saturate(outputRgb), albedo.a);
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
