# Cindara

Cindara is a premium-feeling, native [Jellyfin](https://jellyfin.org/) client for
Windows, Linux, macOS, and Xbox. It is early-stage, public, and licensed under
the [MIT License](LICENSE).

## Current vertical slice

The Avalonia desktop shell accepts a Jellyfin server address and validates it
against Jellyfin's unauthenticated `System/Info/Public` endpoint. The shared
core normalizes server URLs, returns a stable server identity, and exposes
specific failures for invalid addresses, unreachable servers, timeouts,
authorization failures, HTTP errors, and malformed responses.

## Architecture

```text
Cindara.sln
├── src/Cindara.Core          Shared Jellyfin, identity, session, and playback contracts
├── src/Cindara.Desktop       Avalonia shell for Windows, Linux, and macOS
└── tests/Cindara.Core.Tests  Shared-core unit tests
```

- **Cindara.Core** has no UI or platform dependencies. It owns Jellyfin API
  access, server identity, authentication/session contracts, multi-server
  session storage, and playback negotiation contracts.
- **Cindara.Desktop** is the Avalonia composition root. A platform playback
  implementation will eventually use LibVLCSharp or libmpv behind
  `IPlaybackNegotiator`.
- **Cindara.Xbox** will use Microsoft's supported Xbox development and
  publishing path. The primary direction is a GDK/Win32 shell or an applicable
  Xbox Managed Program, with the final platform boundary selected after
  onboarding with Microsoft. It will preserve the same server, session, and
  playback semantics as the shared core, prefer direct play, and fall back to
  Jellyfin HLS transcoding when Xbox codecs require it.

The Xbox project is intentionally not scaffolded yet: the current environment
does not have an approved Xbox toolchain that could produce and validate a
submission-ready project. New Xbox products should pursue the **Microsoft Game
Development Kit (GDK)** with Win32 or the appropriate **Xbox Managed Program**.
Console GDK access and publishing require enrollment and approval through
Microsoft's Xbox developer programs; install the GDK and its documented Visual
Studio components after access is granted.

UWP remains technically compatible with Xbox Series X|S for existing and
backward-compatible apps, but Microsoft has deprecated UWP as a target for new
Xbox Creators Program submissions. It is therefore not Cindara's default Xbox
architecture.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), as pinned
  by `global.json`
- Windows, Linux, or macOS supported by Avalonia
- For Xbox development, approved access to the GDK or applicable Xbox Managed
  Program and the Visual Studio components specified by that program

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
2. Browse Jellyfin libraries with responsive, controller-friendly navigation.
3. Negotiate desktop playback through LibVLCSharp or libmpv.
4. Complete Xbox program onboarding, validate the GDK/Win32 or managed
   architecture, and add native playback with HLS transcoding fallback.
5. Add packaging, accessibility, localization, and release automation.
