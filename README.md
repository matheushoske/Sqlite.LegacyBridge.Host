# Sqlite.LegacyBridge.Host

Native-compatibility runtime for encrypted legacy SQLite databases (RC4/RSA-era stacks) consumed by modern .NET applications.

This repository ships the host executable used by:

- [EntityFrameworkCore.Sqlite.Legacy](https://github.com/matheushoske/EntityFrameworkCore.Sqlite.Legacy)

## Why this host exists

In many production legacy systems, database access depends on historical `System.Data.SQLite` behavior plus native interop assumptions that are hard to replicate with modern managed-only providers.

This host was created to preserve compatibility while enabling modernization:

- keep legacy encrypted databases operational
- keep existing encryption/key conventions
- move business logic to EF Core gradually
- reduce migration risk in critical enterprise systems

## Encryption and compatibility focus

The host is designed for legacy provider ecosystems where encrypted databases may follow RC4/RSA-oriented workflows historically used by old commercial tooling.

What this means in practice:

- It prioritizes provider compatibility over "latest-only" APIs.
- It allows client applications to keep EF abstractions while host handles legacy opening semantics.
- It preserves access paths that often fail in direct `Microsoft.Data.Sqlite` scenarios.

## Role in the complete architecture

`EF Core app` -> `Legacy bridge client` -> `Named pipes` -> `Sqlite.LegacyBridge.Host (net462)` -> `System.Data.SQLite` -> `Encrypted SQLite file`

The host runs as a local process and acts as the compatibility boundary.

## How consumers get this host

Applications using `EntityFrameworkCore.Sqlite.Legacy` can auto-provision host binaries via:

```csharp
SqliteLegacyDbContextOptionsExtensions.SetupBridgeHost();
```

Download source used by default:

- `https://github.com/matheushoske/Sqlite.LegacyBridge.Host/releases/latest/download/Sqlite.LegacyBridge.Host.zip`

## Release contract

Each GitHub release provides one stable artifact:

- `Sqlite.LegacyBridge.Host.zip`

Expected zip content:

- `Sqlite.LegacyBridge.Host.exe`
- managed dependencies
- native dependencies (`SQLite.Interop.dll`, etc.)

## CI/CD

- Workflow: `.github/workflows/host-release-sqlite-legacy-bridge.yml`
- Builds on `net462`
- Validates generated executable before packaging
- Publishes release with generated `host-v*` tag

## Protocol model (high level)

- Local named pipe transport
- Ready/ack handshake
- NDJSON command flow for requests/responses

## Security and operations

- Keep DB passwords in secrets/env configuration, not code
- Apply antivirus allowlisting for host process and pipe runtime when needed
- Keep host and native SQLite binaries side-by-side
- Validate process architecture compatibility in old deployments

## Repository layout

- `src/Sqlite.LegacyBridge.Host` - host executable (`net462`)
- `src/Sqlite.LegacyBridge.Protocol` - shared protocol contracts

