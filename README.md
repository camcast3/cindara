# Cindara

Cindara is a premium-feeling, native [Jellyfin](https://jellyfin.org/) client
for Linux, Windows, and macOS. Linux and couch-first systems such as Bazzite are
the primary target. It is early-stage, public, and licensed under the
[MIT License](LICENSE).

## Current vertical slice

The Avalonia desktop shell uses an ambient, step-by-step authentication flow:
choose a saved server, choose a saved account for that server, then restore the
protected session or sign in when credentials are required. New servers can be
added from the first step. Passwords are never
persisted. Access tokens are stored with Secret Service on Linux, DPAPI on
Windows, or Keychain on macOS; rejected tokens remove only the affected
account and return it to sign-in.

The controller-first shell exposes Home, Libraries, Search, Downloads, and
Settings. Home keeps its existing sidebar, hero, and rows. Selecting any other
destination opens a dedicated full-screen surface with one explicit Back path;
non-Home surfaces do not repeat the Home sidebar. All screens share Home's capped
logical-viewport density model for typography, poster sizing, gutters, forms, and
navigation actions; OS DPI is not applied twice. Home includes a single Continue
Watching row and recently added media
from the selected libraries, with no generic Libraries shortcut row. Open libraries
from the sidebar or library chooser. Home library shortcuts use aligned TV, Movie,
and Anime icons with accessible names. A library opens a virtualized poster grid
headed by its actual name; the heading opens a library switcher. Compact filter
and sort menus show the active All titles/Unwatched/Favorites and Title A–Z/Z–A
choices, alongside the total title count. The All/A–Z rail remains directly
accessible. Menus focus the current choice and Back restores the launcher without
changing the grid. Metadata loads in bounded 40-item batches as focus or scrolling reaches
the final loaded row. A rolling buffer of 60 blank poster slots (capped to the
remaining titles) is present before the next request; responses fill those slots
in place and extend only the far end. Poster and metadata heights stay stable.
There are no visible pages or replacements of earlier items. Selecting a
card opens a read-only summary; Back restores the exact card, query controls, loaded
batches, and grid position. Search supports debounced physical-keyboard
input, a temporary full-screen controller keyboard, one combined movie/series/season/
episode poster grid, bounded paging, cancellation, and exact query/focus/scroll
restoration. Full details and episode navigation remain
deferred to the remaining issue #5 batches; downloads remain deferred. Signing in opens media Home directly, without
a preview launcher, top tab bar, or redundant Home-screen back button. The Home sidebar's
Settings action opens the in-app settings.
Settings uses a controller-first category/detail layout while retaining only
**Language: English**, **Library layout**, **Exit**, and **Back to Home**.
Library layout has independent ordered selections for sidebar shortcuts and Home
libraries. Use each library's Hide/Show and Move up/Move down buttons, then Save layout; Cancel
discards the draft. Defaults are TV libraries, Movies, then Anime. Choices are
saved separately for each account/server address on this device under
`library-layouts` in Cindara's local application data. They do not change Jellyfin
permissions or other devices. Save failures preserve the active layout; unreadable
settings show a warning before the user chooses to replace them.
Only Jellyfin movie and TV-show library types are offered for browsing or layout
selection (including Anime libraries using the TV-show type). Unsupported views
such as Collections and People are omitted, and no latest-media requests are made
for them. Previously saved selections cannot bring unsupported views back.
Minimal account switching, logout, and fullscreen/windowed controls are tracked
separately in [the settings feature request](https://github.com/camcast3/cindara/issues/27);
expanded settings categories, input, and appearance preferences remain deferred.
The pre-login **Diagnostics** action and the signed-in Settings category open the
same full-screen local technical view with sanitized errors and an explicit
support-bundle preview/export.
It makes no diagnostic network requests or uploads. Logs are capped at 1 MiB
and retained for seven days; see [diagnostics and privacy](docs/diagnostics.md).
D-pad/left stick or arrows navigate, Accept/Enter selects, and Back/Escape
dismisses the deepest utility or returns from a full-screen destination to its
exact Home source. Start/Options/+ or F11
toggles fullscreen; **Settings → Exit** closes the app.
Accept on authentication text fields opens a focus-trapped on-screen keyboard.
Search uses an inline controller keyboard so results and query context remain visible.
Saved servers and accounts are controller-navigable steps rather than a modal picker.

The login screen and **Settings** offer **Language: English**.
English is the only supported UI language. Appearance controls are deferred;
the text/contrast/motion foundation remains covered by developer tests but is
not exposed or restored from saved appearance settings. `CINDARA_CULTURE`
controls regional formatting; `qps-ploc` and `qps-plocm` are developer-only
expanded and right-to-left pseudo-localization modes.
See [accessibility and localization](docs/accessibility.md) for defaults,
limitations, validation, and the keyboard/screen-reader release checklist.

Home loads metadata and artwork with at most six requests in flight and a
30-second overall deadline. Library metadata has the same deadline, but the
grid appears immediately without waiting for posters. Up to six poster requests
then run in the background, each with a 15-second request timeout; navigation and
card summaries remain available. Only nearby virtualized rows retain decoded images;
moving away disposes them while the bounded byte cache can serve a later reload.
Failed/missing posters show an explicit placeholder and **Retry missing artwork**.
Leaving the library, changing the query/account, or loading another batch cancels
superseded artwork work. Library batches do not eagerly fetch hero backdrops.
Duplicate images share a request within each artwork window.
An in-memory LRU artwork cache holds at most 128 entries / 32 MiB and uses a
five-minute **cache-wide expiry window**, checked on lookup. The first lookup
after the deadline clears the cache and starts a new window; an image added near
the deadline can therefore expire sooner than five minutes after insertion.
This is not a per-image minimum lifetime. Per-image expiry is deferred to
[the caching follow-up](https://github.com/camcast3/cindara/issues/10).
The cache is isolated to the exact authenticated session (including its token).
Account changes, sign-out, and shutdown clear it; no artwork is cached on disk.
An artwork decode failure also clears the reusable image cache so Retry can fetch
fresh bytes rather than repeatedly decoding the same corrupt response. Existing
displayed cards/images are retained, and ordinary network failures do not clear it.
Each HTTP response is limited to 8 MiB.
Continue Watching combines Jellyfin resume and next-up results into at most 20
cards, with only one episode per series. The most recently played resume episode
takes priority; below 90% watched it resumes, while at or above 90% it advances
to the following unplayed episode in Jellyfin's episode order. Completed movies
are omitted. The rule uses existing Jellyfin APIs, requires no plugin, and does
not mark anything watched or alter server progress. Unstarted shows are not
suggested by the next-up query. Only movies and episodes qualify; series and season
containers are excluded. Selection happens before artwork is downloaded.
The whole row is ordered newest-first by the last playback anywhere in each
series (including completed episodes and rewatches), not by the unplayed next
episode or by whether a card is resumable. Movies use their own last-played
timestamp. Equal timestamps keep server order; missing timestamps sort last.
For two or more candidate series, one bounded request reads the 200 most recently
played episode records and supplies timestamps for matching series. Only series
absent from that activity window need an exact per-series lookup; one-series
loads use that lookup directly. All lookups share the existing six-request cap
and 30-second Home deadline. The candidate set is not trimmed before ranking.
**Cancel loading** or Back/Escape cancels the request; failures retain sign-in and
offer Retry (except a rejected session, which returns to sign-in). An unsuccessful
incremental load keeps every loaded poster and the reserved buffer, replacing the
first blank with a **Retry loading more** tile for the failed offset. Empty libraries
and missing artwork have visible states. Returning
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
