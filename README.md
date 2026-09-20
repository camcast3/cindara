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

## Focused roadmap

1. Authenticate users and persist encrypted sessions for multiple servers.
2. Build a ten-foot library browser with complete controller navigation,
   hotplug handling, focus recovery, and controller glyphs.
3. Negotiate desktop playback through LibVLCSharp or libmpv.
4. Package and test on Bazzite/SteamOS, including Flatpak and Steam shortcuts.
5. Add accessibility, localization, and release automation.
