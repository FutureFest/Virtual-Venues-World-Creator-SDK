// VirtualVenues Water Waves: the waves boats ride (WaterWaves.cs). Include it in a water shader so the surface
// shows the same waves:  #include "Packages/com.virtualvenues.sdk/Runtime/Shaders/VVWaterWaves.hlsl"
// The Renderer's WaterWaves component sets these every frame; a Renderer without one gets zeros = flat water.
// That holds for the vectors and floats only: an unset TEXTURE is not black (it is whatever the engine binds), so the
// wake texture is never sampled unless WaterWaves handed this Renderer one (_VVWakeRect.z > 0).
// Keep VVWaterWaves() identical to WaterWaves.Evaluate, or boats bob out of step with the water.
#ifndef VV_WATER_WAVES
#define VV_WATER_WAVES

float4 _VVWaveA; // main swell:  xy = wave vector (rad/m), z = amplitude (m), w = phase (rad)
float4 _VVWaveB; // cross swell: same layout

// Boat wakes (Play mode; black + zeros otherwise): R = ripple height (m), G = foam 0..1, one texel per simulation cell.
Texture2D _VVWakeTex;
SamplerState vv_linear_clamp_sampler;
float4 _VVWakeRect; // xy = world xz of the texture's corner, zw = 1 / its size in metres
float _VVWakeCell;  // metres per texel

float2 VVWakeSample(float2 xz)
{
    if (_VVWakeRect.z <= 0.0) { return 0.0; } // no wake on this Renderer (no WaterWaves, wakes off, or not playing)
    return _VVWakeTex.SampleLevel(vv_linear_clamp_sampler, (xz - _VVWakeRect.xy) * _VVWakeRect.zw, 0).rg;
}

// Foam left by boats at world xz, 0..1. Add it to the water's own foam.
float VVWakeFoam(float2 xz)
{
    return VVWakeSample(xz).g;
}

// Height above the still water line at world xz, and its slope (dh/dx, dh/dz): the swells plus boat wakes.
void VVWaterWaves(float2 xz, out float height, out float2 slope)
{
    float pa = dot(_VVWaveA.xy, xz) - _VVWaveA.w;
    float pb = dot(_VVWaveB.xy, xz) - _VVWaveB.w;
    height = _VVWaveA.z * sin(pa) + _VVWaveB.z * sin(pb);
    slope = _VVWaveA.z * cos(pa) * _VVWaveA.xy + _VVWaveB.z * cos(pb) * _VVWaveB.xy;

    if (_VVWakeRect.z <= 0.0) { return; }
    float c = max(_VVWakeCell, 0.001);
    height += VVWakeSample(xz).r;
    slope += float2(VVWakeSample(xz + float2(c, 0)).r - VVWakeSample(xz - float2(c, 0)).r,
                    VVWakeSample(xz + float2(0, c)).r - VVWakeSample(xz - float2(0, c)).r) / (2.0 * c);
}

// Shader Graph Custom Function (File mode, name "VVWaterWaves"): world position in; wave height (m) and world normal out.
void VVWaterWaves_float(float3 PositionWS, out float Height, out float3 NormalWS)
{
    float2 slope;
    VVWaterWaves(PositionWS.xz, Height, slope);
    NormalWS = normalize(float3(-slope.x, 1.0, -slope.y));
}

#endif
