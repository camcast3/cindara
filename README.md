# Cindara

Cindara is a premium-feeling, native [Jellyfin](https://jellyfin.org/) client
for Linux, Windows, and macOS. Linux and couch-first systems such as Bazzite are
the primary target. It is early-stage, public, and licensed under the
[MIT License](LICENSE).

## Current vertical slice

The Avalonia desktop shell validates a Jellyfin server, authenticates a user,
and restores independent saved accounts across servers. Passwords are never
persisted. Access tokens are stored with Secret Service on Linux, DPAPI on
Windows, or Keychain on macOS; rejected tokens remove only the affected
account and return it to sign-in.

The controller-first shell exposes Home, Libraries, Search, Downloads, and
Settings. Library/search/download content remains explicitly unavailable until
its roadmap work lands. Signing in opens the current media Home directly, without
a preview launcher, top tab bar, or redundant Home-screen back button. The Home sidebar's
Settings action opens the in-app settings.
Settings contains exactly **Language: English**, **Exit**, and **Back to Home**.
Expanded account, display, input, and appearance settings are deferred to
[the settings feature request](https://github.com/camcast3/cindara/issues/27).
D-pad/left stick or arrows navigate, Accept/Enter selects, and Back/Escape
dismisses dialogs or returns to the navigation rail. Start/Options/+ or F11
toggles fullscreen; **Settings → Exit** closes the app.
Accept on a text field opens an on-screen keyboard. Saved accounts use the same
focus-trapped choice dialog as the rest of the shell.

The login screen and **Settings** offer **Language: English**.
English is the only supported UI language. Appearance controls are deferred;
the text/contrast/motion foundation remains covered by developer tests but is
not exposed or restored from saved appearance settings. `CINDARA_CULTURE`
controls regional formatting; `qps-ploc` and `qps-plocm` are developer-only
expanded and right-to-left pseudo-localization modes.
See [accessibility and localization](docs/accessibility.md) for defaults,
limitations, validation, and the keyboard/screen-reader release checklist.

Home loading overlaps metadata and artwork with at most six requests in flight,
deduplicates images within that load, and has a 30-second overall deadline.
**Cancel loading** or Back/Escape cancels the request; failures retain sign-in and
offer Retry (except a rejected session, which returns to sign-in). Returning
from Settings reuses the current account's loaded Home instead of downloading
it again. Account switching and sign-out clear that data.

SDL3 handles controller hotplug, directional repeat, and active-device prompts
without resetting focus. Input received while the window is inactive is
discarded; held controls must return to neutral after reactivation. Xbox uses
A/B, PlayStation Cross/Circle, and Nintendo B/A for the same physical
south/east accept/back positions. Keyboard and mouse remain available without
a controller.

Credential-bearing requests require HTTPS. Plain HTTP is accepted only for
loopback development servers.

Each OS-protected credential binds its token to the canonical server URL
(scheme, host, port, and base path), server ID, and user ID. The plaintext
`sessions.json` index is used for account selection, not as authority for a
token's destination. Restoration rejects metadata that differs from the protected
binding before making any authenticated request. The entire protected credential
is preserved when a save or removal is rolled back.

Older, token-only credentials cannot be safely upgraded using the plaintext
index. They are rejected without sending the token. Remove the saved account,
reconnect using a verified server address, and sign in again. The same recovery
applies when saved metadata and its protected credential disagree.

## Architecture

```text
Cindara.sln
├── src/Cindara.Core          Shared Jellyfin, identity, session, and playback contracts
├── src/Cindara.Desktop       Avalonia shell with native SDL3 controller input
├── tests/Cindara.Core.Tests  Shared-core unit tests
└── tests/Cindara.Desktop.Tests
                              Desktop input, navigation, and headless UI tests
```

- **Cindara.Core** has no UI or platform dependencies. It owns Jellyfin API
  access, server identity, authentication/session contracts, multi-server
  session storage, and playback negotiation contracts.
- **Cindara.Desktop** is the Avalonia composition root. A platform playback
  implementation will eventually use LibVLCSharp or libmpv behind
  `IPlaybackNegotiator`. SDL3 supplies standardized native gamepad mappings,
  hotplug events, D-pad/left-stick navigation, and controller actions across
  Linux, Windows, and macOS.

Xbox-native support is out of scope. A compact Bazzite or SteamOS device
connected to a TV is the reference ten-foot experience.

The shared ten-foot visual tokens, reusable Avalonia component styles, viewport
scaling rules, screen wireframes, and controller focus graphs are documented in
[the Cindara design system](docs/design-system.md).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), as pinned
  by `global.json`
- Windows, Linux, or macOS supported by Avalonia
- On Linux, a desktop session with graphics and controller access. SDL3 native
  runtimes are bundled by NuGet; no system SDL package is required.
- On Linux, a Secret Service provider and the `secret-tool` command (commonly
  provided by `libsecret-tools`) are required to persist Jellyfin sessions.

## Build

```shell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project src/Cindara.Desktop
```

Formatting is enforced in CI:

```shell
dotnet format --verify-no-changes
```

## Local AI pull request review

The repository includes a read-only `review-committee` Copilot custom agent in
`.github/agents/review-committee.agent.md`. Select **review-committee** from the
Copilot agent picker, attach the pull request diff or changed-files context with
its base and head identifiers, then ask the committee to review it.

The chair runs independent reviews with GPT, Gemini, and Microsoft MAI model
families, verifies and deduplicates their findings, and produces one local
report. It does not edit code or publish a GitHub review.

## Focused roadmap

1. Authenticate users and persist encrypted sessions for multiple servers.
2. Build a ten-foot library browser with complete controller navigation,
   hotplug handling, focus recovery, and controller glyphs.
3. Negotiate desktop playback through LibVLCSharp or libmpv.
4. Package and test on Bazzite/SteamOS, including Flatpak and Steam shortcuts.
5. Add accessibility, localization, and release automation.
