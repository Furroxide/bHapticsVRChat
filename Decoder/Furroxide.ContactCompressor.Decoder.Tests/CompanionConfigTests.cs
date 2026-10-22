using System;
using System.IO;
using System.Threading;
using OscLib.Config;
using Xunit;

namespace Furroxide.ContactCompressor.Tests
{
    public class CompanionConfigTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "bhaptics-config-tests-" + Guid.NewGuid().ToString("N"));
        readonly TestConfig _config;
        readonly string _path;

        public CompanionConfigTests()
        {
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, "test.cfg");
            _config = new TestConfig(_path);
        }

        public void Dispose()
        {
            ConfigManager.AllConfigFiles.Remove(_config);
            Directory.Delete(_root, true);
        }

        public class Values : ConfigCategoryValue
        {
            public int Number = 25;
            public bool Enabled = true;
            public override void Clamp() => Number = Math.Max(0, Math.Min(100, Number));
        }

        sealed class TestConfig : ConfigFile
        {
            internal readonly ConfigCategory<Values> First = new ConfigCategory<Values>("First");
            internal readonly ConfigCategory<Values> Second = new ConfigCategory<Values>("Second");
            internal TestConfig(string path) : base(path, false) => Categories.AddRange(new ConfigCategory[] { First, Second });
        }

        void Write(string content) => File.WriteAllText(_path, content);
        const string Valid = "[First]\nNumber = 40\nEnabled = false\n[Second]\nNumber = 60\nEnabled = true\n";

        [Theory]
        [InlineData("[First\nNumber = 12")]
        [InlineData("[First]\nNumber = \"invalid\"")]
        [InlineData("[First]\nNumber = 9223372036854775807")]
        [InlineData("First = true")]
        [InlineData("")]
        public void InvalidStartupKeepsNonNullDefaultsAndPreservesTheUserFile(string invalid)
        {
            Write(invalid);
            _config.Load();
            Assert.NotNull(_config.First.Value);
            Assert.NotNull(_config.Second.Value);
            Assert.Equal(25, _config.First.Value.Number);
            Assert.True(_config.First.Value.Enabled);
            _config.Save(); // ProcessExit calls SaveAll even after a rejected load.
            Assert.Equal(invalid, File.ReadAllText(_path));
        }

        [Theory]
        [InlineData("[First]\nNumber = 80\n[Second]\nEnabled = \"invalid\"")]
        [InlineData("[First]\nNumber = 80\n[Second]\nNumber = 9223372036854775807")]
        [InlineData("[First]\nNumber = 80\n[Second\nNumber = 90")]
        [InlineData("First = true")]
        [InlineData("")]
        public void InvalidReloadDoesNotPartiallyApplyOrOverwriteSettings(string invalid)
        {
            Write(Valid);
            _config.Load();
            var first = _config.First.Value;
            var second = _config.Second.Value;
            Write(invalid);
            Assert.False(_config.TryLoad(false));
            Assert.Same(first, _config.First.Value);
            Assert.Same(second, _config.Second.Value);
            Assert.Equal(40, _config.First.Value.Number);
            Assert.Equal(60, _config.Second.Value.Number);
            _config.Save();
            Assert.Equal(invalid, File.ReadAllText(_path));

            Write("[First]\nNumber = 90\n[Second]\nNumber = 30\n");
            Assert.True(_config.TryLoad(false));
            Assert.Equal(90, _config.First.Value.Number);
            Assert.Equal(30, _config.Second.Value.Number);
        }

        [Fact]
        public void ValidPartialFileSuppliesDefaultsAndClampsBeforeApplying()
        {
            Write("[First]\nNumber = 900\n");
            _config.Load();
            Assert.Equal(100, _config.First.Value.Number);
            Assert.Equal(25, _config.Second.Value.Number);
            Assert.True(_config.Second.Value.Enabled);
        }

        [Fact]
        public void LockedFileKeepsLastValuesAndRecoversAfterUnlock()
        {
            Write(Valid);
            _config.Load();
            Write("[First]\nNumber = 75\n");
            using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(_config.TryLoad(false));
                Assert.Equal(40, _config.First.Value.Number);
            }
            Assert.True(_config.TryLoad(false));
            Assert.Equal(75, _config.First.Value.Number);
        }

        [Fact]
        public void WatcherDoesNotReadBeforeDerivedCategoriesAreReady()
        {
            Write(Valid);
            Assert.False(_config.TryLoad(false));
            Assert.Equal(25, _config.First.Value.Number);
            _config.Load();
            Assert.Equal(40, _config.First.Value.Number);
        }

        [Fact]
        public void WatcherSurvivesMalformedEditAndReloadsCorrectedFile()
        {
            Write(Valid);
            _config.Load();
            int callbacks = 0;
            _config.OnFileModified = () => Interlocked.Increment(ref callbacks);
            var watcher = new ConfigFileWatcher(_config);
            try
            {
                const string invalid = "[First\nNumber = nope\n";
                Write(invalid);
                Thread.Sleep(400);
                Assert.Equal(40, _config.First.Value.Number);
                Assert.Equal(0, Volatile.Read(ref callbacks));
                Assert.Equal(invalid, File.ReadAllText(_path));
                Write("[First]\nNumber = 88\n");
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref callbacks) > 0, 5000));
                Assert.Equal(88, _config.First.Value.Number);
            }
            finally { watcher.EndInit(); }
        }

        [Fact]
        public void AtomicReplacementReloadsWithoutOverwritingTheReplacement()
        {
            Write(Valid);
            _config.Load();
            using var ready = new ManualResetEventSlim();
            _config.OnFileModified = ready.Set;
            var watcher = new ConfigFileWatcher(_config);
            try
            {
                string replacement = Path.Combine(_root, "replacement.tmp");
                const string content = "[First]\nNumber = 73\n";
                File.WriteAllText(replacement, content);
                File.Move(replacement, _path, true);
                Assert.True(ready.Wait(5000));
                Assert.Equal(73, _config.First.Value.Number);
                Assert.Equal(content, File.ReadAllText(_path));
            }
            finally { watcher.EndInit(); }
        }

        [Fact]
        public void MissingFileDuringReplacementDoesNotRecreateOrResetIt()
        {
            Write(Valid);
            _config.Load();
            File.Delete(_path);
            Assert.False(_config.TryLoad(false));
            Assert.False(File.Exists(_path));
            Assert.Equal(40, _config.First.Value.Number);
        }

        [Fact]
        public void ReloadCallbackFailureIsContained()
        {
            _config.OnFileModified = () => throw new InvalidOperationException("connection unavailable");
            _config.OnFileModified_SafeInvoke();
        }

        [Theory]
        [InlineData("[First]\nNumber = 87\n")]
        [InlineData("[First\nNumber = nope\n")]
        public void SaveBeforeWatcherObservesAnExternalEditPreservesIt(string externalEdit)
        {
            Write(Valid);
            _config.Load();
            Write(externalEdit);
            _config.Save();
            Assert.Equal(externalEdit, File.ReadAllText(_path));
            Assert.Equal(40, _config.First.Value.Number);
        }

        [Fact]
        public void SaveDoesNotRecreateAFileTemporarilyRenamedByAnEditor()
        {
            Write(Valid);
            _config.Load();
            string renamed = Path.Combine(_root, "previous.tmp");
            File.Move(_path, renamed);
            _config.Save();
            Assert.False(File.Exists(_path));
            Assert.Equal(Valid, File.ReadAllText(renamed));
        }

        [Fact]
        public void SaveCanPersistChangesWhenTheDiskStillMatchesTheLoadedSnapshot()
        {
            Write(Valid);
            _config.Load();
            _config.First.Value.Number = 72;
            _config.Save();
            _config.Load();
            Assert.Equal(72, _config.First.Value.Number);
            _config.First.Value.Number = 61;
            _config.Save();
            _config.Load();
            Assert.Equal(61, _config.First.Value.Number);
        }

        [Fact]
        public void MissingStartupFileCreatesUsableDefaults()
        {
            _config.Load();
            Assert.True(File.Exists(_path));
            Assert.Equal(25, _config.First.Value.Number);
            Assert.True(_config.TryLoad(false));
        }
    }
}
