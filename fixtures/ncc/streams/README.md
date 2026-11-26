# NCC stream fixtures

`NccIncrementalStreamDecoderTests` exercises arbitrary chunk boundaries with generated frames. The valid CLR samples in `fixtures/ncc/valid/` are additional framing fixtures. Neither establishes complete captured-session parity.

## Shared native-session replay

The backend and emulator use the same two filenames in their respective `fixtures/ncc/streams/` directories:

* `native-session.tsv`: ASCII, LF line endings, final LF. The header is `direction<TAB>hex`; each following row contains one complete observed wire frame in uppercase hexadecimal, with `T` for terminal-to-host or `H` for host-to-terminal. Preserve observed frame completion order, exact bytes (including CRC), and directions. Do not capture TCP segmentation as a frame boundary.
* `native-session.tsv.sha256`: the 64 lowercase hexadecimal SHA-256 of the exact TSV file, followed by two spaces, `native-session.tsv`, and LF (the `sha256sum` format). Both repositories must have identical file hashes.

Only publish a complete, independently reviewed isolated test call using reserved synthetic identities. The current gate supports short routine, clock and alarm-only dialogues, including the terminal's final clear. Do not include maintenance identifiers or PINs, payment/card records, CDRs, collection records, private identities or unreviewed payloads. An excluded or truncated session is not a candidate for reconstruction or relabelling as a capture. Review the full file before any public commit.

The protocol test validates the paired files, strict framing and CRC, re-encoding byte for byte, complete dialogue order and independent per-direction incremental stream replay with single-byte and irregular chunks. The actual capture test is explicitly skipped only while both files are absent; one missing file, incorrect hash, malformed frame, unsupported dialogue, or incomplete clear fails the gate. Generated self-tests verify the gate without masquerading as native captures.

**Capture status: pending.** Isolated test contacts were attempted, but no complete credential-free dialogue met the capture allowance. No actual capture files are included. Once a qualifying contact is available, add the exact pair to both repositories, record its hash and validate both suites before claiming shared capture parity. This is emulated terminal evidence, not physical hardware qualification.
