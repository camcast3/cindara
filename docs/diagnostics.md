# Private local diagnostics

Diagnostics never uploads, makes network requests, installs analytics/crash
agents, or generates a device/installation tracking identifier. The existing
Jellyfin device identity is **not** copied into logs or bundles. Product requests
remain user-driven and keep their existing credential transport rules.

## Support workflow

1. Reproduce the problem, then select **Diagnostics** in the login/shell footer.
   From media Home, open **Settings**, then the footer's **Diagnostics** button.
   Settings itself contains Language, Library layout, Exit, and Back to Home.
2. Read app/.NET/Avalonia versions, platform/process architecture, the configured
   Skia renderer, SDL3 availability/count, and recent sanitized errors.
   GPU acceleration/driver details are not probed. Playback is explicitly
   **not implemented** until roadmap #8; diagnostics does not introduce a player.
3. Select **Preview support bundle**. No archive is written yet. The screen shows
   its full destination under `Cindara/support-bundles` in the OS local application
   data directory, and the exact included files and uncompressed byte counts:
   `environment.json`, `events.jsonl`, and `privacy.txt`.
4. Select any file to review all of its contents with Previous/Next page.
   The snapshot is immutable: later log events do not change the reviewed export.
   Select **Export reviewed bundle** to write the ZIP, or Cancel/Back to leave
   without exporting. Existing exports are never overwritten. No native file
   dialog is required; the whole flow supports controller, keyboard, and mouse.
5. The success screen repeats the destination. Share the ZIP manually, only with
   a trusted maintainer. Delete it when no longer needed. Exports are not
   automatically rotated/deleted and are never automatically attached to issues.

The destination is displayed locally, **not** recorded in the bundle or log.
Export writes an exclusive `.partial` file beside the destination and moves it
only after the ZIP is complete. Normal failures/cancellation clean up partials;
a process kill/power failure can leave one for manual removal.

## Collected data and redaction boundary

Each JSON-lines event has a timestamp, process start timestamp, process-local
operation counter (reset on launch), level, area, action, outcome, optional
duration/HTTP status, and allowlisted error codes. The start timestamp distinguishes
counter reuse across launches; it is not a persistent identity.

**Redaction is by omission, not a best-effort regex.** The writer accepts typed
enums/numbers and exceptions projected onto an allowlist of domain error codes and
known exception categories. It never serializes exception messages, stack traces,
`Data`, arbitrary type names, request/response bodies, headers, URLs (including
userinfo, query strings, fragments), passwords, tokens, paths, usernames, server
names/IDs, library metadata, media titles, controller names/IDs, machine names,
environment variables, session indexes, protected credentials, or settings files.
Nested/aggregate exceptions are bounded to eight codes. Unknown types become
`Unknown`. Framework free-form trace logging is not enabled by the app.

This deliberately sacrifices raw stacks/messages and server-specific response
details for privacy. Timestamps, versions, error patterns, durations, HTTP status
codes, platform, and controller count can still reveal activity or technical
details; review before sharing. Diagnostic files are not encrypted. Windows uses
the user's application-data directory permissions; newly created Unix log
directories are mode 700, and log/export files mode 600. Host-managed crash dumps
or independently redirected third-party process output are outside this exporter.

## Limits and failures

- Logs live under `Cindara/diagnostics` in
  `Environment.SpecialFolder.LocalApplicationData` (typically
  `%LOCALAPPDATA%` on Windows and `~/.local/share` on Linux; packaged locations
  may differ). Startup opens the logger before Avalonia/credential initialization.
- Debug, Information, Warning, and Error levels are supported; the default minimum
  is Information. No user configuration or expanded Settings surface is added.
- At most **four 256 KiB JSONL files** (**1 MiB total**) survive each write.
  Files older than **seven days**, excess files, and oversized files are removed
  on startup, write, or preview. No cleanup runs while the app is stopped.
- The in-app error list holds at most **30 warnings/errors from this run**.
  Bundles include retained on-disk events from earlier runs too.
- Logging storage failures do not break sign-in/playback-adjacent product work:
  a sanitized stderr notice and in-app storage status expose the failure, and the
  recent-error queue remains available. Storage health reflects the latest write;
  gaps while storage was unavailable cannot be reconstructed.
- Snapshot reads validate size/schema/codes and reserialize typed records rather
  than copying raw log bytes. Invalid JSON/fields or unreadable files fail the
  preview explicitly; unknown JSON properties are omitted. Symbolic-link log files
  are rejected. No other application-data files are included.

## Interpreting events / extending integrations

Group by `RunStarted` and `Operation`, then order by timestamp. Authentication and
Home-load operation scopes carry through async network and session-store work.
Network events supply HTTP status/duration without recording the target, headers,
or bodies; storage events distinguish restore/save/remove failures and expose
allowlisted nested credential/index error codes. SDL initialization, open failures,
hotplug, startup/shutdown, and unavailable playback are recorded without native
error text or hardware identifiers. Cancellation is distinct from failure where
reported by the underlying integration.

Future playback/caching/packaging milestones should use `LocalDiagnostics` with
new reviewed enum values and error classifications, not introduce free-form
messages or serialize backend exceptions. The current playback event is an
honest unavailable marker, not playback-failure coverage for an unbuilt player.

## Validation

Core tests cover credential/header/URL/path/exception redaction, nested exception
bounds, async correlation, request pass-through/no diagnostic network activity,
storage wrapping, rotation/aggregate limits, retention, level filtering, invalid
logs, immutable ZIP previews, explicit export, non-overwrite, and cancellation
cleanup. Desktop tests cover authentication/native-controller failure events and
headless modal navigation, focus restoration, preview/cancel, English and RTL
pseudo-localization. Real Bazzite/controller/TV, native screen readers, and macOS
filesystem behavior still require platform release testing; headless coverage
does not claim physical-device certification.
