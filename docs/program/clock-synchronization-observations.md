# Clock synchronization observations

The host responds to an ordinary terminal MT36 clock request with an MT20 containing the current UTC calendar in binary fields. The year is counted from 1900. The response has one-second precision. A terminal may later report a different local calendar, so the host's transmitted value, receipt time and terminal's stored time must not be conflated.

The NCC session detail now includes the exact offered UTC calendar and frame sequence for each successfully transmitted MT20, including a retransmission of the same value. This small semantic audit entry is stored with the existing outbound frame metadata. Raw outbound data remains omitted from storage. There is no change to the clock response, retry sequence or terminal scheduling rules.

Open a retained session in the WebUI to inspect its clock offers. The console requests only the clock metadata, not the underlying session frames. A missing observation on an older session does not establish that no synchronization occurred. An offered value is not proof that the terminal acknowledged or stored it. Verify the terminal's actual clock before scheduling a timed device action.
