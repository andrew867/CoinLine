# Lab setup

For a hardware lab, use a stable supply, grounded serial adapters, documented baud rates, and isolated line tracing.

For an emulator lab, start the [Docker stack](../getting-started/quickstart-docker.md) and connect the emulator separately to the host's TCP port `4000`.

1. Configure CoinLine with the five-byte lab address sent by the firmware and assign a published set of complete records for that image.
2. Run INSTALL or a warm force download. Check the matched NCC session and each type 14 table acknowledgement in the console. Verify that the batch finished as `Completed`.
3. Once the firmware enters service, make a local rated call and compare the displayed charge with the stored type 64 response. Verify the terminal link and audit record. The firmware can open a separate NCC session for rating.
4. Make a local toll-free call through the line simulator. Verify answer supervision and check whether the terminal handled screening locally without a type 63 request.

Record the results by lab terminal and use sanitized examples when sharing them. See [Table download](table-download.md) and [Rated call](rated-call.md) for hardware lab checks.
