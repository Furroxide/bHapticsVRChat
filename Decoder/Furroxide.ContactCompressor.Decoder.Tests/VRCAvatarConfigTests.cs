using System;
using System.IO;
using OscLib.VRChat;
using Xunit;

namespace Furroxide.ContactCompressor.Tests
{
    public class VRCAvatarConfigTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "bhaptics-avatar-tests-" + Guid.NewGuid().ToString("N"));
        const string Avatar = "avtr_12345678-1234-1234-1234-123456789abc";

        public VRCAvatarConfigTests() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, true);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("../sentinel")]
        [InlineData("..\\sentinel")]
        [InlineData("avtr_../../sentinel")]
        [InlineData("avtr_12345678-1234-1234-1234-123456789abc/../../sentinel")]
        public void InvalidAvatarChangesCannotDeleteFiles(string id)
        {
            string user = Path.Combine(_root, "user");
            Directory.CreateDirectory(Path.Combine(user, "Avatars"));
            string sentinel = Path.Combine(user, "sentinel.json");
            File.WriteAllText(sentinel, "keep");

            VRCAvatarConfig.RemoveFile(id, _root);

            Assert.Equal("keep", File.ReadAllText(sentinel));
        }

        [Fact]
        public void AbsolutePathsCannotEscapeTheAvatarFolder()
        {
            Directory.CreateDirectory(Path.Combine(_root, "user", "Avatars"));
            string sentinel = Path.Combine(_root, "sentinel.json");
            File.WriteAllText(sentinel, "keep");
            VRCAvatarConfig.RemoveFile(Path.Combine(_root, "sentinel"), _root);
            Assert.True(File.Exists(sentinel));
        }

        [Fact]
        public void ValidAvatarChangeRemovesOnlyItsOwnConfigForEachUser()
        {
            foreach (string user in new[] { "first", "second" })
            {
                string folder = Path.Combine(_root, user, "Avatars");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, Avatar + ".json"), "refresh");
                File.WriteAllText(Path.Combine(folder, "other.json"), "keep");
            }

            VRCAvatarConfig.RemoveFile(Avatar, _root);

            foreach (string user in new[] { "first", "second" })
            {
                string folder = Path.Combine(_root, user, "Avatars");
                Assert.False(File.Exists(Path.Combine(folder, Avatar + ".json")));
                Assert.True(File.Exists(Path.Combine(folder, "other.json")));
            }
        }

        [Fact]
        public void MissingOscFolderDoesNotInterruptHaptics()
            => VRCAvatarConfig.RemoveFile(Avatar, Path.Combine(_root, "missing"));
    }
}
