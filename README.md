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
- **Cindara.Xbox** will be a native UWP/XAML shell using `MediaPlayerElement`.
  It will consume the same core contracts and prefer direct play, falling back
  to Jellyfin HLS transcoding when Xbox codecs require it.

The Xbox project is intentionally not scaffolded yet: the current environment
did not have Visual Studio or UWP build tools installed, so a generated project
could not be validated. To add it correctly, install **Visual Studio 2022** with
the **Universal Windows Platform development** workload
(`Microsoft.VisualStudio.Workload.Universal`), a Windows 10 SDK, and the C# UWP
tools. Deploying to a retail Xbox additionally requires activating Developer
Mode and pairing through Xbox Device Portal.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), as pinned
  by `global.json`
- Windows, Linux, or macOS supported by Avalonia
- Visual Studio 2022 with the UWP workload only when developing the future Xbox
  shell

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
4. Add the validated UWP/XAML Xbox shell and `MediaPlayerElement` playback with
   HLS transcoding fallback.
5. Add packaging, accessibility, localization, and release automation.
