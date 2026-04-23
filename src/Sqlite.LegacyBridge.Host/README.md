# Sqlite.LegacyBridge.Host

Compatibility host process for encrypted legacy SQLite access in environments that depend on `System.Data.SQLite` behavior.

## Runtime purpose

This executable is responsible for:

- opening legacy encrypted databases (including RC4/RSA-era compatibility profiles)
- executing SQL operations requested by the client bridge
- returning results through a stable named-pipe protocol

## Typical startup contract

```powershell
Sqlite.LegacyBridge.Host.exe --pipe <pipe_name> --database <path_to_db>
```

## Configuration surface

- `PLU_SQLITE_PASSWORD`: database password for encrypted files
- `PLU_SQLITE_DB`: fallback path when database is not passed as argument

## Lifecycle

1. Process starts and initializes pipe server
2. Readiness signal is emitted
3. Client acknowledgment is received
4. Request loop processes NDJSON commands
5. Process exits when session is closed/terminated

## Integration path (recommended)

Most consumers should not manage this executable directly.  
Preferred flow:

1. Install `EntityFrameworkCore.Sqlite.Legacy` (or `.Ef31`)
2. Run `SqliteLegacyDbContextOptionsExtensions.SetupBridgeHost()`
3. Configure EF with `UseSqliteLegacy(...)`

## Deployment notes

- Keep host and native SQLite binaries in the same `legacy/` folder
- Respect architecture constraints from native provider distribution
- For locked-down environments, pre-distribute host release ZIP internally

## Troubleshooting

- **Startup hang**: check pipe permission/collision and process block by security software.
- **`file is not a database`**: verify file path, encryption profile, and password.
- **Exit on boot**: missing native binaries or architecture mismatch.
- **Interop errors**: ensure all binaries are built for compatible architecture (`x86` commonly required in old stacks).

