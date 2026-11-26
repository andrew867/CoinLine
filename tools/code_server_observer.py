#!/usr/bin/env python3
"""Receive-only loopback probe for an independent terminal code-server call."""

import argparse
import json
import socket
import time
from datetime import datetime, timezone


CONTROL_BYTES = {
    0x01: "SOH", 0x02: "STX", 0x04: "EOT", 0x06: "ACK",
    0x15: "NAK", 0x18: "CAN", 0x43: "possible_crc_request",
}


def observe(port, duration, session_duration, idle, byte_limit, emit):
    """Observe one connection at a time. Never send bytes or retain payloads."""
    def event(kind, **fields):
        emit({"event": kind, "atUtc": datetime.now(timezone.utc).isoformat(), **fields})

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", port))
        listener.listen(1)
        expires = time.monotonic() + duration
        event("listening", address="127.0.0.1", port=listener.getsockname()[1],
              durationSeconds=duration, sessionSeconds=session_duration,
              idleSeconds=idle, maximumBytesPerSession=byte_limit)
        session = 0
        while time.monotonic() < expires:
            listener.settimeout(max(0.001, min(1, expires - time.monotonic())))
            try:
                client, _ = listener.accept()
            except socket.timeout:
                continue
            session += 1
            with client:
                event("connected", session=session)
                total, receives = 0, 0
                deadline = min(expires, time.monotonic() + session_duration)
                reason = "session_deadline"
                while total < byte_limit and time.monotonic() < deadline:
                    client.settimeout(max(0.001, min(idle, deadline - time.monotonic())))
                    try:
                        data = client.recv(min(1024, byte_limit - total))
                    except socket.timeout:
                        reason = "session_deadline" if time.monotonic() >= deadline else "idle_timeout"
                        break
                    except OSError:
                        reason = "read_error"
                        break
                    if not data:
                        reason = "peer_closed"
                        break
                    total += len(data)
                    receives += 1
                    # A TCP receive is not a protocol frame. Classify only a
                    # standalone possible control octet, never arbitrary data.
                    shape = CONTROL_BYTES.get(data[0], "redacted") if len(data) == 1 else "redacted"
                    event("received", session=session, receive=receives,
                          bytes=len(data), possibleControl=shape)
                if total >= byte_limit:
                    reason = "byte_limit"
                event("closed", session=session, reason=reason, receivedBytes=total,
                      receiveCount=receives, sentBytes=0)
        event("listener_closed", sessions=session)


def bounded_int(low, high):
    def parse(value):
        number = int(value)
        if not low <= number <= high:
            raise argparse.ArgumentTypeError(f"must be within {low}..{high}")
        return number
    return parse


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=bounded_int(1024, 65535), required=True)
    parser.add_argument("--duration-seconds", type=bounded_int(1, 3600), default=900)
    parser.add_argument("--session-seconds", type=bounded_int(1, 300), default=60)
    parser.add_argument("--idle-seconds", type=bounded_int(1, 120), default=15)
    parser.add_argument("--maximum-bytes", type=bounded_int(1, 65536), default=4096)
    parser.add_argument("--output", required=True, help="New metadata-only JSONL report")
    args = parser.parse_args()
    with open(args.output, "x", encoding="utf-8") as report:
        def emit(event):
            report.write(json.dumps(event) + "\n")
            report.flush()
        observe(args.port, args.duration_seconds, args.session_seconds,
                args.idle_seconds, args.maximum_bytes, emit)


if __name__ == "__main__":
    main()
