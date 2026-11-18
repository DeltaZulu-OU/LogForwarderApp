using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace LogForwarder.Sinks
{
    /// <summary>
    /// Sends client-facing DNS responses through the DNSTap batch API.
    /// SinkDispatcher owns the only queue; no Serilog logger or second batcher is created.
    /// </summary>
    public sealed class DnstapOutputSink : IOutputSink
    {
        private static readonly MessageTemplate Template =
            new MessageTemplateParser().Parse("DNS response");
        private readonly IBatchedLogEventSink _sink;
        private readonly bool _ownsSink;
        private readonly Action<Exception>? _onCaptureError;
        private bool _disposed;

        public DnstapOutputSink(string host, int port,
            TimeSpan? connectionTimeout = null, TimeSpan? operationTimeout = null,
            TimeSpan? reconnectInterval = null, Action<Exception>? onCaptureError = null)
            : this(Serilog.Sinks.Dnstap.DnstapSink.CreateTcp(host, port,
                connectionTimeout, operationTimeout, reconnectInterval),
                ownsSink: true, onCaptureError)
        {
        }

        internal DnstapOutputSink(IBatchedLogEventSink sink, bool ownsSink = false,
            Action<Exception>? onCaptureError = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _ownsSink = ownsSink;
            _onCaptureError = onCaptureError;
        }

        public async Task ForwardAsync(IReadOnlyList<LogEntry> logs, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(logs);
            token.ThrowIfCancellationRequested();
            if (logs.Count == 0)
                return;

            var events = new List<LogEvent>(logs.Count);
            foreach (var entry in logs)
            {
                ArgumentNullException.ThrowIfNull(entry);
                if (entry.DnstapCaptureError is { } captureError)
                {
                    _onCaptureError?.Invoke(captureError);
                    continue;
                }

                if (entry.DnstapCapture is null)
                    continue;

                try
                {
                    var record = DnstapRecordConverter.Convert(entry);
                    events.Add(new LogEvent(new DateTimeOffset(entry.Timestamp),
                        LogEventLevel.Information, null, Template,
                        [new LogEventProperty(Serilog.Sinks.Dnstap.DnstapEventProperty.Name,
                            new ScalarValue(record))]));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    _onCaptureError?.Invoke(ex);
                }
            }

            // Upstream batch writes can partially succeed; propagating failures permits
            // dispatcher diagnostics while deliberately preventing whole-batch replay.
            if (events.Count != 0)
                await _sink.EmitBatchAsync(events).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ownsSink && _sink is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
