# Link a code job to a staged transition

An incoming code-server approval is distinct from a manual or outbound transfer approval. A scheduled approval identifies one terminal, one published code-only MT94 schedule, one reviewed configuration transition, and one registered primary artifact.

1. Register the package with its exact target firmware registry version. Add an explicit compatibility rule for the installed source version; a name or a hardware routing hint alone does not establish compatibility.
2. Upload the matching primary artifact. Register the expected size and SHA-256 before uploading.
3. Prepare the published MT94 schedule and stage the target configuration using the existing transition review. The source assignment remains active until the separately observed target-version activation.
4. Create a live-capable job for that terminal and package, select its primary artifact, and run the host simulation.
5. Open the job and choose the matching staged transition under **Delivery configuration**. Review the source and target labels and enter rollback notes, then approve.

The approval checks the retained configuration and exact code record, the latest matched source version, and the registered primary artifact bytes and selected blocks. If MT94 has already been delivered, the exact completed batch and application acknowledgement are also verified. The recorded artifact digest and length, transition and schedule are immutable for this approval; a second job cannot claim that schedule. An approval linked to a scheduled transition cannot be executed through the outbound transport.

The job may be cancelled before an execution attempt. This cancels Manager authorization, not a scheduling record already stored at the terminal; use the separate MT94 cancellation workflow for that. A new delivery approval requires a newly reviewed schedule.

The Manager listener and its independent request matching are a subsequent integration step. The linked approval alone does not produce a code-server connection, transfer, restart or service observation.
