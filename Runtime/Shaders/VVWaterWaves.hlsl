// VirtualVenues Water Waves: the waves boats ride (WaterWaves.cs). Include it in a water shader so the surface
// shows the same waves:  #include "Packages/com.virtualvenues.sdk/Runtime/Shaders/VVWaterWaves.hlsl"
// The Renderer's WaterWaves component sets these every frame; a Renderer without one gets zeros = flat water.
// Keep VVWaterWaves() identical to WaterWaves.Evaluate, or boats bob out of step with the water.
#ifndef VV_WATER_WAVES
#define VV_WATER_WAVES

float4 _VVWaveA; // main swell:  xy = wave vector (rad/m), z = amplitude (m), w = phase (rad)
float4 _VVWaveB; // cross swell: same layout

// Height above the still water line at world xz, and its slope (dh/dx, dh/dz).
void VVWaterWaves(float2 xz, out float height, out float2 slope)
{
    float pa = dot(_VVWaveA.xy, xz) - _VVWaveA.w;
    float pb = dot(_VVWaveB.xy, xz) - _VVWaveB.w;
    height = _VVWaveA.z * sin(pa) + _VVWaveB.z * sin(pb);
    slope = _VVWaveA.z * cos(pa) * _VVWaveA.xy + _VVWaveB.z * cos(pb) * _VVWaveB.xy;
}

// Shader Graph Custom Function (File mode, name "VVWaterWaves"): world position in; wave height (m) and world normal out.
void VVWaterWaves_float(float3 PositionWS, out float Height, out float3 NormalWS)
{
    float2 slope;
    VVWaterWaves(PositionWS.xz, Height, slope);
    NormalWS = normalize(float3(-slope.x, 1.0, -slope.y));
}

#endif
