using System;
using Dnstap;

namespace LogForwarder
{
    /// <summary>
    /// Maps the client-facing completed DNS exchange to a DNSTap CLIENT_RESPONSE.
    /// The query and response are serialized DNS datagrams, not raw transport captures.
    /// The query timestamp and responder endpoint are unavailable from IDnsQueryLogger
    /// and are intentionally left unspecified.
    /// </summary>
    internal static class DnstapRecordConverter
    {
        private static readonly DnstapRecordFactory Factory = new();

        internal static global::Dnstap.Dnstap Convert(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var observation = entry.DnstapObservation
                ?? throw new InvalidOperationException("DNSTap capture was not enabled for this log entry.");

            return Factory.Create(new DnsLogEntry
            {
                EventType = Message.Types.Type.ClientResponse,
                ClientAddress = observation.ClientAddress,
                ClientPort = observation.ClientPort,
                Transport = MapProtocol(observation.Transport),
                ResponseTimestamp = new DateTimeOffset(entry.Timestamp, TimeSpan.Zero),
                QueryPacket = observation.QueryPacket,
                ResponsePacket = observation.ResponsePacket
            });
        }

        private static SocketProtocol? MapProtocol(TechnitiumLibrary.Net.Dns.DnsTransportProtocol protocol) =>
            protocol.ToString().ToUpperInvariant() switch
            {
                "UDP" => SocketProtocol.Udp,
                "TCP" => SocketProtocol.Tcp,
                "TLS" => SocketProtocol.Dot,
                "HTTPS" => SocketProtocol.Doh,
                "QUIC" => SocketProtocol.Doq,
                _ => null
            };
    }
}
