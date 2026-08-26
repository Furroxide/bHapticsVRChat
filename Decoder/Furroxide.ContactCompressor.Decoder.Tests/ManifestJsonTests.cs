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
