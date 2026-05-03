# Solution structure

| Project | Role |
|---------|------|
| `HostPlatform.Api` | HTTP API, controllers, middleware, `Program.cs` |
| `HostPlatform.Domain` | Entities, enums |
| `HostPlatform.Infrastructure` | `HostPlatformDbContext`, DLOG engine, table services, persistence |
| `HostPlatform.Application` | Application layer (if present) |
| `HostPlatform.Protocols.Ncc` | NCC codec, stream reader |
| `HostPlatform.Protocols.Dlog` | DLOG registry, classifier, hex utilities |
| `HostPlatform.Protocols.Tables` | Table helpers / placeholders |
| `HostPlatform.Rating` | `RatingEngine` |
| `HostPlatform.Cards` | Card ledger surface |
| `HostPlatform.Craft` | Craft placeholders |
| `HostPlatform.Firmware` | Firmware safety gates |
| `HostPlatform.Worker` | Background worker host |
