# Architecture overview

The host platform separates **protocol fidelity** (parse, retain bytes, diagnostics) from **business workflows** (rating MVP, download orchestration metadata, simulation gateways).

!!! tip "IMPLEMENTED"
    Clean layering: API controllers thin; protocol logic in `HostPlatform.Protocols.*`; persistence in Infrastructure.

See also: [Domain model](domain-model.md), [Data flow](data-flow.md).
