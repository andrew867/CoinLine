# Firmware DLA transport adapter (`IDlXmodemTransportAdapter`)

The host registers **`IDlXmodemTransportAdapter`** with default implementation **`DlXmodemSimulationTransportAdapter`**.

## Simulation (default)

`SimulateTransferAsync` runs **only on the server**: it records declared artifact extent and emits checklist text for operator review. **No serial port or modem device is opened.**

## Live path (certification)

Replacing the default implementation with UART-backed code must align with the **firmware_update** dossier, including:

- [Transport (XMODEM) and timers](https://github.com/andrew867/CoinLine/blob/main/docs/protocols/firmware_update/transport_xmodem_and_timers.md)
- [Wire protocol function codes and headers](https://github.com/andrew867/CoinLine/blob/main/docs/protocols/firmware_update/wire_protocol_function_codes_and_headers.md)
- [Orchestration tasks and gates](https://github.com/andrew867/CoinLine/blob/main/docs/protocols/firmware_update/orchestration_tasks_and_gates.md)

Live flashing remains blocked unless **`Firmware:AllowLiveFlashing`** and operational sign-off.

## Orchestration

`FirmwareJobOrchestrator.SimulateJobAsync` invokes the adapter at step **`dla_xmodem_transport`** after host checksum and compatibility steps. Job JSON lists steps and `dla_transport_simulation` safety checks.
