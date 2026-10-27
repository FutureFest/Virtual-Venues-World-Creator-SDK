using NUnit.Framework;
using UnityEngine;
using VirtualVenues.WorldCreator;
using Screen = VirtualVenues.WorldCreator.Screen;

namespace VirtualVenues.Tests
{
    public class StageStreamsTests
    {
        [Test] public void LegacyNamesKeepTheirNumbers()
        {
            Assert.That(StageStreams.Parse(""), Is.EqualTo(0));
            Assert.That(StageStreams.Parse(null), Is.EqualTo(0));
            Assert.That(StageStreams.Parse("Artist"), Is.EqualTo(0));
            Assert.That(StageStreams.Parse("Visuals"), Is.EqualTo(1));
            Assert.That(StageStreams.Parse("7"), Is.EqualTo(7));
            Assert.That(StageStreams.Parse("-3"), Is.EqualTo(0));
            Assert.That(StageStreams.Parse("junk"), Is.EqualTo(0));
        }

        // Old UPC builds read anything but "Visuals" as Artist, so 0 and 1 must keep their names.
        [Test] public void FormatRoundTripsAndKeepsOldNames()
        {
            Assert.That(StageStreams.Format(0), Is.EqualTo("Artist"));
            Assert.That(StageStreams.Format(1), Is.EqualTo("Visuals"));
            for (int i = 0; i < 12; i++) { Assert.That(StageStreams.Parse(StageStreams.Format(i)), Is.EqualTo(i)); }
        }

        [Test] public void LayoutConfigureSetsStreamIndex()
        {
            GameObject go = new GameObject("StageStreamsTest", typeof(AudioSource));
            try
            {
                go.AddComponent<Screen>().Configure(2, "4");
                go.AddComponent<Speaker>().Configure(2, "Visuals", 0);
                go.AddComponent<Artist>().Configure(2, Vector3.zero, "3");
                Assert.That(go.GetComponent<Screen>().StreamIndex, Is.EqualTo(4));
                Assert.That(go.GetComponent<Speaker>().StreamIndex, Is.EqualTo(1));
                Assert.That(go.GetComponent<Artist>().StreamIndex, Is.EqualTo(3));
                Assert.That(go.GetComponent<Screen>().StageIndex, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
