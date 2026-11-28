# Authorized code-server session

The incoming code-server session is the transport component for a terminal-initiated transfer. It accepts the observed full-download request shapes for initial and resumed requests, using one CRC-checked XMODEM request block. It does not authorize a terminal or select a job by itself.

Before any image bytes are sent, the caller must independently match the request to an approved job, its retained schedule, the exact permitted block selection and the registered artifact. The caller must also acquire the durable execution claim for that job. The session requires an explicit grant and checks that the requested blocks exactly match it, that all selected blocks exist within the image and that the image digest matches the grant. No default image or block selection is available.

Request and response application data are processed in memory; only bounded wire-event metadata is returned. The result separates request reception, authorization, transport completion and application response. A completed transfer does not establish that the terminal has restarted, reported the target version or returned to service. If the session is interrupted after authorization, reconcile the outcome before taking another execution action.

The unit tests use generated images and independent in-memory endpoints. They cover exact grants, rejected requests, changed image digests, mismatched blocks and bounded request/response handling. These tests do not qualify a physical terminal or a Manager listener. The incoming listener and its job authorization integration remain separate from this component.
