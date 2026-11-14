using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVenues.WorldCreator;

namespace VirtualVenues.Tests
{
    public class WorldWindTests
    {
        private const float Tolerance = 1e-4f;
        private readonly List<GameObject> _made = new List<GameObject>();

        private static Vector4 Wind => Shader.GetGlobalVector(WorldWind.WindId);
        private static Vector4 Gust => Shader.GetGlobalVector(WorldWind.GustId);

        [TearDown] public void TearDown()
        {
            foreach (GameObject go in _made) { if (go != null) { Object.DestroyImmediate(go); } }
            _made.Clear();
        }

        // Built switched off, so the values are in place before OnEnable sets the globals.
        private WorldWind Make(string name, float yaw, float main)
        {
            var go = new GameObject(name);
            _made.Add(go);
            go.SetActive(false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            WorldWind wind = go.AddComponent<WorldWind>();
            wind.Zone.windMain = main;
            go.SetActive(true);
            return wind;
        }

        private static void AssertVector(Vector4 actual, Vector4 expected)
        {
            for (int i = 0; i < 4; i++) { Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Tolerance), $"component {i} of {actual}"); }
        }

        [Test] public void AddingWorldWindAddsADirectionalWindZone()
        {
            WorldWind wind = Make("Wind", 0f, 1f);
            Assert.That(wind.GetComponent<WindZone>(), Is.Not.Null);
            Assert.That(wind.Zone.mode, Is.EqualTo(WindZoneMode.Directional));
        }

        [Test] public void SetsWindFromTheArrowAndMain()
        {
            WorldWind wind = Make("Wind", 90f, 1.5f); // arrow to world +X = east
            AssertVector(Wind, new Vector4(1f, 0f, 1.5f, 1f));

            wind.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            wind.Zone.windMain = 2f;
            wind.Zone.windTurbulence = 0.3f;
            wind.Zone.windPulseMagnitude = 0.6f;
            wind.Zone.windPulseFrequency = 0.2f;
            wind.Push();
            AssertVector(Wind, new Vector4(0.70710677f, 0.70710677f, 2f, 1f));
            AssertVector(Gust, new Vector4(0.3f, 0.6f, 0.2f, 0f));
        }

        [Test] public void OffOrGoneSetsBothToZero()
        {
            WorldWind wind = Make("Wind", 0f, 1f);
            wind.enabled = false;
            AssertVector(Wind, Vector4.zero);
            AssertVector(Gust, Vector4.zero);

            wind.enabled = true;
            AssertVector(Wind, new Vector4(0f, 1f, 1f, 1f));

            Object.DestroyImmediate(wind.gameObject);
            AssertVector(Wind, Vector4.zero);
            AssertVector(Gust, Vector4.zero);
        }

        [Test] public void LastEnabledWindIsUsedAndTheOtherTakesOver()
        {
            WorldWind north = Make("North", 0f, 1f);
            WorldWind east = Make("East", 90f, 0.5f);
            Assert.That(WorldWind.Active, Is.EqualTo(east));
            AssertVector(Wind, new Vector4(1f, 0f, 0.5f, 1f));

            east.enabled = false;
            Assert.That(WorldWind.Active, Is.EqualTo(north));
            AssertVector(Wind, new Vector4(0f, 1f, 1f, 1f));
        }

        [Test] public void PackKeepsTheHeadingLengthOneAndPowerNotBelowZero()
        {
            // Tilted: only the heading counts.
            AssertVector(WorldWind.PackWind(Quaternion.Euler(60f, 0f, 0f) * Vector3.forward, 1f), new Vector4(0f, 1f, 1f, 1f));
            // Straight down has no heading: north.
            AssertVector(WorldWind.PackWind(Vector3.down, 1f), new Vector4(0f, 1f, 1f, 1f));
            // Negative Main = no wind, not reversed wind.
            Assert.That(WorldWind.PackWind(Vector3.forward, -2f).z, Is.EqualTo(0f));
            Vector4 any = WorldWind.PackWind(new Vector3(3f, 1f, -4f), 1f);
            Assert.That(new Vector2(any.x, any.y).magnitude, Is.EqualTo(1f).Within(Tolerance));
            AssertVector(WorldWind.PackGust(-1f, 0.5f, 0.1f), new Vector4(0f, 0.5f, 0.1f, 0f));
        }
    }
}
