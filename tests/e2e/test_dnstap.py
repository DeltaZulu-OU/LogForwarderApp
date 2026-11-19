"""Live DNSTap TCP receiver assertions using the independent Go decoder."""

import json
import time
from pathlib import Path

from conftest import RESULTS, probe, wait_until


CAPTURE = RESULTS / "dnstap.jsonl"


def records():
    if not CAPTURE.exists():
        return []
    decoded = []
    for line in CAPTURE.read_text().splitlines():
        if not line.strip():
            continue
        try:
            decoded.append(json.loads(line))
        except json.JSONDecodeError:
            # A concurrent collector write may still be in progress.
            continue
    return decoded


def matches_name(record, name):
    if record.get("type") != "MESSAGE":
        return False
    message = record.get("message") or {}
    return name.lower() in json.dumps({
        "query_message": message.get("query_message", ""),
        "response_message": message.get("response_message", ""),
    }).lower()


def configure_dnstap(runtime):
    config = runtime.config()
    config["sinks"]["dnstap"] = {
        "enabled": True,
        "host": "dnstap",
        "port": 6000,
        "connectionTimeoutMs": 3000,
        "operationTimeoutMs": 3000,
        "reconnectIntervalMs": 200,
        "batching": {"batchSize": 1, "batchDelayMs": 20, "retryTimeLimitMs": 0},
    }
    runtime.set_config(config)


def test_udp_client_response_reaches_independent_collector(runtime, unique_name):
    configure_dnstap(runtime)
    output = probe(unique_name)
    assert "status: NOERROR" in output
    runtime.event(unique_name)  # Independent HTTP sink remains operational.

    result = wait_until(
        lambda: next((r for r in records() if matches_name(r, unique_name)), None),
        "DNSTap UDP observation", timeout=25)
    message = result["message"]
    assert message["type"] == "CLIENT_RESPONSE"
    assert message["socket_protocol"] == "UDP"
    assert "query_message" in message
    assert "response_message" in message
    assert unique_name.lower() in str(message["query_message"]).lower()
    assert unique_name.lower() in str(message["response_message"]).lower()
    assert message.get("query_port", 0) > 0


def test_tcp_client_response_reaches_independent_collector(runtime, unique_name):
    configure_dnstap(runtime)
    output = probe(unique_name, qtype="AAAA", tcp=True)
    assert "status: NOERROR" in output
    runtime.event(unique_name, qtype="AAAA")
    result = wait_until(
        lambda: next((r for r in records() if matches_name(r, unique_name)), None),
        "DNSTap TCP observation", timeout=25)
    assert result["message"]["type"] == "CLIENT_RESPONSE"
    assert result["message"]["socket_protocol"] == "TCP"
    assert "response_message" in result["message"]


def test_disabling_dnstap_preserves_http_and_stops_capture(runtime, unique_name):
    config = runtime.config()
    config["sinks"]["dnstap"] = {"enabled": False}
    runtime.set_config(config)
    probe(unique_name)
    assert runtime.event(unique_name)["question"]["questionName"] == unique_name
    # This unique name was never captured during a DNSTap-enabled configuration.
    time.sleep(0.5)
    assert not any(matches_name(r, unique_name) for r in records())

def test_unavailable_collector_does_not_block_other_sinks(runtime, unique_name):
    config = runtime.config()
    config["sinks"]["dnstap"] = {
        "enabled": True,
        "host": "127.0.0.1",
        "port": 65534,
        "connectionTimeoutMs": 300,
        "operationTimeoutMs": 300,
        "reconnectIntervalMs": 200,
        "batching": {"batchSize": 1, "batchDelayMs": 20, "retryTimeLimitMs": 0},
    }
    runtime.set_config(config)
    assert "status: NOERROR" in probe(unique_name)
    assert runtime.event(unique_name)["question"]["questionName"] == unique_name
    # This collector endpoint is deliberately unbound in the test container.
    assert not any(matches_name(r, unique_name) for r in records())
