# Audio-Gram and Asterisk

Audio-Gram is an optional telephone messaging feature. A compatible payphone can offer a voice messaging service when an eligible outgoing call encounters busy or unanswered ringing. If the caller accepts, the phone contacts a separate service through its telephone line. The CoinLine NCC host provisions the terminal and collects its reports. The separate Asterisk service handles audio recording and delivery.

## Architecture

```text
CoinLine NCC
    | MT68 provisioning / MT69 reporting
Feature-enabled payphone
    | Analog line: call progress, DTMF, voice
Analog line gateway
    | SIP and RTP
Asterisk
    +-- Lab extensions: busy or unanswered ringing
    +-- Service access: compatible Audio-Gram adapter
```

An existing compatible service can replace the adapter in this diagram. It must support the phone's Audio-Gram protocol, authorization, payment decisions, voice messaging and delivery. Asterisk can provide call routing, media and call progress. The included local AGI implements recording and delivery after a trusted handoff; it is not an implementation of the terminal-specific authorization conversation.

### Compatibility and evidence

| Component | Current evidence |
| --- | --- |
| MT68 editor and encoding | The 72-byte record, including its message type, has a typed editor. Fixtures verify unchanged saves, localized edits and preservation of unrelated fields. |
| MT68 transport | An isolated NCC fixture received the exact 72 bytes and returned MT14 to the original selected download batch. |
| MT69 reporting | A separate fixture uploaded the 213-byte statistics record and CoinLine decoded it. |
| Terminal support | Audio-Gram is conditional in the older firmware profile. Inspected older builds disable it; the later documented Multi-pay profile does not include MT68. A matching size is not proof that a build enables the feature. |
| Asterisk and messaging service | A local AGI recording and delivery core is included, with a persistent message spool, atomic Asterisk call files and distinct playback outcomes. Its media and spool tests use synthetic files and AGI replies. Live Asterisk, the terminal-specific authorization conversation and a feature-enabled terminal remain separate acceptance gates. |

Check the installed firmware and terminal requirements before creating an assignment. Do not add MT68 to an incompatible production set just because another terminal recognizes the record.

## Configure a terminal in CoinLine

1. Register the installed firmware and open **Table sets**. Copy an appropriate published set to a draft, keeping the phone's other valid configuration records.
2. Open **Terminal configuration**, then **Terminal features and call setup**, then **Audio-Gram messaging**. Start with the inactive MT68 template or import a known-good record for the same build.
3. Enter the access number and protocol settings agreed with the messaging service. Configure call and payment eligibility, surcharges, call-progress thresholds and timing. Keep the service disabled while preparing the switch.
4. Save the record and review the **Audio-Gram configuration preview** and firmware findings. A blank key edit preserves an existing stored key; a newly enabled record requires a valid key. Do not include keys in notes, exported captures or screenshots.
5. Once a compatible service is available, enable the record on the draft. Publish the set and use **Downloads** to prepare a selected-record batch containing MT68 for the isolated test phone. Confirm which other records, if any, the set requires.
6. On the terminal's eligible call-in, inspect the same batch for MT68's application acknowledgement. An acknowledgement proves reception, not the subsequent messaging behavior.

See [Table management](table-management.md) for publishing, assignments and selected downloads.

### Settings reference

