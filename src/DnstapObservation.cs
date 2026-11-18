using System;
using System.IO;
using System.Net;
using TechnitiumLibrary.Net.Dns;

namespace LogForwarder
{
    /// <summary>
    /// An optional snapshot of one completed, client-facing DNS exchange.
    /// Packet bytes are serialized from Technitium datagrams; they are NOT
    /// guaranteed to match the original received/transmitted wire octets.
    /// </summary>
    internal sealed class DnstapObservation
    {
        private DnstapObservation(IPAddress initiator, ushort initiatorPort,
            DnsTransportProtocol transport, byte[] query, byte[] response)
        {
            ClientAddress = initiator;
            ClientPort = initiatorPort;
            Transport = transport;
            QueryPacket = query;
            ResponsePacket = response;
        }

        internal IPAddress ClientAddress { get; }
        internal ushort ClientPort { get; }
        internal DnsTransportProtocol Transport { get; }
        internal ReadOnlyMemory<byte> QueryPacket { get; }
        internal ReadOnlyMemory<byte> ResponsePacket { get; }

        internal static DnstapObservation Capture(IPEndPoint client,
            DnsTransportProtocol transport, DnsDatagram request, DnsDatagram response)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(response);

            using var queryStream = new MemoryStream();
            request.WriteTo(queryStream);
            using var responseStream = new MemoryStream();
            response.WriteTo(responseStream);
            return new DnstapObservation(client.Address, checked((ushort)client.Port),
                transport, queryStream.ToArray(), responseStream.ToArray());
        }
    }
}
