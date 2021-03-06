using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bencodex.Types;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Net.Messages;
using NetMQ;
using Xunit;

namespace Libplanet.Tests.Net.Messages
{
    public class RecentStatesTest
    {
        [Fact]
        public void Constructor()
        {
            var emptyBlockStates = ImmutableDictionary<
                HashDigest<SHA256>,
                IImmutableDictionary<string, IValue>
            >.Empty;
            Assert.Throws<ArgumentNullException>(() =>
                new RecentStates(
                    default,
                    default,
                    default,
                    null,
                    ImmutableDictionary<string, IImmutableList<HashDigest<SHA256>>>.Empty
                )
            );
            Assert.Throws<ArgumentNullException>(() =>
                new RecentStates(
                    default,
                    default,
                    default,
                    emptyBlockStates,
                    null
                )
            );
        }

        [Fact]
        public void DataFrames()
        {
            // This test lengthens long... Please read the brief description of the entire payload
            // structure from the comment in the RecentStates.DataFrames property code.
            ISet<Address> accounts = Enumerable.Repeat(0, 5).Select(_ =>
                new PrivateKey().ToAddress()
            ).ToHashSet();
            int accountsCount = accounts.Count;
            var privKey = new PrivateKey();

            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            var randomBytesBuffer = new byte[HashDigest<SHA256>.Size];
            (HashDigest<SHA256>, IImmutableDictionary<string, IValue>)[] blockStates =
                accounts.SelectMany(address =>
                {
                    rng.GetNonZeroBytes(randomBytesBuffer);
                    var blockHash1 = new HashDigest<SHA256>(randomBytesBuffer);
                    rng.GetNonZeroBytes(randomBytesBuffer);
                    var blockHash2 = new HashDigest<SHA256>(randomBytesBuffer);
                    IImmutableDictionary<string, IValue> emptyState =
                        ImmutableDictionary<string, IValue>.Empty;
                    return new[]
                    {
                        (
                            blockHash1,
                            emptyState.Add(
                                address.ToHex().ToLowerInvariant(),
                                (Text)$"A:{blockHash1}:{address}")
                        ),
                        (
                            blockHash2,
                            emptyState.Add(
                                address.ToHex().ToLowerInvariant(),
                                (Text)$"B:{blockHash2}:{address}")
                        ),
                    };
                }).ToArray();
            IImmutableDictionary<HashDigest<SHA256>, IImmutableDictionary<string, IValue>>
                compressedBlockStates = blockStates.Where(
                    (_, i) => i % 2 == 1
                ).ToImmutableDictionary(p => p.Item1, p => p.Item2);
            HashDigest<SHA256> blockHash = blockStates.Last().Item1;

            IImmutableDictionary<string, IImmutableList<HashDigest<SHA256>>> stateRefs =
                accounts.Select(a =>
                {
                    var states = blockStates
                        .Where(pair => pair.Item2.ContainsKey(a.ToHex().ToLowerInvariant()))
                        .Select(pair => pair.Item1)
                        .ToImmutableList();
                    return new KeyValuePair<string, IImmutableList<HashDigest<SHA256>>>(
                        a.ToHex().ToLowerInvariant(), states);
                }).ToImmutableDictionary();

            RecentStates reply =
                new RecentStates(blockHash, -1, 1, compressedBlockStates, stateRefs);

            var versionSigner = new PrivateKey();
            AppProtocolVersion version = AppProtocolVersion.Sign(versionSigner, 1);
            Peer peer = new BoundPeer(privKey.PublicKey, new DnsEndPoint("0.0.0.0", 1234));

            NetMQMessage msg = reply.ToNetMQMessage(privKey, peer, version);
            const int headerSize = Message.CommonFrames;  // version, type, peer, sig
            int stateRefsOffset = headerSize + 3;  // blockHash, offsetHash, iteration
            int blockStatesOffset = stateRefsOffset + 1 + (accountsCount * 4);
            Assert.Equal(
               blockStatesOffset + 1 + (compressedBlockStates.Count * 4),
               msg.FrameCount
            );
            Assert.Equal(blockHash, new HashDigest<SHA256>(msg[headerSize].Buffer));
            Assert.Equal(accountsCount, msg[stateRefsOffset].ConvertToInt32());
            for (int i = 0; i < accountsCount; i++)
            {
                int offset = stateRefsOffset + 1 + (i * 4);
                Assert.Equal(Address.Size * 2, msg[offset].BufferSize);
                var key = Encoding.UTF8.GetString(msg[offset].Buffer);
                Assert.Contains(new Address(key), accounts);

                Assert.Equal(4, msg[offset + 1].BufferSize);
                Assert.Equal(2, msg[offset + 1].ConvertToInt32());

                Assert.Equal(HashDigest<SHA256>.Size, msg[offset + 2].BufferSize);
                Assert.Equal(stateRefs[key][0], new HashDigest<SHA256>(msg[offset + 2].Buffer));
                Assert.Equal(HashDigest<SHA256>.Size, msg[offset + 3].BufferSize);
                Assert.Equal(stateRefs[key][1], new HashDigest<SHA256>(msg[offset + 3].Buffer));

                accounts.Remove(new Address(key));
            }

            Assert.Empty(accounts);
            Assert.Equal(compressedBlockStates.Count, msg[blockStatesOffset].ConvertToInt32());

            var codec = new Bencodex.Codec();
            for (int i = 0; i < compressedBlockStates.Count; i++)
            {
                int offset = blockStatesOffset + 1 + (i * 4);

                var hash = new HashDigest<SHA256>(msg[offset].Buffer);
                Assert.Contains(hash, compressedBlockStates);
                Assert.Equal(1, msg[offset + 1].ConvertToInt32());

                var addr = new Address(Encoding.UTF8.GetString(msg[offset + 2].Buffer));
                Assert.Equal(new Address(compressedBlockStates[hash].Keys.First()), addr);

                using (var compressed = new MemoryStream(msg[offset + 3].Buffer))
                using (var df = new DeflateStream(compressed, CompressionMode.Decompress))
                using (var decompressed = new MemoryStream())
                {
                    df.CopyTo(decompressed);
                    decompressed.Seek(0, SeekOrigin.Begin);
                    string state = ((Text)codec.Decode(decompressed)).Value;
                    Assert.Equal($"B:{hash}:{addr}", state);
                }
            }

            msg = reply.ToNetMQMessage(privKey, peer, version);
            var parsed = new RecentStates(msg.Skip(headerSize).ToArray());
            Assert.Equal(blockHash, parsed.BlockHash);
            Assert.False(parsed.Missing);
            Assert.Equal(compressedBlockStates, parsed.BlockStates);
            Assert.Equal(stateRefs, parsed.StateReferences);

            RecentStates missing = new RecentStates(blockHash, -1, 1, null, null);
            msg = missing.ToNetMQMessage(privKey, peer, version);
            Assert.Equal(blockHash, new HashDigest<SHA256>(msg[headerSize].Buffer));
            Assert.Equal(-1, msg[stateRefsOffset].ConvertToInt32());

            parsed = new RecentStates(
                missing.ToNetMQMessage(privKey, peer, version).Skip(headerSize).ToArray());
            Assert.Equal(blockHash, parsed.BlockHash);
            Assert.True(parsed.Missing);
        }
    }
}
