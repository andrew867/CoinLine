# Firmware DLA transport adapter (`IDlXmodemTransportAdapter`)

The host registers **`IDlXmodemTransportAdapter`** with implementation **`DlXmodemTransportAdapter`**. It implements host-memory simulation and a separately enabled live sender transport.

## Simulation (default)

`SimulateTransferAsync` runs **only on the server**: it records declared artifact extent and emits checklist text for operator review. **No serial port or modem device is opened.**

## Live path (certification)

The existing sender supports configured serial, TCP or named-pipe client transport; an injected in-memory transport is available to tests. Live execution requires **`Firmware:AllowLiveFlashing`** and **`COINLINE_FIRMWARE_LIVE_DLA_ENABLED`**, along with a compatible artifact and job approval. Native terminal-initiated code-server handshake and complete upgrade acceptance have not been established by this sender. Do not mistake transport configuration for device qualification. See [firmware update](../protocols/firmware-update.md).

## Orchestration

`FirmwareJobOrchestrator.SimulateJobAsync` invokes the adapter at step **`dla_xmodem_transport`** after host checksum and compatibility steps. Job JSON lists steps and `dla_transport_simulation` safety checks.
