// PlatformerSandbox Lighting Basics — Stage 8 bounded convex polygon occlusion.
// The material response from Stage 5 is retained; hard visibility is evaluated
// independently PER LIGHT (ambient is never shadowed). This is a deliberate
// three-shape lab fixture, not arbitrary production shadow geometry.
// DesktopGL ps_3_0: no POSITION/SV_POSITION pixel-shader semantic.
#if OPENGL
    #define PS_SHADERMODEL ps_3_0
#else
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = POINT;
    MagFilter = POINT;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
Texture2D NormalMap;
sampler2D NormalSampler = sampler_state
{
    Texture = <NormalMap>;
    MinFilter = POINT;
    MagFilter = POINT;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
Texture2D EdgeMap;
sampler2D EdgeSampler = sampler_state
{
    Texture = <EdgeMap>;
    MinFilter = POINT;
    MagFilter = POINT;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

Texture2D ProductionVisibility0;
sampler2D ProductionVisibility0Sampler = sampler_state
{
    Texture = <ProductionVisibility0>;
    MinFilter = POINT; MagFilter = POINT; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
Texture2D ProductionVisibility1;
sampler2D ProductionVisibility1Sampler = sampler_state
{
    Texture = <ProductionVisibility1>;
    MinFilter = POINT; MagFilter = POINT; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
float ProductionMaskMode; // lab=0 (analytic 3 polygons); gameplay=1 (LDtk visibility fans)
// Phase 18: 0 legacy lab, 1 production ambient/alpha base, 2 additive light pair.
// Ambient and alpha must be written ONCE, not once per two-light shader batch.
float ProductionLightPassMode;
// L19: emission is added ONCE in the ambient/alpha pass; it is independent
// of light eligibility, shadow masks and virtual depth. Map red channel is
// brightness, alpha of source art still limits the final surface composite.
Texture2D EmissionMap;
sampler2D EmissionSampler = sampler_state
{
    Texture = <EmissionMap>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
float4 ProductionEmission; // rgb hue, a strength (0..8)

// Phase 17B: separable Gaussian visibility filter. The source is an existing
// hard-visibility mask; source and destination are always DIFFERENT RTs.
// Bilinear tap pairing reconstructs all integer offsets up to radius 8 using
// only 9 texture fetches per axis, avoiding the visible 17A sparse-tap bands.
Texture2D SoftMaskSource;
sampler2D SoftMaskSampler = sampler_state
{
    Texture = <SoftMaskSource>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
float2 SoftBlurTexelStep;     // (1 / viewport width, 0) OR (0, 1 / height)
float4 SoftBlurOffsets;      // subpixel-accurate Gaussian tap-pair centers
float4 SoftBlurWeights;      // normalized weights for EACH positive/negative tap
float SoftBlurCenterWeight;

// Phase 17C: per-light, per-mask first-hit ray profile. The CPU derives it
// from the SAME polygon fan as the hard mask. R/G encode a 16-bit normalized
// radial distance; point sampling avoids mixing adjacent shadow edges.
Texture2D SoftContactProfile;
sampler2D SoftContactProfileSampler = sampler_state
{
    Texture = <SoftContactProfile>;
    MinFilter = POINT; MagFilter = POINT; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
float SoftContactEnabled;
float2 SoftContactLightCenter;
float SoftContactFanReachPixels;
float SoftContactEmitterRadiusPixels;
float SoftContactMaxBlurPixels;
float2 ViewportPixels;

float ContactTapScale(float2 uv)
{
    if (SoftContactEnabled < 0.5) return 1.0;
    float2 position = uv * ViewportPixels;
    float2 d = position - SoftContactLightCenter;
    // Screen Y is inverted relative to world-space ShadowVisibility angles.
    float angle = atan2(-d.y, d.x);
    float u = frac(angle * 0.15915494309189535 + 1.0);
    float profileU = (floor(u * 1024.0) + 0.5) / 1024.0;
    float2 packed = tex2D(SoftContactProfileSampler, float2(profileU, 0.5)).rg;
    float normalizedHit = (floor(packed.r * 255.0 + 0.5) * 256.0 +
        floor(packed.g * 255.0 + 0.5)) / 65535.0;
    float hit = normalizedHit * SoftContactFanReachPixels;
    float receiver = length(d);
    float pastBlocker = max(0.0, receiver - hit);
    // Project the emitter's apparent radius from the blocker towards the
    // receiver. Max 8px for our bounded Gaussian kernel; 0.75px at contact
    // prevents a sharp visibility discontinuity at polygon silhouette edges.
    float width = min(SoftContactMaxBlurPixels,
        max(0.75, SoftContactEmitterRadiusPixels * pastBlocker / max(2.0, hit)));
    return saturate(width / 8.0);
}

float4 GaussianVisibilityPS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float v = SoftBlurCenterWeight * tex2D(SoftMaskSampler, uv).r;
    float tapScale = ContactTapScale(uv);
    float2 d0 = SoftBlurTexelStep * SoftBlurOffsets.x * tapScale;
    float2 d1 = SoftBlurTexelStep * SoftBlurOffsets.y * tapScale;
    float2 d2 = SoftBlurTexelStep * SoftBlurOffsets.z * tapScale;
    float2 d3 = SoftBlurTexelStep * SoftBlurOffsets.w * tapScale;
    v += SoftBlurWeights.x * (tex2D(SoftMaskSampler, uv + d0).r + tex2D(SoftMaskSampler, uv - d0).r);
    v += SoftBlurWeights.y * (tex2D(SoftMaskSampler, uv + d1).r + tex2D(SoftMaskSampler, uv - d1).r);
    v += SoftBlurWeights.z * (tex2D(SoftMaskSampler, uv + d2).r + tex2D(SoftMaskSampler, uv - d2).r);
    v += SoftBlurWeights.w * (tex2D(SoftMaskSampler, uv + d3).r + tex2D(SoftMaskSampler, uv - d3).r);
    v = saturate(v);
    return float4(v, v, v, 1.0);
}


float3 AmbientRgb;
float ShadingMode;             // Flat=0, Normal=1, Rim=2, Hybrid=3
float NormalStrength;
float RimStrength;
float4 Light0PositionRadius;   // x, y, radius in pixels, virtual height in pixels
float4 Light1PositionRadius;
float4 Light0ColorIntensity;  // RGB and brightness (zero means disabled)
float4 Light1ColorIntensity;

// Stage 8: three bounded convex polygons (rectangle/triangle/trapezoid).
// Two packed xy-pairs per shape; unused fourth vertex of triangle duplicates C.
// The CPU binds these on EVERY draw, including empty slots.
float4 ShadowPoly0AB;
float4 ShadowPoly0CD;
float4 ShadowPoly1AB;
float4 ShadowPoly1CD;
float4 ShadowPoly2AB;
float4 ShadowPoly2CD;
float4 ShadowVertexCounts;      // per polygon slot: 0, 3 or 4
float4 ShadowSlotEnable;       // per polygon slot on/off
float4 ShadowSettings;         // per-light cast0, cast1, receiver, any-blocker
float DebugOutputMode;
float DebugLightIndex;
// Per-wall draw only: x/y mark which overlapping lights are behind this wall.
// z = inside-rim width in pixels; w = rim intensity. Zero x/y for all other
// surfaces. Wall shadows still use PolygonVisibility independently per light.
float4 WallOverlapSettings;
// Per currently shaded wall: x/y = light origin inside its silhouette,
// z/w = wall silhouette center in SCREEN PIXELS. These are reset for every draw.
float4 WallRimContext;
// The exact polygon for the CURRENTLY drawn wall, in screen pixels. This is
// separate from the three global shadow occluders (which affect other surfaces).
// Winding is clockwise in screen coordinates (Y down), so outward is (dy,-dx).
float4 WallRimPolygonAB;
float4 WallRimPolygonCD;
float WallRimPolygonCount;
// Index in the compact active shadow array of the wall currently being drawn.
// -1 for non-wall surfaces. Only that wall is exempt from blocking its OWN rim.
float WallRimSelfSlot;

// A convex polygon is the intersection of its inward-facing half planes.
// Clip the light-to-pixel segment to each half plane; this handles triangle,
// trapezoid and rectangle with the same analytic per-light visibility logic.
float Cross2(float2 a, float2 b) { return a.x * b.y - a.y * b.x; }

float3 ClipEdge(float2 light, float2 pixel, float2 a, float2 b, float3 interval)
{
    if (interval.z < 0.5) return interval;
    float2 edge = b - a;
    float signedStart = Cross2(edge, light - a);
    float gradient = Cross2(edge, pixel - light);
    if (abs(gradient) < 0.000001)
    {
        if (signedStart <= 0.000001) interval.z = 0.0;
        return interval;
    }
    float limit = -signedStart / gradient;
    if (gradient > 0.0) interval.x = max(interval.x, limit);
    else interval.y = min(interval.y, limit);
    if (interval.y < interval.x) interval.z = 0.0;
    return interval;
}

float PointInsidePolygon(float2 p, float2 a, float2 b, float2 c, float2 d, float count)
{
    if (Cross2(b-a,p-a) < -0.00001 || Cross2(c-b,p-b) < -0.00001)
        return 0.0;
    if (count < 3.5)
        return Cross2(a-c,p-c) >= -0.00001 ? 1.0 : 0.0;
    return Cross2(d-c,p-c) >= -0.00001 && Cross2(a-d,p-d) >= -0.00001
        ? 1.0 : 0.0;
}

float PolygonVisibility(float2 light, float2 pixel, float4 ab, float4 cd, float count)
{
    if (count < 2.5) return 1.0;
    float2 a = ab.xy, b = ab.zw, c = cd.xy, d = cd.zw;
    // Self-shadowing and light-inside cases intentionally stay unshadowed in
    // this flat 2D lab, until the explicit depth model is introduced.
    if (PointInsidePolygon(light,a,b,c,d,count) > 0.5 ||
        PointInsidePolygon(pixel,a,b,c,d,count) > 0.5) return 1.0;
    float3 clip = float3(0.0, 1.0, 1.0);
    clip = ClipEdge(light,pixel,a,b,clip);
    clip = ClipEdge(light,pixel,b,c,clip);
    if (count > 3.5)
    {
        clip = ClipEdge(light,pixel,c,d,clip);
        clip = ClipEdge(light,pixel,d,a,clip);
    }
    else clip = ClipEdge(light,pixel,c,a,clip);
    if (clip.z > 0.5 && clip.y > clip.x + 0.00001 &&
        clip.x < 0.99999 && clip.y > 0.00001) return 0.0;
    return 1.0;
}

// One visibility calculation per light, shared by diffuse and back-rim.
// The wall face opts out of regular projected shadows, but its back-rim must
// still trace against OTHER active walls. Skip only its exact compact GPU slot.
// Reusing this visibility avoids doubling the polygon clipping cost on ps_3_0.
float VisibilityForLight(float2 lightPosition, float2 receiverPosition,
    float casts, float backRimReceiver)
{
    if (ShadowSettings.w < 0.5 || casts < 0.5 ||
        (ShadowSettings.z < 0.5 && backRimReceiver < 0.5))
        return 1.0;
    float result = 1.0;
    if (ShadowSlotEnable.x > 0.5 &&
        (backRimReceiver < 0.5 || abs(WallRimSelfSlot - 0.0) > 0.5))
        result = min(result, PolygonVisibility(lightPosition, receiverPosition,
            ShadowPoly0AB, ShadowPoly0CD, ShadowVertexCounts.x));
    if (ShadowSlotEnable.y > 0.5 &&
        (backRimReceiver < 0.5 || abs(WallRimSelfSlot - 1.0) > 0.5))
        result = min(result, PolygonVisibility(lightPosition, receiverPosition,
            ShadowPoly1AB, ShadowPoly1CD, ShadowVertexCounts.y));
    if (ShadowSlotEnable.z > 0.5 &&
        (backRimReceiver < 0.5 || abs(WallRimSelfSlot - 2.0) > 0.5))
        result = min(result, PolygonVisibility(lightPosition, receiverPosition,
            ShadowPoly2AB, ShadowPoly2CD, ShadowVertexCounts.z));
    return result;
}

void EvaluateLight(float2 screenPosition, float3 normal, float2 edgeNormal,
    float edgeEligibility, float useNormals, float useRim,
    float4 positionRadius, float4 colorIntensity, float visibility,
    out float3 diffuseEnergy, out float3 rimEnergy)
{
    float2 toLight = positionRadius.xy - screenPosition;
    float radius = max(1.0, positionRadius.z);
    float distanceToLight = length(toLight);
    float falloff = saturate(1.0 - distanceToLight / radius);
    float3 energy = colorIntensity.rgb * (colorIntensity.a * falloff * falloff * visibility);
    float3 unitLight = normalize(float3(toLight, max(1.0, positionRadius.w)));
    float directional = lerp(1.0, saturate(dot(normal, unitLight)), useNormals * NormalStrength);
    diffuseEnergy = energy * directional;
    float2 planarDirection = toLight / max(0.0001, distanceToLight);
    float facing = saturate(dot(edgeNormal, planarDirection));
    rimEnergy = energy * facing * edgeEligibility * RimStrength * useRim;
}

// Analytic silhouette rim: measure perpendicular distance to the actual
// polygon SEGMENTS, not alpha samples at horizontal/vertical texture offsets.
// x = distance to segment in screen pixels; y = signed facing cosine.
float2 BackRimEdgeSample(float2 pixelPos, float2 a, float2 b,
    float2 lightPos, float2 insideDirection, float originInside)
{
    float2 segment = b - a;
    float lenSquared = max(0.0001, dot(segment, segment));
    float t = saturate(dot(pixelPos - a, segment) / lenSquared);
    float2 nearestPoint = a + t * segment;
    float2 outward = float2(segment.y, -segment.x) * rsqrt(lenSquared);
    // Outside light: use this edge's true outward normal and the light's
    // position relative to its nearest boundary point. This eliminates
    // midpoint-based light-facing errors on angled segments and near corners.
    // Inside light: use its displacement from the polygon centroid as the
    // artistic direction. A truly centered light has no preferred rim side.
    float2 towardLight = originInside > 0.5
        ? insideDirection : (lightPos - nearestPoint);
    float towardLen = length(towardLight);
    float facing = towardLen > 2.0 ? dot(outward, towardLight / towardLen) : 0.0;
    return float2(length(pixelPos - nearestPoint), facing);
}

float BackWallRimFactor(float2 pixelPos, float2 lightPos, float originInside)
{
    if (WallRimPolygonCount < 2.5) return 0.0;
    float2 a = WallRimPolygonAB.xy;
    float2 b = WallRimPolygonAB.zw;
    float2 c = WallRimPolygonCD.xy;
    float2 d = WallRimPolygonCD.zw;
    float2 innerDirection = lightPos - WallRimContext.zw;
    // Find the nearest edge of the ENTIRE polygon, including unlit sides.
    // Also track nearby back-facing edges: at convex corners, a light-facing
    // edge must never smear a highlight onto its shadow-facing neighbor.
    float2 nearest = BackRimEdgeSample(pixelPos, a, b,
        lightPos, innerDirection, originInside);
    float nearestDark = nearest.y <= 0.15 ? nearest.x : 99999.0;
    float2 sample = BackRimEdgeSample(pixelPos, b, c,
        lightPos, innerDirection, originInside);
    if (sample.x < nearest.x) nearest = sample;
    if (sample.y <= 0.15) nearestDark = min(nearestDark, sample.x);
    if (WallRimPolygonCount > 3.5)
    {
        sample = BackRimEdgeSample(pixelPos, c, d,
            lightPos, innerDirection, originInside);
        if (sample.x < nearest.x) nearest = sample;
        if (sample.y <= 0.15) nearestDark = min(nearestDark, sample.x);
        sample = BackRimEdgeSample(pixelPos, d, a,
            lightPos, innerDirection, originInside);
        if (sample.x < nearest.x) nearest = sample;
        if (sample.y <= 0.15) nearestDark = min(nearestDark, sample.x);
    }
    else
    {
        sample = BackRimEdgeSample(pixelPos, c, a,
            lightPos, innerDirection, originInside);
        if (sample.x < nearest.x) nearest = sample;
        if (sample.y <= 0.15) nearestDark = min(nearestDark, sample.x);
    }
    float widthPx = max(0.5, WallOverlapSettings.z);
    // Two edges within ~2px of one another form a corner. Honor the dark
    // side there, rather than leaking across a diagonal/grazing corner.
    if (nearestDark <= nearest.x + 2.0 && nearestDark <= widthPx + 2.0)
        return 0.0;
    float band = 1.0 - smoothstep(0.0, widthPx, nearest.x);
    float facing = smoothstep(0.20, 0.50, nearest.y);
    return band * facing;
}

float3 BackWallLight(float2 screenPosition, float4 positionRadius,
    float4 colorIntensity)
{
    float radius = max(1.0, positionRadius.z);
    float falloff = saturate(1.0 - length(positionRadius.xy - screenPosition) / radius);
    return colorIntensity.rgb * (colorIntensity.a * falloff * falloff);
}

float4 MainPS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 pixelPos = uv * ViewportPixels;
    float visibility0 = VisibilityForLight(Light0PositionRadius.xy, pixelPos,
        ShadowSettings.x, WallOverlapSettings.x);
    float visibility1 = VisibilityForLight(Light1PositionRadius.xy, pixelPos,
        ShadowSettings.y, WallOverlapSettings.y);
    // Production visibility is rasterized by the same unbounded ray fan as
    // ComposeLayer. The two masks exclude only the current surface's own ID.
    if (ProductionMaskMode > 0.5)
    {
        visibility0 = tex2D(ProductionVisibility0Sampler, uv).r;
        visibility1 = tex2D(ProductionVisibility1Sampler, uv).r;
    }

    // Debug views share EXACTLY the same per-light visibility calculation as
    // final shading. They do not apply per-surface normal/rim modifiers.
    if (DebugOutputMode > 0.5 && DebugOutputMode < 1.5)
    {
        float v = DebugLightIndex > 0.5 ? visibility1 : visibility0;
        return float4(v, v, v, 1);
    }

    float4 albedo = tex2D(SpriteTextureSampler, uv) * vertexColor;
    if (ProductionLightPassMode > 0.5 && ProductionLightPassMode < 1.5)
    {
        float mask = tex2D(EmissionSampler, uv).r;
        float3 emitted = ProductionEmission.rgb * ProductionEmission.a * mask * albedo.a;
        return float4(albedo.rgb * AmbientRgb + emitted, albedo.a);
    }
    float4 sampledNormal = tex2D(NormalSampler, uv);
    float3 n = sampledNormal.xyz * 2.0 - 1.0;
    n = normalize(float3(n.xy * NormalStrength, max(0.001, n.z)));
    float4 edge = tex2D(EdgeSampler, uv);
    float2 e = edge.xy * 2.0 - 1.0;
    e = e / max(0.0001, length(e));
    float useNormal = (ShadingMode > 0.5 && ShadingMode < 1.5) || (ShadingMode > 2.5) ? 1.0 : 0.0;
    float useRim = ShadingMode > 1.5 ? 1.0 : 0.0;
    float3 diffuse0, rim0, diffuse1, rim1;
    EvaluateLight(pixelPos, n, e, edge.a, useNormal, useRim,
        Light0PositionRadius, Light0ColorIntensity, visibility0, diffuse0, rim0);
    EvaluateLight(pixelPos, n, e, edge.a, useNormal, useRim,
        Light1PositionRadius, Light1ColorIntensity, visibility1, diffuse1, rim1);

    // When a light is authored BEHIND this intersected wall, it cannot
    // illuminate the visible face. Other lights and ambient are unaffected.
    float back0 = saturate(WallOverlapSettings.x);
    float back1 = saturate(WallOverlapSettings.y);
    diffuse0 *= 1.0 - back0;
    diffuse1 *= 1.0 - back1;
    rim0 *= 1.0 - back0;
    rim1 *= 1.0 - back1;
    float3 backRim = float3(0, 0, 0);
    if (back0 + back1 > 0.5)
    {
        // Reuse the same per-light shadow visibility already computed above.
        // On a back-rim receiver it excludes only this wall's shadow slot and
        // correctly accounts for all other enabled casting occluders.
        float3 lightRims =
            back0 * visibility0 * BackWallRimFactor(pixelPos, Light0PositionRadius.xy,
                WallRimContext.x) * BackWallLight(pixelPos, Light0PositionRadius, Light0ColorIntensity) +
            back1 * visibility1 * BackWallRimFactor(pixelPos, Light1PositionRadius.xy,
                WallRimContext.y) * BackWallLight(pixelPos, Light1PositionRadius, Light1ColorIntensity);
        backRim = WallOverlapSettings.w * lightRims;
    }

    if (DebugOutputMode > 1.5 && DebugOutputMode < 2.5)
        return float4(saturate(AmbientRgb + diffuse0 + diffuse1), 1);
    if (DebugOutputMode > 2.5)
        return float4(saturate(DebugLightIndex > 0.5 ? diffuse1 : diffuse0), 1);

    // Additive pairs must not repeat ambient or write alpha. Light energy is
    // accumulated into the already initialized surface target, preserving
    // transparent artwork and independent shadow/rim visibility per light.
    if (ProductionLightPassMode > 1.5)
    {
        float3 contribution = max(0.0, albedo.rgb * (diffuse0 + diffuse1) +
            albedo.a * (rim0 + rim1 + backRim));
        return float4(contribution, 0.0);
    }
    float3 illumination = AmbientRgb + diffuse0 + diffuse1;
    float3 rgb = saturate(albedo.rgb * illumination + albedo.a * (rim0 + rim1 + backRim));
    return float4(rgb, albedo.a);
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

// Reusable two-pass, full-resolution visibility filter. Production layers
// multiply the SECOND pass directly into their illuminated layer; material
// masks write the second pass back to a separate mask render target.
technique GaussianVisibility
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL GaussianVisibilityPS();
    }
}

// L19: HDR tone map and half-resolution bloom. Operates on the fully composed
// scene BEFORE HUD/UI; the existing SpriteDrawing technique is unchanged.
Texture2D L19SourceTexture;
sampler2D L19SourceSampler = sampler_state
{
    Texture = <L19SourceTexture>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
Texture2D L19BloomTexture;
sampler2D L19BloomSampler = sampler_state
{
    Texture = <L19BloomTexture>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = NONE;
    AddressU = CLAMP; AddressV = CLAMP;
};
float L19Exposure;
float L19Threshold;
float L19BloomGain;
float2 L19BlurStep;

float3 L19Tone(float3 hdr)
{
    // Exponential photographic tonemap: monotone, bounded, no channel clipping
    // before exposure; disabled entirely when HDR is toggled off at the host.
    return saturate(1.0 - exp(-max(hdr, 0.0) * max(0.001, L19Exposure)));
}
float4 L19TonePS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 scene = tex2D(L19SourceSampler, uv);
    return float4(L19Tone(scene.rgb), 1.0);
}
float4 L19ExtractPS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float3 rgb = max(0.0, tex2D(L19SourceSampler, uv).rgb);
    float brightness = max(rgb.r, max(rgb.g, rgb.b));
    float knee = max(0.001, L19Threshold * 0.4);
    float soft = saturate((brightness - L19Threshold + knee) / (2.0 * knee));
    float selected = max(0.0, brightness - L19Threshold) + soft * soft * knee;
    return float4(rgb * (selected / max(0.0001, brightness)), 1.0);
}
float4 L19BlurPS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float3 c = tex2D(L19SourceSampler, uv).rgb * 0.227027;
    float2 s1 = L19BlurStep * 1.384615;
    float2 s2 = L19BlurStep * 3.230769;
    c += (tex2D(L19SourceSampler, uv + s1).rgb +
          tex2D(L19SourceSampler, uv - s1).rgb) * 0.316216;
    c += (tex2D(L19SourceSampler, uv + s2).rgb +
          tex2D(L19SourceSampler, uv - s2).rgb) * 0.070270;
    return float4(c, 1.0);
}
float4 L19CompositePS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float3 scene = tex2D(L19SourceSampler, uv).rgb;
    float3 glow = tex2D(L19BloomSampler, uv).rgb * L19BloomGain;
    return float4(L19Tone(scene + glow), 1.0);
}
// Bloom-only in SDR: retain the original scene color transfer when HDR off.
float4 L19SdrCompositePS(float4 vertexColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float3 scene = tex2D(L19SourceSampler, uv).rgb;
    float3 glow = tex2D(L19BloomSampler, uv).rgb * L19BloomGain;
    return float4(saturate(scene + glow), 1.0);
}
technique L19ToneMap { pass P0 { PixelShader = compile PS_SHADERMODEL L19TonePS(); } }
technique L19BloomExtract { pass P0 { PixelShader = compile PS_SHADERMODEL L19ExtractPS(); } }
technique L19BloomBlur { pass P0 { PixelShader = compile PS_SHADERMODEL L19BlurPS(); } }
technique L19BloomComposite { pass P0 { PixelShader = compile PS_SHADERMODEL L19CompositePS(); } }
technique L19SdrBloomComposite { pass P0 { PixelShader = compile PS_SHADERMODEL L19SdrCompositePS(); } }
