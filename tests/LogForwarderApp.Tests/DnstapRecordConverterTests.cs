using System.Net;
using System.Text.Json;
using Dnstap;
using LogForwarder;
using TechnitiumLibrary.Net.Dns;

namespace LogForwarderApp.Tests;

[TestClass]
public sealed class DnstapRecordConverterTests
{
    [TestMethod]
    public void CaptureIsOptInAndDoesNotChangeExistingJsonOutput()
    {
        var disabled = TestFixtures.CreateLogEntry("example.org");
        Assert.IsNull(disabled.DnstapCapture);
        Assert.ThrowsExactly<InvalidOperationException>(() => DnstapRecordConverter.Convert(disabled));

        var enabled = TestFixtures.CreateLogEntry("example.org", captureDnstap: true);
        Assert.IsNotNull(enabled.DnstapCapture);
        using var json = JsonDocument.Parse(enabled.ToString());
        Assert.IsFalse(json.RootElement.TryGetProperty("dnstapCapture", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("dnstapCaptureError", out _));
    }

    [TestMethod]
    public void ConvertsCompletedClientFacingExchangeWithoutInventingMetadata()
    {
        var (query, response) = TestFixtures.CreateDnsExchange("example.org");
        var timestamp = new DateTime(2026, 11, 18, 12, 30, 0, DateTimeKind.Utc);
        var entry = new LogEntry(timestamp, new IPEndPoint(IPAddress.Parse("192.0.2.10"), 53000),
            DnsTransportProtocol.Udp, query, response, captureDnstap: true);

        var record = DnstapRecordConverter.Convert(entry);
        Assert.AreEqual(global::Dnstap.Dnstap.Types.Type.Message, record.Type);
        Assert.AreEqual(Message.Types.Type.ClientResponse, record.Message.Type);
        Assert.AreEqual(SocketFamily.Inet, record.Message.SocketFamily);
        Assert.AreEqual(SocketProtocol.Udp, record.Message.SocketProtocol);
        Assert.AreSequenceEqual(IPAddress.Parse("192.0.2.10").GetAddressBytes(),
            record.Message.QueryAddress.ToByteArray());
        Assert.AreEqual(53000u, record.Message.QueryPort);
        Assert.IsFalse(record.Message.HasResponsePort);
        Assert.IsTrue(record.Message.ResponseAddress.IsEmpty);
        Assert.IsFalse(record.Message.HasQueryTimeSec);
        Assert.AreEqual((ulong)new DateTimeOffset(timestamp).ToUnixTimeSeconds(),
            record.Message.ResponseTimeSec);

        // Technitium can serialize DNS datagrams but cannot guarantee the original bytes.
        using var input = new MemoryStream(record.Message.QueryMessage.ToByteArray());
        using var output = new MemoryStream(record.Message.ResponseMessage.ToByteArray());
        var decodedQuery = DnsDatagram.ReadFrom(input);
        var decodedResponse = DnsDatagram.ReadFrom(output);
        Assert.AreEqual(query.Identifier, decodedQuery.Identifier);
        Assert.AreEqual(response.Identifier, decodedResponse.Identifier);
    }

    [TestMethod]
    public void SnapshotIsIndependentOfSourceDnsDatagrams()
    {
        var entry = TestFixtures.CreateLogEntry("independent.example", captureDnstap: true);
        var before = DnstapRecordConverter.Convert(entry);
        var after = DnstapRecordConverter.Convert(entry);
        Assert.AreSequenceEqual(before.Message.QueryMessage.ToByteArray(),
            after.Message.QueryMessage.ToByteArray());
        Assert.AreSequenceEqual(before.Message.ResponseMessage.ToByteArray(),
            after.Message.ResponseMessage.ToByteArray());
    }

    [TestMethod]
    public void Ipv6ClientIsEncodedAsInet6()
    {
        var (query, response) = TestFixtures.CreateDnsExchange("example.org");
        var entry = new LogEntry(DateTime.UtcNow, new IPEndPoint(IPAddress.IPv6Loopback, 53333),
            DnsTransportProtocol.Tcp, query, response, captureDnstap: true);
        var record = DnstapRecordConverter.Convert(entry);
        Assert.AreEqual(SocketFamily.Inet6, record.Message.SocketFamily);
        Assert.AreEqual(SocketProtocol.Tcp, record.Message.SocketProtocol);
        Assert.AreSequenceEqual(IPAddress.IPv6Loopback.GetAddressBytes(),
            record.Message.QueryAddress.ToByteArray());
        Assert.AreEqual(53333u, record.Message.QueryPort);
    }
}
