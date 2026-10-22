using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebSocketDotNet.Protocol;
using Xunit;

namespace Furroxide.ContactCompressor.Decoder.Tests
{
    public sealed class WebSocketTransportTests
    {
        public static IEnumerable<object[]> FrameCases()
        {
            // Exercise the short, 16-bit and 64-bit wire lengths, including empty
            // frames and both mask states. Every read returns at most one byte.
            foreach (int length in new[] { 0, 2, 125, 126, 65535, 65536 })
            foreach (bool masked in new[] { false, true })
                yield return new object[] { length, masked };
        }

        [Theory]
        [MemberData(nameof(FrameCases))]
        public void SyncReadsAccumulatePartialFieldsAndPreservePayload(int length, bool masked)
        {
            byte[] payload = Payload(length);
            using var stream = new PartialReadStream(Frame(payload, masked));
            AssertFrame(WebSocketFragment.Read(stream), payload);
            Assert.Equal(stream.Length, stream.Position);
        }

        [Theory]
        [MemberData(nameof(FrameCases))]
        public async Task AsyncReadsAccumulatePartialFieldsAndPreservePayload(int length, bool masked)
        {
            byte[] payload = Payload(length);
            using var stream = new PartialReadStream(Frame(payload, masked));
            AssertFrame(await WebSocketFragment.ReadAsync(stream), payload);
            Assert.Equal(stream.Length, stream.Position);
        }

        public static IEnumerable<object[]> TruncatedFrames()
        {
            // EOF in either header byte, either extended length, mask and payload.
            byte[] shortFrame = Frame(Payload(5), false);
            foreach (int length in new[] { 0, 1, 2, shortFrame.Length - 1 })
                yield return new object[] { shortFrame.Take(length).ToArray() };
            byte[] extended16 = Frame(Payload(126), false);
            foreach (int length in new[] { 2, 3, 4, extended16.Length - 1 })
                yield return new object[] { extended16.Take(length).ToArray() };
            byte[] extended64 = Frame(Payload(65536), false);
            foreach (int length in new[] { 2, 3, 5, 9, 10, extended64.Length - 1 })
                yield return new object[] { extended64.Take(length).ToArray() };
            byte[] masked = Frame(Payload(5), true);
            foreach (int length in new[] { 2, 3, 4, 5, 6, masked.Length - 1 })
                yield return new object[] { masked.Take(length).ToArray() };
        }

        [Theory]
        [MemberData(nameof(TruncatedFrames))]
        public void SyncTruncationStopsAtEof(byte[] bytes)
        {
            using var stream = new PartialReadStream(bytes);
            Assert.Throws<EndOfStreamException>(() => WebSocketFragment.Read(stream));
            Assert.InRange(stream.ReadCalls, 1, bytes.Length + 1);
        }

        [Theory]
        [MemberData(nameof(TruncatedFrames))]
        public async Task AsyncTruncationStopsAtEof(byte[] bytes)
        {
            using var stream = new PartialReadStream(bytes);
            await Assert.ThrowsAsync<EndOfStreamException>(() => WebSocketFragment.ReadAsync(stream));
            Assert.InRange(stream.ReadCalls, 1, bytes.Length + 1);
        }

        [Fact]
        public void SyncReadDoesNotConsumeFollowingFrame()
        {
            byte[] first = Payload(126), second = Payload(5);
            byte[] firstFrame = Frame(first, true);
            using var stream = new MemoryStream(firstFrame.Concat(Frame(second, false)).ToArray());
            AssertFrame(WebSocketFragment.Read(stream), first);
            Assert.Equal(firstFrame.Length, stream.Position);
            AssertFrame(WebSocketFragment.Read(stream), second);
        }

        [Fact]
        public async Task AsyncReadDoesNotConsumeFollowingFrame()
        {
            byte[] first = Payload(126), second = Payload(5);
            byte[] firstFrame = Frame(first, true);
            using var stream = new MemoryStream(firstFrame.Concat(Frame(second, false)).ToArray());
            AssertFrame(await WebSocketFragment.ReadAsync(stream), first);
            Assert.Equal(firstFrame.Length, stream.Position);
            AssertFrame(await WebSocketFragment.ReadAsync(stream), second);
        }

        [Fact]
        public void SyncReadPreservesUnderlyingIoFailure()
        {
            var expected = new IOException("Simulated timeout");
            using var stream = new PartialReadStream(Frame(Payload(5), false), expected);
            Assert.Same(expected, Assert.Throws<IOException>(() => WebSocketFragment.Read(stream)));
        }

        [Fact]
        public async Task AsyncReadPreservesUnderlyingIoFailure()
        {
            var expected = new IOException("Simulated timeout");
            using var stream = new PartialReadStream(Frame(Payload(5), false), expected);
            Assert.Same(expected, await Assert.ThrowsAsync<IOException>(() => WebSocketFragment.ReadAsync(stream)));
        }

        [Fact]
        public async Task AsyncReadPreservesCancellation()
        {
            var expected = new OperationCanceledException();
            using var stream = new PartialReadStream(Frame(Payload(5), false), expected);
            Assert.Same(expected, await Assert.ThrowsAsync<OperationCanceledException>(() => WebSocketFragment.ReadAsync(stream)));
        }

        private static void AssertFrame(WebSocketFragment frame, byte[] payload)
        {
            Assert.True(frame.IsFinal);
            Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
            Assert.Equal((ulong)payload.Length, frame.PayloadLength);
            Assert.Equal(payload, frame.Payload);
            Assert.False(frame.IsMasked);
            Assert.Equal(new byte[4], frame.Mask);
        }

        private static byte[] Payload(int length)
            => Enumerable.Range(0, length).Select(i => (byte)((i * 71 + 17) % 256)).ToArray();

        // Construct wire bytes independently of the implementation's serializer.
        private static byte[] Frame(byte[] payload, bool masked)
        {
            var bytes = new List<byte> { 0x82 };
            byte maskFlag = masked ? (byte)0x80 : (byte)0;
            if (payload.Length <= 125)
                bytes.Add((byte)(maskFlag | payload.Length));
            else if (payload.Length <= ushort.MaxValue)
            {
                bytes.Add((byte)(maskFlag | 126));
                bytes.Add((byte)(payload.Length >> 8));
                bytes.Add((byte)payload.Length);
            }
            else
            {
                bytes.Add((byte)(maskFlag | 127));
                for (int shift = 56; shift >= 0; shift -= 8)
                    bytes.Add((byte)((ulong)payload.Length >> shift));
            }
            byte[] mask = { 0x11, 0x22, 0x33, 0x44 };
            if (masked) bytes.AddRange(mask);
            for (int i = 0; i < payload.Length; i++)
                bytes.Add(masked ? (byte)(payload[i] ^ mask[i % 4]) : payload[i]);
            return bytes.ToArray();
        }

        private sealed class PartialReadStream : MemoryStream
        {
            private readonly Exception failure;
            public int ReadCalls { get; private set; }

            public PartialReadStream(byte[] bytes, Exception failure = null) : base(bytes)
                => this.failure = failure;

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCalls++;
                if (ReadCalls == 2 && failure != null) throw failure;
                return base.Read(buffer, offset, Math.Min(count, 1));
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try { return Task.FromResult(Read(buffer, offset, count)); }
                catch (Exception exception) { return Task.FromException<int>(exception); }
            }
        }
    }
}