| Settings | Purpose and recommendations |
| --- | --- |
| Enable, access number and protocol version | The access number must be reachable from the terminal's telephone line. The protocol version is one decimal digit in the identified firmware. Use the version agreed with the service. |
| Service key | Eight decimal digits supplied by the service operator; the first group of four cannot be all zeros. The editor accepts a replacement without displaying the stored value. |
| Call types | Local, intra-area, inter-area and international. Begin with just the type used by the isolated test route. |
| Payment types | Coins, stored-value cards, credit cards and calling cards. Enable only payment methods supported by both the installed terminal and the service. |
| Surcharges | Independent amounts for the four call zones. Agree on lab prices and charging rules before enabling the test. |
| Return funds | Select the applicable return-escrow policy in agreement with the service operator. Verify actual coin return and collection separately. |
| Unanswered ringing and busy cycles | Offer-detection thresholds based on call progress. Use at least two busy cycles and a greater unanswered-ringing count; otherwise the identified implementation uses internal thresholds. Actual audible cadence and the offer point must be checked at the phone. |
| Sync, go, no-go and paid tones | DTMF signaling values supplied by the compatible service. Go and no-go must be distinguishable. Do not invent these settings or use a dialplan that grants authorization unconditionally. |
| Statistics and CDRs | Independent settings. Statistics requests MT69 service counters; the CDR option requests service call records under the applicable terminal reporting path. |
| Reserved fields | Keep the imported or template values unless their meanings are established for the particular firmware. |

The following timing words are measured in **10 ms ticks**, not seconds. For example, 100 ticks represent 1 second. Use times measured on your gateway and compatible service; no universal default is established by the CoinLine fixture.

| Timer | Meaning |
| --- | --- |
| Sync timeout | Wait for the service's synchronization response. |
| Digit pause | DTMF digit duration and interdigit spacing in the identified implementation. |
| Go/no-go timeout | Wait for the service's decision. |
| Answer timeout | Wait for the service access call to be answered. |
| Identity delay | Delay before sending initial identity signaling after answer. |
| Session timeout | Service transaction time limit; the identified implementation starts it after an accepted go response. |
| Line release delay | Time allowed for the previous call to release before reconnecting. The legacy interface calls this seventh timing word Voice Prompt Play Delay. |

### Separate service call from NCC

At a high level, a feature-enabled terminal can detect an eligible unsuccessful call, offer messaging after its configured call-progress threshold, and, if selected, release that call and dial the service access number. The service conversation has terminal identity, synchronization, authorization, payment and voice handling phases. The exact dialogue and timing depend on the terminal and service implementation. Use the compatible service's requirements rather than treating the access number as an ordinary answered voice call.

The service call is made on the voice line. It does not require a new CoinLine NCC listener. The terminal's routine NCC interactions can deliver MT68 and upload MT69 or eligible CDRs; a standalone rating or card transaction is not a reason to send a table set.

## Recommended isolated Asterisk lab

Use a dedicated Asterisk 22-compatible PJSIP configuration on a private lab network, with no public telephone routes accessible from the lab endpoint. These examples use local extensions instead of real destinations:

| Extension | Switch behavior |
| --- | --- |
| `7001` | Unanswered ringing. No answer is sent; the call ends after 45 seconds unless the terminal hangs up first. |
| `7002` | Busy indication, with a 15-second timeout. |
| `7003` | Audio-Gram access number. Rejects the call in the supplied configuration. An optional route to a compatible adapter is provided in the sample. |

Choose lab screening and rating entries that reach these extensions and are eligible for the selected MT68 call type. If the phone expects a longer dial string, use corresponding exact extensions in all three configurations: terminal routing, the gateway and Asterisk. Start with the MT68 service disabled, statistics and CDR collection enabled for observation, local calls only, and only the agreed lab payment method. Ringing cycles of 5 and busy cycles of 2 are an illustrative starting point, not a verified physical terminal setting. Supply the access number, key, protocol version, tones, surcharges and timings agreed with the adapter before enabling service.

### Switch configuration

Download the two sample fragments:

- [extensions-audiogram.conf](../examples/asterisk/extensions-audiogram.conf), the isolated dialplan with explicit progress and disabled service route.
- [pjsip-audiogram.conf](../examples/asterisk/pjsip-audiogram.conf), the lab gateway endpoint and static service adapter endpoint.

