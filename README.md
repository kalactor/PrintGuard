# PrintGuard

PrintGuard is an offline Windows 10/11 print firewall app.
It runs in the tray, blocks new print jobs immediately, asks for password, and only then releases the job.

## Tech Stack

- .NET 8
- WPF desktop app (`PrintGuard.App`)
- `System.Printing` + WMI (`Win32_PrintJob`)
- Offline only (no telemetry, no cloud)

## Solution Structure

- `src/PrintGuard.App`
  WPF tray UI app.
- `src/PrintGuard.Core`
  Print watcher, queue control, password security, config, logging, startup.
- `tests/PrintGuard.Tests`
  Unit tests for config and password logic.

## Tray Icon

Tray support is implemented with `System.Windows.Forms.NotifyIcon` (`TrayManager`).

Tray menu:

- Enable/Disable protection
- Unlock for 5/15/60 minutes
- Open Settings
- View Logs
- About
- Exit

Sensitive actions require admin password:

- Disable protection
- Unlock for minutes
- Open Settings
- Exit

Settings includes:

- Change admin password

## How Print Blocking Works

When protection is enabled:

1. WMI subscribes to `__InstanceCreationEvent` for `Win32_PrintJob`.
2. Polling watcher runs as fallback (`PollingIntervalMs`).
3. For each new job on protected printers, PrintGuard pauses the job.
4. Password prompt opens (topmost) with printer/document details.
5. Correct password:
   - resume just that job, or
   - unlock for prompt duration if selected.
6. Wrong password:
   - job stays paused
   - lockout is enforced
   - optional cancel on repeated failed unlocks.

Fallback if per-job pause is unsupported:

- pause entire printer queue when possible
- otherwise optionally cancel the job for safety and authorize one reprint/timed unlock

## Password Security

- First run requires admin password creation.
- Stored in `%AppData%\PrintGuard\config.json`.
- Only PBKDF2 hash + random salt are stored.
- No plaintext password storage.
- Lockout defaults:
  - 5 failed attempts
  - 60 seconds lockout

## Build

```powershell
dotnet restore
dotnet build PrintGuard.sln
```

Run app:

```powershell
dotnet run --project src/PrintGuard.App/PrintGuard.App.csproj
```

Run tests:

```powershell
dotnet test tests/PrintGuard.Tests/PrintGuard.Tests.csproj
```

## Startup Notes

- Startup is controlled by HKCU Run registry key (`StartupManager`) for the unpackaged desktop app.

## Known Limitations

- Some printer drivers (especially virtual printers) may not support per-job pause/resume reliably.
- Queue-level pause fallback may affect multiple jobs on same printer.
- Process-level allowlisting is intentionally omitted because reliable source process attribution is not consistently available from spooler metadata.

## Privacy

PrintGuard has no hidden behavior:

- no keylogging
- no document exfiltration
- no remote calls
