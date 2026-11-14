// VirtualVenues World Wind: the world's wind (WorldWind.cs, on a Wind Zone), for any shader. Include it:
//   #include "Packages/com.virtualvenues.sdk/Runtime/Shaders/VVWind.hlsl"
// The World Wind component sets the two globals below every frame, in the Editor too. With no World Wind (none in the
// scene, or it is off) both are all 0: VVHasWind() is false and the helpers return the fallback you pass in.
// Plain globals, not material properties: never declare them inside CBUFFER_START(UnityPerMaterial), so the SRP
// Batcher is not touched. Don't declare _VVWind / _VVWindGust yourself in a shader that includes this file.
#ifndef VV_WIND_INCLUDED
#define VV_WIND_INCLUDED

float4 _VVWind;     // xy = where the wind blows TO (world x, z; length 1), z = power (Wind Zone Main: 1 normal, 0 none, 2 double), w = 1 with a World Wind, else 0
float4 _VVWindGust; // x = turbulence, y = pulse magnitude, z = pulse frequency (Wind Zone values), w = 0

// True when a World Wind is on in this world.
bool VVHasWind()
{
    return _VVWind.w > 0.5;
}

// Where the wind blows to, world (x, z), length 1. fallback: your shader's own direction when there is no World Wind.
float2 VVWindDirection(float2 fallback)
{
    return VVHasWind() ? _VVWind.xy : fallback;
}

// Wind power: 1 = normal, 0 = none, 2 = double. fallback: your shader's own power when there is no World Wind.
float VVWindPower(float fallback)
{
    return VVHasWind() ? _VVWind.z : fallback;
}

// Gusts from the Wind Zone, for shaders that want them: x = turbulence, y = pulse magnitude, z = pulse frequency.
// All 0 with no World Wind.
float3 VVWindGust()
{
    return _VVWindGust.xyz;
}

// Shader Graph Custom Function (File mode, name "VVWind"): your fallback direction and power in; the world's wind out.
// HasWind = 1 with a World Wind, else 0.
void VVWind_float(float2 FallbackDirection, float FallbackPower, out float2 Direction, out float Power, out float3 Gust, out float HasWind)
{
    Direction = VVWindDirection(FallbackDirection);
    Power = VVWindPower(FallbackPower);
    Gust = VVWindGust();
    HasWind = VVHasWind() ? 1.0 : 0.0;
}

#endif
