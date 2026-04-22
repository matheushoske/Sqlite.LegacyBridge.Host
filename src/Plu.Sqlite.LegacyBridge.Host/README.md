# Sqlite.LegacyBridge.Host

`Sqlite.LegacyBridge.Host` is the .NET Framework executable that opens the legacy encrypted SQLite database with `System.Data.SQLite` and serves requests over named pipes.

## Usage

```powershell
Sqlite.LegacyBridge.Host.exe --pipe <pipe_name> --database <path_to_db>
```

Environment variables:

- `PLU_SQLITE_PASSWORD`: password used to open the database.
- `PLU_SQLITE_DB`: optional fallback database path.

## Protocol

- Host sends `PLU_LEGACY_BRIDGE_READY`.
- Client replies with `PLU_LEGACY_CLIENT_ACK`.
- Communication continues as line-delimited JSON (NDJSON).

## Release artifact expectations

- Release package should include:
  - `Sqlite.LegacyBridge.Host.exe`
  - required `System.Data.SQLite` native files (`SQLite.Interop.dll` etc.)
- Recommended release matrix includes `x86` and optional `x64` where supported.

## Troubleshooting

- If startup hangs, verify antivirus exclusions and pipe accessibility.
- If DB open fails (`file is not a database`), validate password/encryption compatibility.
- If process exits immediately, verify native SQLite runtime files are present.

