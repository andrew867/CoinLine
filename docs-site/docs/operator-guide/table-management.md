# Table management

Register installed builds under **Firmware versions**, then select the installed build on each phone. Select the same **Target firmware** on a table-set draft if it is intended for that build. A set with a target can be assigned only to phones registered with that version. The draft's firmware review reports record lengths that conflict with a verified layout.

1. Open **Table sets** and select the set for the target firmware. Make a named draft copy of a published set to retain its existing records.
2. Open **Terminal configuration**. Under **Text and display**, choose **Text advertising** to edit the 20 message slots. **Edit as text block** changes the text while keeping each slot's duration and display mode. Mark the first unused slot in a rotation **End sequence**. For a timed blank, leave the text empty, set a duration, and clear **End sequence**. The editor saves a 481-byte MT29 record.
3. Select another record to open its native fields. **Terminal features and call setup** includes Multi-pay, Card only and Coin only. **Cards, coin and carriers** includes coin slots, magnetic-card ranges and carrier acceptance. Call screening and tariff records have their own editors. Use the entry selector and search on lists with many records.
4. Start optional quick-access, service restriction, promotion or discount records from an inactive template on a draft. Import a complete record built for the target firmware when a template is unavailable. For records of the same size, the editor uses a verified target layout when one is registered. Otherwise, select the matching layout before editing. Published records are read-only.
5. Publish the draft. In **Assign this set**, choose a phone, group, location or customer and preview which phones will be updated. Group, location and customer updates apply to their current eligible phones. A set with a terminal access record is eligible for phones with that wire ID.
6. Check **Downloads** after the terminal requests a transfer. The batch shows each table's acknowledgment and completion status.

In **Terminal display preview**, inspect a single message or play a complete sequence. MT26 controls which contexts are available. When quick-access or call-established advertising is enabled, slots 1 through 10 run on hook and slots 11 through 20 run in the enabled off-hook contexts. The preview supports static, flash, alternating and scrolling modes. Select **MTR 1.9 terminal timing** to use the set's MT30 scroll interval when present, or **Maximizer preview timing** to adjust playback intervals in the browser.

To queue the set for a routine call-in, choose **Prepare now for the next call-in** or **Prepare at a specific time** after previewing the target phones. The date picker uses your browser's local time. The terminal can pick up an available set on its next call-in or explicit table request. Use **Scheduled downloads** on the set or Downloads page to check availability, see the latest attempt, or cancel an open schedule between attempts.

For an existing bitmap screen in a supported graphical record, open **Text and display** and select the screen. A draft accepts 112 by 72 pixel PNG, BMP and JPEG artwork and previews the resulting monochrome image. Save the record to apply the artwork. The editor preserves the remaining screen data.

Under **Terminal features and call setup**, select **Audio-Gram messaging** to edit MT68 for a compatible switch-provided messaging service. Start from an inactive record or import an existing one. Set the access number and protocol version, then review the call and payment permissions, surcharges, signaling, and timers supplied by the service operator. Enter the service key to configure a new record or replace its stored key. Leave the key field blank to keep the stored value.

The set summary shows the operating type, enabled coin slots and configured magnetic-card ranges for the recognized profiles. Review any provisioning findings before assigning the set.

In **Dialing, screening and service**, the numbering plan editor shows the existing matching rules for a recognized dial plan. It can change the digit classes and selected call options while preserving the links and number-formatting data.

In **Phones**, open a phone and expand **Phone groups** to choose its memberships. A phone can belong to up to 40 groups. Its primary group appears in the phone list. Every saved membership is included in that group's assignment preview.

The console also exposes **Terminal status**, where decoded alarms, maintenance reports and status flags can be filtered by phone or report type.
