using System;
using System.Globalization;
using System.Threading;
using Xunit;

namespace Furroxide.ContactCompressor.Tests
{
    public class ManifestJsonTests
    {
        const string Sample = @"{
  ""version"": 1,
  ""prefix"": ""bOSC/v3"",
  ""generator"": ""test"",
  ""regions"": [
    { ""id"": ""Torso"", ""axes"": ""XYZ"",
      ""boxExtents"": [0.3858, 0.536679, 0.545549],
      ""regionExtents"": [0.1858, 0.336679, 0.345549],
      ""points"": [ { ""id"": ""VestFront/0"", ""u"": 0.02, ""v"": 1.0, ""w"": 1.0, ""radius"": 0.045 } ] }
  ]
}";

        [Fact]
        public void ParsesAManifest()
        {
            var manifest = ManifestJson.Parse(Sample);

            Assert.Equal(1, manifest.version);
            Assert.Equal("bOSC/v3", manifest.prefix);

            var torso = manifest.Find("Torso");
            Assert.NotNull(torso);
            Assert.Equal(EncoderAxes.XYZ, torso.ParsedAxes);
            Assert.Equal(0.3858f, torso.boxExtents[0], 5);
            Assert.Equal(0.345549f, torso.regionExtents[2], 5);

            var point = Assert.Single(torso.points);
            Assert.Equal("VestFront/0", point.id);
            Assert.Equal(0.02f, point.u, 5);
            Assert.Equal(0.045f, point.radius, 5);
        }

        [Fact]
        public void ParsesTheSameOnACommaDecimalLocale()
        {
            // A German or French Windows install must not read 0.045 as 45.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var manifest = ManifestJson.Parse(Sample);
                Assert.Equal(0.045f, manifest.Find("Torso").points[0].radius, 5);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void RejectsEmptyInput(string input)
        {
            Assert.Throws<ArgumentException>(() => ManifestJson.Parse(input));
        }

        [Theory]
        [InlineData(@"[1,2,3]")]                    // root must be an object
        [InlineData(@"{""version"": 1")]            // unterminated
        [InlineData(@"{} trailing")]                // trailing content
        [InlineData(@"{""a"": 1,}")]                // dangling comma
        [InlineData(@"{""a"" 1}")]                  // missing colon
        public void RejectsMalformedInput(string input)
        {
            Assert.ThrowsAny<Exception>(() => ManifestJson.Parse(input));
        }

        [Fact]
        public void HandlesEscapesAndUnicode()
        {
            // Verbatim, so what appears here is exactly the JSON text: \t, \", \\ and é are
            // JSON escapes for the reader to resolve, not C# ones.
            const string json = @"{""generator"": ""tab:\tquote:\""slash:\\char:é"", ""regions"": []}";

            var manifest = ManifestJson.Parse(json);

            Assert.Equal("tab:\tquote:\"slash:\\char:é", manifest.generator);
            Assert.Empty(manifest.regions);
        }

        /// <summary>
        /// Unity's JsonUtility is what actually writes the manifests users ship, and it does not
        /// format the way the offline generator does: four-space indent, and full round-trip
        /// precision instead of six decimal places. Verbatim sample taken from a real export.
        /// </summary>
        const string UnityWritten = @"{
    ""version"": 1,
    ""prefix"": ""bOSC/v3"",
    ""generator"": ""KhnFuCat [PC]"",
    ""regions"": [
        {
            ""id"": ""Torso"",
            ""axes"": ""XYZ"",
            ""boxExtents"": [
                0.3857998549938202,
                0.46098601818084719,
                0.5511569976806641
            ],
            ""regionExtents"": [
                0.18579985201358796,
                0.2609860301017761,
                0.351157009601593
            ],
            ""points"": [
                {
                    ""id"": ""VestFront/3"",
                    ""u"": 0.9784724712371826,
                    ""v"": 0.9964442253112793,
                    ""w"": 0.9024742841720581,
                    ""radius"": 0.04500000178813934
                }
            ]
        }
    ]
}";

        [Fact]
        public void ReadsWhatUnityActuallyWrites()
        {
            var manifest = ManifestJson.Parse(UnityWritten);

            Assert.Equal("bOSC/v3", manifest.prefix);
            Assert.Equal("KhnFuCat [PC]", manifest.generator);

            var torso = manifest.Find("Torso");
            Assert.NotNull(torso);
            Assert.Equal(EncoderAxes.XYZ, torso.ParsedAxes);
            Assert.Equal(0.185799f, torso.regionExtents[0], 5);
            Assert.Equal(0.260986f, torso.regionExtents[1], 5);

            var point = Assert.Single(torso.points);
            Assert.Equal("VestFront/3", point.id);
            Assert.Equal(0.978472f, point.u, 5);
            Assert.Equal(0.045f, point.radius, 5);

            // And the decoder accepts it end to end.
            var decoder = new ContactCompressorDecoder(manifest);
            Assert.True(decoder.Accept("/avatar/parameters/bOSC/v3/Torso/Xp", 0.7f));
            Assert.False(decoder.Accept("/avatar/parameters/bOSC/v2/VestFront/3/others", 1f));
        }

        [Fact]
        public void ToleratesMissingOptionalFields()
        {
            var manifest = ManifestJson.Parse(@"{""regions"": [ { ""id"": ""Bare"" } ] }");

            Assert.Equal(ContactCompressorManifest.CurrentVersion, manifest.version);
            Assert.Equal(ContactParameterNames.DefaultPrefix, manifest.prefix);

            var region = manifest.Find("Bare");
            Assert.NotNull(region);
            Assert.Empty(region.points);
            Assert.Equal(3, region.boxExtents.Length);
        }
    }
}
