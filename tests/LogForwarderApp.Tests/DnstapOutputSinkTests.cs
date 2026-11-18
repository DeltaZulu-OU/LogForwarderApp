using LogForwarder;
using LogForwarder.Sinks;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Dnstap;

namespace LogForwarderApp.Tests;

[TestClass]
public sealed class DnstapOutputSinkTests
{
    private sealed class RecordingBatchSink : IBatchedLogEventSink, IDisposable
    {
        public IReadOnlyCollection<LogEvent>? Batch { get; private set; }
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        public Exception? Failure { get; set; }

        public Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            Calls++;
            Batch = batch;
            if (Failure is not null)
                throw Failure;
            return Task.CompletedTask;
        }

        public Task OnEmptyBatchAsync() => Task.CompletedTask;
        public void Dispose() => Disposals++;
    }

    [TestMethod]
    public async Task ConvertsCapturedRecordsIntoOneTypedBatch()
    {
        var recorder = new RecordingBatchSink();
        using var sink = new DnstapOutputSink(recorder);
        await sink.ForwardAsync([
            TestFixtures.CreateLogEntry("first.example", captureDnstap: true),
            TestFixtures.CreateLogEntry("second.example", captureDnstap: true)
        ], CancellationToken.None);

        Assert.AreEqual(1, recorder.Calls);
        Assert.IsNotNull(recorder.Batch);
        Assert.HasCount(2, recorder.Batch);
        foreach (var evt in recorder.Batch)
        {
            Assert.IsTrue(DnstapEventProperty.TryGetRecord(evt, out var record));
            Assert.AreEqual(global::Dnstap.Message.Types.Type.ClientResponse, record!.Message.Type);
            Assert.IsTrue(record.Message.QueryMessage.Length > 0);
            Assert.IsTrue(record.Message.ResponseMessage.Length > 0);
        }
    }

    [TestMethod]
    public async Task SkipsUncapturedEntriesWithoutStartingTransport()
    {
        var recorder = new RecordingBatchSink();
        using var sink = new DnstapOutputSink(recorder);
        await sink.ForwardAsync([
            TestFixtures.CreateLogEntry("no-capture.example")
        ], CancellationToken.None);
        Assert.AreEqual(0, recorder.Calls);
    }

    [TestMethod]
    public async Task PropagatesTransportFailureWithoutRetryingBatch()
    {
        var recorder = new RecordingBatchSink { Failure = new IOException("transport failed") };
        using var sink = new DnstapOutputSink(recorder);
        await Assert.ThrowsExactlyAsync<IOException>(() => sink.ForwardAsync([
            TestFixtures.CreateLogEntry("failed.example", captureDnstap: true)
        ], CancellationToken.None));
        Assert.AreEqual(1, recorder.Calls);
    }

    [TestMethod]
    public async Task DoesNotForwardCanceledOrEmptyBatches()
    {
        var recorder = new RecordingBatchSink();
        using var sink = new DnstapOutputSink(recorder);
        await sink.ForwardAsync([], CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            sink.ForwardAsync([TestFixtures.CreateLogEntry("cancel.example", captureDnstap: true)], cts.Token));
        Assert.AreEqual(0, recorder.Calls);
    }

    [TestMethod]
    public void DisposesOnlyOwnedSink()
    {
        var injected = new RecordingBatchSink();
        var sink = new DnstapOutputSink(injected);
        sink.Dispose();
        sink.Dispose();
        Assert.AreEqual(0, injected.Disposals);

        var owned = new RecordingBatchSink();
        var ownedSink = new DnstapOutputSink(owned, ownsSink: true);
        ownedSink.Dispose();
        ownedSink.Dispose();
        Assert.AreEqual(1, owned.Disposals);
    }

    [TestMethod]
    public void ConfigurationValidatesOnlyEnabledDnstap()
    {
        var disabled = AppConfig.Deserialize(
            """{"sinks":{"dnstap":{"enabled":false}},"pipeline":{}}""");
        Assert.IsFalse(disabled.Sinks.DnstapSinkConfig!.Enabled);

        var enabled = AppConfig.Deserialize(
            """{"sinks":{"dnstap":{"host":"127.0.0.1","port":6000,"batching":{"batchSize":100,"retryTimeLimitMs":0}}},"pipeline":{}}""");
        Assert.AreEqual("127.0.0.1", enabled.Sinks.DnstapSinkConfig!.Host);
        Assert.AreEqual(100, enabled.Sinks.DnstapSinkConfig.Batching!.BatchSize);

        foreach (var invalid in new[]
        {
            """{"sinks":{"dnstap":{"enabled":true}},"pipeline":{}}""",
            """{"sinks":{"dnstap":{"host":"localhost","port":0}},"pipeline":{}}""",
            """{"sinks":{"dnstap":{"host":"localhost","operationTimeoutMs":0}},"pipeline":{}}""",
            """{"sinks":{"dnstap":{"host":"localhost","allowBatchReplay":true}},"pipeline":{}}""",
            """{"sinks":{"dnstap":{"host":"localhost","batching":{"batchSize":0}}},"pipeline":{}}"""
        })
        {
            Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
                () => AppConfig.Deserialize(invalid));
        }
    }
}
