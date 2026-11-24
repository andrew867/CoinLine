# Emulator acceptance

1. Start the [Compose lab](../../docker/README.md) and verify API and console readiness. Use the separately supplied emulator with the NCC endpoint set to TCP port `4000` on the host.
2. Configure a terminal with the same five-byte lab address the firmware sends. Assign a published set of complete, compatible table records. Make a cold INSTALL or warm force-download call.
3. In NCC Sessions, confirm that the call matches the terminal. In Downloads, verify that every item received a type 14 acknowledgement and that the batch ended as `Completed`. The batch details record a terminal rejection or missing acknowledgement when a transfer fails.
4. After the terminal enters service, make a local rated test call. Verify that the type 63 request and type 64 response are stored on the associated call record with an audit event. Compare the initial charge shown by the terminal with the stored response. A separate local test for an unmatched rate should return status 3 and display “Can't Rate Call”.
5. To check local toll-free screening, dial the lab route through the line simulator. Confirm the line simulator answers and NCC Sessions records no type 63 request for that call.

Record the install and rating sessions against the same lab terminal. The firmware can close the install session before opening a separate session for rating.
