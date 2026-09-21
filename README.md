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
                              Desktop input mapping tests
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