Review both fragments and include them from your existing `extensions.conf` and `pjsip.conf` using `#include`, after checking for conflicting names and extensions. The PJSIP sample includes its own UDP transport for a standalone lab; use an existing suitable transport instead if the same address and port are already bound. Replace the documentation-only IP addresses and gateway authentication placeholder with actual private lab values. The example expects a static gateway that sends authenticated SIP calls to Asterisk, not a registering endpoint. Keep SIP and RTP accessible only to the intended lab peers; do not expose the sample to the internet. Configure outbound adapter authentication as required by the adapter.

The active dialplan is intentionally small:

```ini
[coinline-audiogram-lab]
exten => 7001,1,Ringing()
 same => n,Wait(45)
 same => n,Hangup(19)
exten => 7002,1,Busy(15)
 same => n,Hangup(17)
exten => 7003,1,Hangup(21)
```

When a compatible service is installed, replace the `7003` rejection with the optional `Dial(PJSIP/audiogram-adapter,30)` and final hangup from the downloadable fragment. Asterisk then dials the adapter's configured contact; the 30-second limit applies to establishing that call, not to the Audio-Gram transaction after answer. The adapter remains responsible for the conversation. Do not add `Answer()` before the busy or ringing routes, and do not send artificial service authorization tones.

The sample follows the official Asterisk [PJSIP configuration examples](https://docs.asterisk.org/Configuration/Channel-Drivers/SIP/Configuring-res_pjsip/res_pjsip-Configuration-Examples/), [PJSIP dialing guide](https://docs.asterisk.org/Configuration/Channel-Drivers/SIP/Configuring-res_pjsip/Dialing-PJSIP-Channels/), [Dial](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/Dialplan_Applications/Dial/), [Ringing](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/Dialplan_Applications/Ringing/) and [Busy](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/Dialplan_Applications/Busy/) application documentation.

### Local AGI recording and delivery

The repository's `services/audiogram/coinline_audiogram_agi.py` is a Python 3.10+ AGI script with no third-party Python dependencies. It implements the voice-message portion of a service: record ordinary mono WAV audio, validate its duration, store a persistent SQLite message entry, submit an Asterisk call file, and play the message after the intended local recipient answers. An incomplete playback is not counted as delivery. Asterisk handles the configured no-answer call attempts and retry interval.

The service is deliberately independent of the terminal's protected initiation and authorization conversation. It does not interpret card data, service keys, payment requests or arbitrary caller-provided destination digits. The only destinations accepted are preconfigured local Asterisk extensions. Do not route extension `7003` to this script or grant a terminal authorization just to reach the recording service. A compatible, authorized protocol component must make that handoff, with the final recipient on an approved local route. That component remains to be validated against an enabled firmware build.

To evaluate the included recording and delivery core on an isolated Asterisk host:

1. Install `services/audiogram/coinline_audiogram_agi.py` in Asterisk's AGI script directory with execute permission. Copy `services/audiogram/audiogram.example.json` to `/etc/coinline/audiogram.json` and keep the configuration private. The sample is disabled until `enabled` is explicitly set to `true`.
2. Create the configured private data and staging directories and Asterisk's archive directory if absent. The Asterisk service account must be able to read and write them; the recording data directory should not be web-accessible. The staging and outgoing call-file directories must be on the same filesystem. Do not replace Asterisk's existing outgoing directory or its permissions.
3. Include the sample dialplan. Configure the optional `audiogram-lab-recipient` endpoint on the private lab network and ensure extension `7005` routes only to it, with no PSTN fallback. Give a separate trusted lab originating endpoint access to `coinline-audiogram-record-test`. That context is not included from the gateway or public context.
4. Dial `7004` from the trusted test endpoint. Asterisk answers and plays a recording beep. Speak for at least the configured minimum duration, then press `#` or hang up. The service validates the saved WAV and queues a local delivery. Answer the recipient endpoint and listen to the entire playback.
5. Inspect the script's status output. It distinguishes recording, queued, playing, delivered and incomplete states from the Asterisk call-file archive outcome. Asterisk reporting a completed call does not by itself prove that the message played completely.

```sh
python3 /var/lib/asterisk/agi-bin/coinline_audiogram_agi.py /etc/coinline/audiogram.json status
```

If a recording persists but call-file placement failed, run the same command with `enqueue MESSAGE_ID` after correcting the directory or permissions. A stable call-file name prevents requeueing a message already present in outgoing or the archive. Do not automatically resubmit an incomplete or expired delivery; review its message and Asterisk status first. Recording files remain private for the operator's retention policy. No external delivery, real payment, or production legacy authorization is included in this lab core.

The implementation follows the official Asterisk [AGI](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/Dialplan_Applications/AGI/), [RECORD FILE](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/AGI_Commands/record_file/), [STREAM FILE](https://docs.asterisk.org/Asterisk_22_Documentation/API_Documentation/AGI_Commands/stream_file/) and [call-file spool](https://docs.asterisk.org/Configuration/Interfaces/Asterisk-Call-Files/) interfaces. The repository tests run with fake media and temporary local directories; no Asterisk instance or external telephone call is needed in CI.

### Analog supervision and media

The gateway must present the line behavior that the terminal expects:

- Unanswered ringing and busy cadence must actually reach the analog line, without an answer indication. SIP 180 or 486 on its own does not establish what the phone hears.
- A new seizure after line release must be possible. Verify disconnect, dial tone, dialing and the configured physical answer-supervision method on the line.
- Prefer one common G.711 codec on both legs, typically PCMU (`ulaw`) in North American labs or PCMA (`alaw`) if required. Keep Asterisk in the media path (`direct_media=no`) during the lab.
- Agree on DTMF transport between the gateway, Asterisk and adapter. The sample starts with `rfc4733`; the gateway must correctly detect and regenerate analog tones. Use G.711 in-band DTMF if the compatible adapter specifically requires it and test both directions. Verify duration, spacing and the full required symbol range before enabling messaging.

Inspect the loaded configuration on the Asterisk host:

```sh
asterisk -rx "dialplan show coinline-audiogram-lab"
asterisk -rx "pjsip show endpoint coinline-lab-gateway"
asterisk -rx "pjsip show endpoint audiogram-adapter"
```

These commands inspect the configuration. Apply or reload it using your lab's normal Asterisk maintenance process. Test the three local extensions from the intended gateway before changing MT68.

## Acceptance checklist

Record the firmware version, terminal type, MT68 revision and selected download batch. Use synthetic identities and lab calls.

| Step | Expected observation |
| --- | --- |
| 1. Establish line behavior | `7001` produces ringing without answering; `7002` produces busy. `7003` is rejected until the compatible service is installed. No external call is placed. |
| 2. Deliver provisioning | The exact MT68 record is delivered to the intended terminal and MT14 is recorded on the original batch. |
| 3. Trigger offer | On the feature-enabled terminal, test unanswered and busy separately. Check the prompt appears only where the selected call type and payment permit it. |
| 4. Test service unavailable | With an authorized lab config and unavailable service, verify the terminal handles failed access or timeout and does not report a successful message. |
| 5. Test actual service | With the compatible adapter, verify the second dial, answer supervision, synchronization, authorization, agreed charging, voice recording and delivery. Test a declined transaction and ensure no successful delivery or incorrect collection is reported. |
| 6. Check reporting | On the next appropriate NCC report, inspect **Uploads** for decoded MT69 service counters and **Calls** for any requested service CDR. Compare counters and collection with the observed calls and actual payment, not just the quoted surcharge. |
| 7. Restore lab state | Deliver an MT68 with the enable flag cleared if removing the feature. Confirm terminal acknowledgement and behavior before retiring the service route. |

Keep editor/encoding tests, application acknowledgement and observed service behavior as separate acceptance results. The CoinLine fixtures currently establish the first two and decoded report handling; steps involving the physical phone and Asterisk service remain pending.
