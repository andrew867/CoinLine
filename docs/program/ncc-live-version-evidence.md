# Reported controller versions and firmware selection

The firmware scheduler can select phones by the controller ROM and software labels in their latest complete MT60 report. These labels are observations of the running terminal, not registered inventory versions or proof that a code download succeeded.

Open a software-version event from Terminal Status to see whether the report was observed on a matched NCC connection. Diagnostic imports and previously stored reports without live evidence remain visible for troubleshooting, but do not determine version-based schedule eligibility. After upgrading Manager to this schema, let each phone report again over its normal NCC dialogue before using the version filter. The ordinary wire ID, customer, group and other selection methods are unchanged.

The new evidence record is created in the same database transaction as the complete MT60 received by the NCC listener. A diagnostic import does not create that evidence, even when its session reference is present. A later live report replaces the earlier version for selection; a newer diagnostic entry does not. Preview and scheduling evaluate the same gate.

This is a prerequisite for a reviewed configuration transition after remote code download. It does not activate a new firmware registry entry, change a phone's assigned tables, initiate a code download or establish code-download compatibility. Actual code receipt, reboot into the specified compatible version, target table acknowledgements, service and rollback qualification remain independent milestones.
