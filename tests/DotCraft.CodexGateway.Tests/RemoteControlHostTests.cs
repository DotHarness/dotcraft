using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.CodexGateway.Relay;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class RemoteControlHostTests
{
    [Fact]
    public async Task PairingSeparatesCredentialsAndReusesOnlyTheCurrentAccountEnrollment()
    {
        await using var relay = await RelayFixture.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var directory = Path.Combine(Path.GetTempPath(), "dotcraft-codex-" + Guid.NewGuid().ToString("N"));
        var forced = false;
        var account = "one";
        var options = new RemoteControlHostOptions { StateDirectory = directory, BackendBaseUri = relay.Backend };
        RemoteControlSessionFactory sessions = (_, _) => throw new InvalidOperationException("No RPC expected.");
        Task<RemoteControlCredentials> Credentials(bool force, CancellationToken _)
        {
            forced |= force;
            return Task.FromResult(new RemoteControlCredentials(forced ? "fresh-access" : "expired-access", account));
        }
        try
        {
            string installation;
            await using (var host = new RemoteControlHost(options, Credentials, sessions))
            {
                installation = host.Status.InstallationId;
                var pairing = await host.PairAsync(timeout.Token);
                Assert.True(await host.IsPairingClaimedAsync(pairing, timeout.Token));
                Assert.True(forced);
                Assert.Throws<IOException>(() => new RemoteControlHost(options, Credentials, sessions));
            }
            await using (var host = new RemoteControlHost(options, Credentials, sessions))
            {
                Assert.Equal(installation, host.Status.InstallationId);
                await host.PairAsync(timeout.Token);
            }
            account = "two";
            await using (var host = new RemoteControlHost(options, Credentials, sessions))
                Assert.Equal("environment-two", (await host.PairAsync(timeout.Token)).EnvironmentId);

            var requests = relay.Requests.ToArray();
            Assert.Equal(3, requests.Count(request => request.Action == "enroll"));
            var refresh = Assert.Single(requests, request => request.Action == "refresh");
            Assert.Equal("Bearer fresh-access", refresh.Headers["Authorization"]);
            Assert.Equal("one", refresh.Headers["chatgpt-account-id"]);
            Assert.Equal(installation, refresh.Headers["x-codex-installation-id"]);
            Assert.All(requests.Where(request => request.Action.StartsWith("pair")), request =>
            {
                Assert.StartsWith("Bearer relay-", request.Headers["Authorization"]);
                Assert.False(request.Headers.ContainsKey("chatgpt-account-id"));
            });
            var state = await File.ReadAllTextAsync(Path.Combine(directory, "host.json"), timeout.Token);
            Assert.DoesNotContain("relay-one", state);
            Assert.DoesNotContain("fresh-access", state);
            Assert.DoesNotContain("pairing-secret", state);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RelayIsolatesStreamsAndReplaysOnlyUnacknowledgedChunksWithoutReexecutingInput()
    {
        await using var relay = await RelayFixture.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var directory = Path.Combine(Path.GetTempPath(), "dotcraft-codex-" + Guid.NewGuid().ToString("N"));
        var received = new ConcurrentQueue<(RemoteControlPeer Peer, JsonElement Message)>();
        var disposed = 0;
        var options = new RemoteControlHostOptions { StateDirectory = directory, BackendBaseUri = relay.Backend };
        var host = new RemoteControlHost(options,
            (_, _) => Task.FromResult(new RemoteControlCredentials("fresh-access", "one")),
            (peer, send) => new EchoSession(peer, send, received, () => Interlocked.Increment(ref disposed)));
        var running = host.RunAsync(ct);
        try
        {
            var first = await relay.ConnectAsync(ct);
            Assert.Equal("Bearer relay-one", first.Headers["Authorization"]);
            Assert.Equal("3", first.Headers["x-codex-protocol-version"]);
            Assert.Equal(host.Status.InstallationId, first.Headers["x-codex-installation-id"]);
            var a = new RemoteControlPeer("phone", "a");
            var b = new RemoteControlPeer("phone", "b");
            await first.SendAsync(Envelope(a, 1, new { id = 99, method = "ignored" }), ct);
            await first.SendAsync(Ping(a, "before-initialize"), ct);
            var unknown = await first.ReadAsync(ct);
            Assert.Equal("unknown", unknown.GetProperty("status").GetString());
            await AckAsync(first, a, unknown.GetProperty("seq_id").GetUInt64(), null, ct);
            await first.SendAsync(Envelope(a, 1, new { id = 1, method = "initialize" }), ct);
            await first.SendAsync(Envelope(b, 1, new { id = 1, method = "initialize" }), ct);
            for (var index = 0; index < 2; index++)
            {
                var reply = await first.ReadAsync(ct);
                Assert.Equal(1, reply.GetProperty("message").GetProperty("id").GetInt32());
                await AckAsync(first, new("phone", reply.GetProperty("stream_id").GetString()!),
                    reply.GetProperty("seq_id").GetUInt64(), null, ct);
            }
            await first.SendAsync(Envelope(a, 2, new { id = 2, method = "echo" }), ct);
            await first.SendAsync(Envelope(a, 2, new { id = 2, method = "echo" }), ct);
            await first.SendAsync(Envelope(a, 3, new { id = 3, method = "echo" }), ct);
            for (var index = 0; index < 2; index++)
            {
                var reply = await first.ReadAsync(ct);
                Assert.Equal(index + 2, reply.GetProperty("message").GetProperty("id").GetInt32());
                await AckAsync(first, a, reply.GetProperty("seq_id").GetUInt64(), null, ct);
            }
            var text = new string('x', 240 * 1024);
            var raw = JsonSerializer.SerializeToUtf8Bytes(new { id = 4, method = "echo", @params = new { text } });
            var count = (raw.Length + RelayFrames.TargetChunkBytes - 1) / RelayFrames.TargetChunkBytes;
            for (var index = 0; index < count; index++)
            {
                var chunk = new
                {
                    type = "client_message_chunk", client_id = a.ClientId, stream_id = a.StreamId,
                    seq_id = 4, segment_id = index, segment_count = count, message_size_bytes = raw.Length,
                    message_chunk_base64 = Convert.ToBase64String(raw, index * RelayFrames.TargetChunkBytes,
                        Math.Min(RelayFrames.TargetChunkBytes, raw.Length - index * RelayFrames.TargetChunkBytes)),
                    cursor = "large-input"
                };
                await first.SendAsync(chunk, ct);
                if (index == 0)
                    await first.SendAsync(chunk, ct);
            }
            var chunks = new List<JsonElement>();
            var firstChunk = await first.ReadAsync(ct);
            chunks.Add(firstChunk);
            var outgoingCount = firstChunk.GetProperty("segment_count").GetInt32();
            for (var index = 1; index < outgoingCount; index++)
                chunks.Add(await first.ReadAsync(ct));
            Assert.All(chunks, chunk => Assert.True(JsonSerializer.SerializeToUtf8Bytes(chunk).Length <= RelayFrames.MaxEnvelopeBytes));
            var sequence = firstChunk.GetProperty("seq_id").GetUInt64();
            await AckAsync(first, a, sequence, 0, ct);
            await first.SendAsync(Ping(b, "after-chunk-ack"), ct);
            Assert.Equal("pong", (await first.ReadAsync(ct)).GetProperty("type").GetString());
            first.Abort();

            var second = await relay.ConnectAsync(ct);
            Assert.Equal("after-chunk-ack", second.Headers["x-codex-subscribe-cursor"]);
            var replayed = new List<JsonElement>();
            var replayedPong = false;
            while (replayed.Count != outgoingCount - 1 || !replayedPong)
            {
                var reply = await second.ReadAsync(ct);
                if (reply.GetProperty("stream_id").GetString() == b.StreamId)
                {
                    Assert.Equal("pong", reply.GetProperty("type").GetString());
                    replayedPong = true;
                    await AckAsync(second, b, reply.GetProperty("seq_id").GetUInt64(), null, ct);
                    continue;
                }
                Assert.Equal(sequence, reply.GetProperty("seq_id").GetUInt64());
                Assert.True(reply.GetProperty("segment_id").GetInt32() > 0);
                replayed.Add(reply);
            }
            var reassembled = new MemoryStream();
            foreach (var chunk in new[] { firstChunk }.Concat(replayed.OrderBy(chunk => chunk.GetProperty("segment_id").GetInt32())))
                reassembled.Write(Convert.FromBase64String(chunk.GetProperty("message_chunk_base64").GetString()!));
            using (var message = JsonDocument.Parse(reassembled.ToArray()))
                Assert.Equal(text, message.RootElement.GetProperty("result").GetProperty("text").GetString());
            await AckAsync(second, a, sequence, null, ct);
            await second.SendAsync(Envelope(a, 2, new { id = 2, method = "echo" }), ct);
            await second.SendAsync(Envelope(a, 5, new { id = 5, method = "echo" }), ct);
            Assert.Equal(5, (await second.ReadAsync(ct)).GetProperty("message").GetProperty("id").GetInt32());
            Assert.Single(received, entry => entry.Peer == a
                && entry.Message.GetProperty("id").GetInt32() == 2);

            await second.SendAsync(Envelope(a, 1, new { id = 6, method = "initialize" }), ct);
            Assert.Equal(6, (await second.ReadAsync(ct)).GetProperty("message").GetProperty("id").GetInt32());
            Assert.Equal(1, Volatile.Read(ref disposed));
            var reinitialized = JsonSerializer.SerializeToUtf8Bytes(new { id = 7, method = "echo" });
            await second.SendAsync(new
            {
                type = "client_message_chunk", client_id = a.ClientId, stream_id = a.StreamId,
                seq_id = 2, segment_id = 0, segment_count = 1, message_size_bytes = reinitialized.Length,
                message_chunk_base64 = Convert.ToBase64String(reinitialized)
            }, ct);
            Assert.Equal(7, (await second.ReadAsync(ct)).GetProperty("message").GetProperty("id").GetInt32());
            await second.SendAsync(new { type = "client_closed", client_id = a.ClientId, stream_id = a.StreamId }, ct);
            await second.SendAsync(Ping(a, "closed"), ct);
            Assert.Equal("unknown", (await second.ReadAsync(ct)).GetProperty("status").GetString());
            await second.SendAsync(Ping(b, "still-open"), ct);
            Assert.Equal("active", (await second.ReadAsync(ct)).GetProperty("status").GetString());
        }
        finally
        {
            await host.DisposeAsync();
            await running;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static object Envelope(RemoteControlPeer peer, ulong sequence, object message) => new
    {
        type = "client_message", client_id = peer.ClientId, stream_id = peer.StreamId, seq_id = sequence, message
    };

    private static object Ping(RemoteControlPeer peer, string cursor) => new
    {
        type = "ping", client_id = peer.ClientId, stream_id = peer.StreamId, cursor
    };

    private static Task AckAsync(RelayFixture.Connection connection, RemoteControlPeer peer,
        ulong sequence, int? segment, CancellationToken ct) =>
        connection.SendAsync(new
        {
            type = "ack", client_id = peer.ClientId, stream_id = peer.StreamId,
            seq_id = sequence, segment_id = segment
        }, ct);
}
