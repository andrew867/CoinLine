# Firmware lab results

## NCC install and maintenance

The firmware used its configured line identifier as the NCC address. Once the terminal identity matched the host, the firmware requested clock sync and tables. It acknowledged all ten installed records during an OEM warm refresh and a demo cold install. Both sessions cleared normally.

The host now waits 350 ms before link acknowledgement in matched firmware dialogues so the device can finish transmitting and arm its acknowledgement timer. The clock response and its end marker remain pending until the terminal acknowledges each frame. A matching NACK causes retransmission with the original packet ID and retry bit. Table fragments are paced by link acknowledgements, and the host waits for a type 14 acknowledgement after each complete record.

The firmware maintenance test acknowledged the host's type 15 reply and cleared the connection. The host creates no install batch for a maintenance upload or rating request.

## Rating and local calling

The demo received type 64 status 3 for an unavailable rate, acknowledged the response, and displayed “Can't Rate Call”. The OEM received a type 64 status 6 quote of 3 cents for the first 60 seconds, acknowledged it, and displayed the matching charge. A temporary exact test rule returned 5 cents for 60 seconds and exercised the corresponding spoken amount. The original rate was restored after that call.

The demo also completed a toll-free call through the local line simulator with no type 63 request. The screening record used `0xFF` as the rate index for both free entries. A separate outbound SIP test from the emulator received answer supervision and inbound audio.

## Native exchange and payment verification

The WebUI imported two adjacent exchanges into MT101 and saved their distinct tariff indices. The OEM acknowledged the segment and its companion MT73 during a ten-record download. Both records matched the stored bytes after shutdown. The warm phone quoted 25 and 15 cents for the two exchanges with no MT63 traffic. It credited a nickel and a dime on the 15-cent call and dialed through the local line simulator.

The OEM initially rejected a synthetic magnetic stripe on the host-rated route before sending authorization. A carrier record alone left the rejection unchanged. After removing the route's MT92 coin-only restriction and retaining an MT23 carrier that accepts the range, the phone completed MT63/64, then MT2/65. The controlled decline was displayed on the phone. A separate host-rated call accepted coins and entered dialing. Neither runtime dialogue downloaded tables.

The local lab's corrected 12-record set received all 12 application acknowledgements. The warm phone rendered both existing text advertisements. Some craft exits in the emulator required a warm restart before the service probe. Provider-backed approval and settlement remain unverified.
