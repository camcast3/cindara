# Cindara ten-foot design system

Cindara uses a cinematic, calm interface optimized for a controller at TV
distance. Modern media apps are a usability benchmark, but Cindara's teal
accent, near-black layered surfaces, typography, copy, shapes, and artwork
treatment are original. Product UI must not reuse another product's assets,
branding, exact layout, or proprietary interaction.

## Information architecture

The production information architecture exposes **Home**, **Libraries**, **Search**,
**Downloads**, and **Settings**. Home alone owns the persistent icon rail, hero,
and media rows. Selecting a non-Home destination opens a dedicated full-screen
surface with an explicit Back action; it does not repeat the Home rail. Downloads
is a visible placeholder until offline media ships. Details are entered from
content, and playback is a temporary full-screen layer.

The authenticated Home uses a compact icon rail with Search, Home,
Libraries, and Settings. The product owner removed the speculative
Home/Trending/Activity/Profile top bar. Home focuses the media content and Settings
opens the signed-in settings. Libraries use the account's actual server-provided
entries rather than hard-coded TV/Movie/Anime shortcuts. Search leads to its
inline controller keyboard, focused-result context, and grouped horizontal result
rails. Full details and playback remain assigned to the remaining #5 batches and #4.

## Implemented shell navigation

`ShellView` owns the full-screen non-Home destination frame, not browsing data.
Sign-in opens media
Home automatically, initially focusing a card (or the sidebar Home if empty).
There is no intermediate preview launcher or redundant Home-screen back button.
Libraries opens incrementally loaded virtualized poster-grid browsing with
implemented filter, sort, and A–Z title controls. Search preserves one combined result grid, query, selected
item, and offsets; Downloads retains an honest unavailable state. Settings exposes
Language: English, Library layout, Exit, and Back to Home through a category/detail
split without implying unimplemented settings features. Initial focus is
Preferences. Up/down changes category, right enters its detail actions, and left
returns through the category to the rail. Button labels are centered with
consistent padding.
Minimal account/window controls are tracked separately in [#27](https://github.com/camcast3/cindara/issues/27);
expanded settings remain deferred.

The Home rail expands on focus or hover and collapses to original vector icons
when content is focused. Accept opens the focused destination. Non-Home Back
returns directly to the exact source action or library shortcut on Home without
reloading it. Exit closes the app; Back to Home restores media and focus.
The login screen offers only the English language selector alongside authentication.

`FocusNavigationService` scopes navigation to the active screen or dialog.
Explicit rail links take priority; other controls use transformed bounds,
aligned candidates, nearest directional edge, distance, and stable visual order.
Screen focus is remembered, and hidden, disabled, or removed controls recover to
the nearest available action. Initial focus waits for layout. Tab/Shift+Tab cycle
inside the current scope. Modal choices disable background interaction, trap
directional and keyboard focus, start on a safe action, and restore their launcher
on selection or Back. Account changes discard old screen focus.

The saved-account and language pickers use these primitives. Controller
Accept on an authentication text field opens a modal keyboard with letters, digits,
punctuation, Shift, Space, Backspace, Clear, Done, and Cancel. Physical typing and
paste still work in the field; passwords stay masked and drafts are discarded on
dismissal. Startup localization and pseudo-localization are available; full
international controller text entry and platform keyboard integration remain
future work. Physical Unicode typing and paste are retained.

The production frame and dialogs use live Avalonia logical pixels rather than
shrinking a fixed reference canvas. A physical 4K display at 200% DPI therefore
lays out as 1920x1080 logical pixels and is not scaled twice. Layout follows
viewport, text-scale, and DPI changes without cached physical dimensions. The
approved Home preview keeps its separate density policy below.

SDL input never transfers keyboard focus between controls when a controller
connects, disconnects, or becomes active. Fresh input selects the active device;
only its most recently held direction repeats (400 ms delay, 100 ms interval).
Accept/Back/Menu never repeat. Events are drained while inactive, and held
buttons/axes are suppressed until released/neutral after startup, hotplug, or
reactivation. Prompts use Xbox A/B, PlayStation Cross/Circle/Options, Nintendo
B/A/+, or generic South/East/Start labels. Accept always means physical South;
the Nintendo labels intentionally follow the physical layout rather than swapping
the navigation actions.

## Tokens and accessibility

`Styles/Tokens.axaml` is the source of truth. The base palette uses `#080B12`
behind raised `#131925` and `#1C2535` surfaces. Primary text (`#F5F8FC`) and
secondary text (`#B3BED0`) exceed WCAG AA against those surfaces; muted caption
text (`#8490A5`) also exceeds 4.5:1, including on the raised surface.
Teal `#64D8CB` identifies focus,
selection, and primary action. Error, warning, and success never rely on color
alone.

Inter is used at 48/32/24/18/14 logical pixels for display, title, heading,
body, and caption roles. Spacing follows a 4/8/16/24/40 scale. Corners use
8/14/24 radii. Every action has at least a 48 by 48 logical-pixel focus target
at 1080p. Focus uses a high-contrast three- or four-pixel outline plus
elevation; hover may raise the surface, pressed reduces emphasis, selected
keeps an outline (navigation uses a bottom border), disabled reduces opacity,
loading uses labeled skeletons, and errors add a labeled pink boundary.
Accent-filled primary actions use a dark inner focus border and light outer
ring so neither the button fill nor surrounding dark surface masks focus.

Standard motion is 200 ms, with 120 ms for direct feedback and 320 ms for
large context changes. Scale focused cards to at most 1.04 in production so
neighbors do not shift. When reduced motion is enabled, motion tokens become
zero, Fluent button press transforms are disabled, gallery scrolling is
immediate, and indeterminate busy animation is replaced by static status text.
Focus outlines remain instantaneous in every mode.

Developer-injected presentation preferences apply at window scope: text roles scale to 125% or
150%, high contrast replaces Cindara and Fluent control palettes, and default
restoration reverses every override. These appearance controls are deferred from
the user-facing UI; the app uses defaults and does not restore earlier appearance
settings. There is no OS preference auto-detection.
See [the accessibility contract and manual test matrix](accessibility.md) for
persistence, localization, pseudo-locales, contrast coverage, and limits.

## TV viewport behavior

The supported minimum window is **720x480 logical pixels**. Required actions and
messages scroll or wrap at that limit; the layout does not promise that an entire
page remains visible without scrolling. Production breakpoints are based on live
logical dimensions:

| Class | Rule | Safe margin | Content spacing |
| --- | --- | --- | --- |
| Compact | width below 960, or shortest side below 600 | 16 | 16 |
| Standard | width 960-1439 | 24 | 24 |
| Wide | width 1440-2559 | 32 | 32 |
| Ten-foot | width 2560 or greater | 48 | 40 |

Library and Search grids independently follow their available content width.
Poster width is computed from the actual destination width, remains at
least 120 logical pixels, and is capped at the approved couch-readable density;
its 2:3 ratio is preserved. Library metadata appends in bounded batches while
virtualized rows prevent the complete library from realizing controls at once.
Ten-foot typography and bounded forms use a capped 1.5 density scale. This is
separate from OS display scaling: a physical 4K display at 200% still supplies a
1920x1080 logical viewport and does not receive the density scale twice.
Resizing does not replace controls, so
keyboard/controller focus and scroll memory remain attached to the same item.
Compact footers stack status and actions, dialogs cap their scrollable body to
the current height, and long action groups wrap.

`ViewportProfile` defines the production reference: 1920x1080 logical pixels
with a 48-pixel safe margin. Its uniform scale is
`min(width / 1920, height / 1080)`: 0.667 at 1280x720 and 2 at 3840x2160.
This supplies reference safe areas and focus targets; it is not the density
policy used by the approved non-production gallery.

`GalleryViewportProfile` deliberately preserves the density approved on the
monitor and TV. Cards, their captions, and gutters use
`clamp(logicalWidth / 1600, 1, 1.55)` to avoid oversized posters. Hero text,
header text, and text spacing scale from the actual adaptive hero height:
`clamp(heroHeight / 518.4, 0.7, 2)`. Hero height is constrained by both
`clamp(logicalHeight * 0.48, 420, 1080)` and the space needed below it for a
complete poster. This preserves the approved 3440x1400 and 4K typography while
shrinking long-title layouts on short/compact windows instead of clipping their
subtitle and metadata. The 0.7 floor keeps small-window text legible; these
intentional caps mean cards and hero text do not scale uniformly with one another.
The same height-derived scale applies to all gallery text and icons: navigation
symbols and labels, the monogram, section headings, media labels, captions, and
empty-state copy. Navigation actions retain a 48-logical-pixel minimum target
while the rail itself ranges from 72 logical pixels on compact windows to 192
at the 4K logical profile. Artwork/card density continues to follow width so
posters are not made sparse merely because typography must shrink.

| Logical viewport | Card scale | Hero text scale | Hero height |
| --- | --- | --- | --- |
| 1280x720 | 1 | 0.7 | 299.2 |
| 1280x800 | 1 | 0.731 | 379.2 |
| 1920x1080 | 1.2 | 1 | 518.4 |
| 3440x1440 | 1.55 | 1.333 | 691.2 |
| 3840x2160 | 1.55 | 2 | 1036.8 |

All inputs are Avalonia logical dimensions. A physical 3840x2160 TV at 200%
OS scaling therefore uses the 1920x1080 row; the OS applies the remaining 2x,
not the gallery. Overscan-safe margins remain the production shell's target.

Poster cards use a 2:3 ratio; landscape cards use 16:9. Home rails reveal
additional cards through horizontal movement; Library and Search use vertically
scrolling multi-row grids. Hero artwork
carries a dark Cindara gradient so text
remains readable. Dialogs dim, but do not blur, the context. Toasts do not take
focus. Skeletons preserve final geometry and respect reduced motion.

Media cards are 20% wider and taller than the initial browsing slice. Before
viewport scaling, Home landscape cards are 348 x 195.6 and Home posters are
187.2 x 280.8 logical pixels. Library-grid posters are 247.2 x 372 logical
pixels in the existing shell coordinate system. Text sizes, spacing, and viewport
scaling remain unchanged. Short windows reduce the hero's height to leave room
for a full poster, its labels, and input hints; 1080p/ultrawide/4K hero geometry
is unchanged.

`DesignGalleryView.axaml` supplies the current authenticated Home preview with a
navigation rail, hero, landscape cards, and poster rails. The reusable
styles also define dialog, toast, empty-state actions, and loading skeleton
treatments; the wireframes below specify the remaining screens.

## Screen wireframes and focus graphs

Directional links below describe the target production shell. **A/Enter/Space** activates,
**B/Escape** returns or dismisses, and **Start/F11** toggles fullscreen. Mouse
click maps to activation, pointer hover maps to hover (not keyboard focus), and
wheel/trackpad scroll maps to rail or grid scrolling.
The current Home implements sidebar/rail directional navigation. Back/Escape
returns focus to its sidebar Home without leaving the signed-in experience.
Start/Options/+ and F11 toggle fullscreen. Modals consume controller Menu so it
cannot change the background; F11 remains the explicit keyboard escape path.
Appearance overrides are developer-tested foundation code, not current UI options.

### Home

```text
[Rail]  [Hero title / episode / facts / overview]
        [Continue watching  > > >]
        [Recently added     > > >]
```

Initial focus: first media card, or the sidebar Home action if there is no media.
Right from sidebar Home returns to the focused card. Within a rail, left/right moves cards;
up/down enters the first card of the adjacent rail. Up from the first row
returns to sidebar Home. The hero is informational, with no Play or More Info
buttons. Returning from production details will restore the originating card.

The authenticated preview uses one right-aligned hero image. Its width is 60%
of the content viewport, independent of hero height, so its left fade begins
near the midpoint on both 16:9 TVs and ultrawide monitors. Preserve the source
aspect ratio and top alignment; clip any excess height at the bottom. Apply
opacity masks relative to the artwork's visible bounds, not the entire window,
with fully transparent left and bottom edges over an opaque background. This
prevents seams and keeps scrolled cards from bleeding through the hero.

Preview hero text starts at the upper left without a top tab bar. Its scroll
viewport is a responsive left column: at most 48% of the post-navigation content
width and capped by the existing scaled 880-pixel reading measure. It remains
transparent so the artwork's opacity masks provide the transition instead of an
opaque rectangle. The hidden vertical scrollbar still supports wheel,
Page Up/Page Down, and controller scrolling without drawing over the artwork.
Title, subtitle, metadata, description, and their spacing scale together
from 1080p to 4K; long text wraps inside a scrollable hero rather than overlapping
the rails. Page Up/Page Down scroll the description. Poster and landscape cards have no border at rest.
The focused card retains one rounded accent outline for controller and keyboard
use; the framework's rectangular focus adorner is suppressed on media cards so
directional navigation does not add a second outline.

The rail viewport reserves measured trailing space below its final visible row
so that row can reach the same focused position beneath the hero as earlier
rows. Align the row heading below the hero, with the cards beneath it; never
align only the artwork and obscure the heading. Recompute trailing space after
layout or viewport changes, accounting for the heading, cards, captions, and
the existing bottom margin.
Controller and keyboard up/down navigation enters the first card of the adjacent
nonempty row and resets that row's horizontal offset. Returning to a previously
visited row also starts at its first card; left/right still traverses within
the current row, and a mouse click retains the specific card selected.
Row changes use the standard 200 ms cubic ease-out motion. New input redirects
the current transition rather than queuing moves. Scroll targets are measured
in content coordinates so partially completed movement cannot shift the final
heading position. Automatic vertical bring-into-view is suppressed to avoid a
snap before the transition; horizontal card visibility remains automatic.

The preview has a single Home navigation action in the sidebar. Settings opens
the compact settings panel; returning Home preserves the
loaded media and focused card. The top tab bar has been removed pending a
product decision about its purpose.
Continue Watching stays first and combines resume and next-up episodes into one
row, followed directly by the configured recently-added media rows. Library
entry points live only in the sidebar/chooser, never in a generic Home row.
Each series appears at most once: the most
recent resume episode below 90% watched wins, otherwise the next unplayed episode
in Jellyfin's order is shown. Movies at or above 90% watched are omitted.
This client-side display rule does not change server playback progress.
The row is ordered by each series' latest playback, newest first, with resumed
and next episodes mixed together. Movies use their own last-played timestamp;
missing timestamps sort last and ties retain server order.
Activity timestamps are batched from up to 200 recent episode records when
multiple series are candidates, with exact lookups only for missing series.
This avoids a request per series in the common case without dropping candidates
before the newest-first sort. A one-series load skips the extra batch request.
Recently-added rows use TV, Movies, Anime order. Anime libraries are distinguished by
their name because Jellyfin normally reports them as `tvshows`. Separate
libraries within each group retain the server's order and are never merged.

### Review status and preview limits

The product owner approved this design for review on 2026-09-21 after live
authenticated-media iteration on an ultrawide monitor and a physical 4K TV.
The preview launches fullscreen and uses logical viewport dimensions so Windows
DPI scaling is not applied twice. The production shell now relies on live logical
layout for display migration; its DPI-change behavior has headless coverage.
Startup uses the OS-selected display rather than persisting a display preference.

This gallery intentionally caps rows at 20 items and preloads a bounded subset
of recently-added backdrops, falling back to card artwork elsewhere. Library
destinations append metadata in 40-item batches without waiting for artwork or
preloading backdrops. Only nearby virtualized rows retain decoded posters. Six
background workers progressively fill the active artwork window, preserving focus.
Poster requests keep a 15-second timeout;
failure leaves the grid usable and offers an explicit artwork-only retry.
Playback and mutations are not implemented here. Metadata and artwork overlap under a six-request cap,
images are deduplicated within the request, and a 30-second deadline prevents
unbounded loading. Cancel loading/Back stops the request and enables Retry.
Loading Home initially focuses Cancel loading, and moving right from the Home
rail reaches it (left in the developer RTL layout). The loading focus state is
separate from ready/error Home so retrying restores the cancel action.
Session tokens are sent only in authenticated headers;
the preview transport requires HTTPS (or HTTP loopback) before sending any
request and rejects redirects rather than forwarding those headers.
Rejected preview sessions clear the active gallery, invalidate only the rejected
saved token, and return the account to sign-in. Other accounts and any replacement
token saved during the request are preserved; storage failures are surfaced.
Malformed or unsupported artwork reports an invalid-response error and releases
partially decoded images. Gallery replacement is transactional: a failed load
does not dispose the previous gallery before the replacement is ready. Ending
authentication, switching accounts/servers, or exiting the app disposes and
clears the old gallery; merely returning from the preview to the same account
retains it until replacement or authentication ends. The session-scoped in-memory
image LRU is capped at 128 entries / 32 MiB with a five-minute cache-wide expiry
window. A lookup after its deadline clears all entries and starts a new window,
so recently inserted images may expire sooner; there is no per-image minimum
lifetime. Per-image expiry is deferred to #10. Authentication boundaries also
discard the cache. HTTP responses are capped at 8 MiB.

### Login

```text
[Ambient Cindara background]
          Step 1: [Saved server / Add server]
          Step 2: [Saved account / Add account]
          Step 3: [Restore or username/password sign-in]
[Language]          [Progress]          [Diagnostics]
```

Each step has one primary decision. Saved sessions are grouped by canonical
server, then user. Selecting a saved user attempts protected restoration and
shows credentials only for a new or rejected session. Passwords remain ephemeral.

### Library

```text
[Back]  Library name
        [All / Unwatched / Favorites] [Title A–Z / Z–A] [Item range / total]
        [Poster] [Poster] [Poster] [Poster] ...
        [Poster] [Poster] [Poster] [Poster] ... [All / A–Z rail]
```

Initial focus: first poster, library choice if empty, Cancel while loading, or
Retry after a failed/canceled metadata request. Artwork loading does not disable
the grid or pagination; it has separate loading/cancel/retry controls.
Directional navigation follows the live responsive column count. Entering the
final loaded row requests the next 40 metadata items once and appends them in place.
The title rail filters to All or titles beginning with A–Z and resets loaded items.
Filter/sort changes commit only after a successful first batch. A failed incremental
batch preserves all posters and appends a focused Retry loading more tile. Lightweight
metadata is retained; decoded artwork outside nearby rows is disposed and can reload
through the session byte cache.
Selecting a card opens a read-only metadata summary; Back restores the exact
card, loaded batches, and scroll position. Returning from Home or Settings reuses
the grid.

### Details

```text
[Backdrop / title / facts]
[Play] [More] [Favorite]
[Seasons or related-content rail]
```

Initial focus: Play. Left/right traverses primary actions; down enters the
season selector or first related card. Up returns to actions. Back restores
the source screen and focus. Unavailable actions remain visible and disabled
with an explanation.

### Search

```text
[Back]  [Search field] [Controller keyboard]
        [Poster] [Poster] [Poster] [Poster] ...
        [Poster] [Poster] [Poster] [Poster] ...
        [Previous] [Item range / total] [Next]
```

Initial focus: search field. Controller Accept opens a temporary full-screen
keyboard overlay; Done restores the exact grid focus/offset and Back dismisses
the keyboard before leaving Search. Physical keyboard input remains direct.
Supported Jellyfin types share one combined poster grid with title/year; richer
metadata appears only after opening details. Empty, loading, canceled, retry,
paging, and expired-session states are explicit.

### Settings

```text
[Back]  Settings
        [Preferences]    [Language: English]
        [Library layout] [Configure sidebar / Home order]
        [Application]    [Exit / Back to Home]
        [Diagnostics]    [Open local diagnostics]
```

Initial focus: Preferences. Up/down changes category and its detail pane, right
enters the visible actions, and left returns through category to Back. The
language picker traps focus until selection or Back and currently offers English
only. Exit closes the app, while Back to Home restores media without a new load.
The category composition does not add new settings features.

Library layout opens a choice between Sidebar libraries and Home libraries.
Each editor has explicit Hide/Show and Move up/down actions for every available
server library, followed by Save layout and Cancel. Both orders are independent
and saved per account, server ID, and canonical server address on this device.
Defaults show TV, Movies, then Anime; saved empty lists remain empty and newly
discovered libraries stay hidden until selected. Changing a sidebar list does
not change Home, and vice versa. Cancel never applies the draft.

Both the Home and browsing sidebars expose the configured library shortcuts.
Only supported movie/TV library types are offered; Collections, People, and other
unsupported views are excluded from the chooser and both layout editors, even
when an older saved layout includes their IDs. Filtering uses Jellyfin's type,
not the displayed library name.
They scroll when needed. Home filters and orders its existing loaded rows without
re-fetching media; resources for hidden rows remain owned until the gallery is
disposed. Layout changes recover focus if the previously focused row was hidden.

### Playback overlay

```text
[Title and status]
[<<<<] [Play/Pause] [>>>>]       [Audio] [Subtitles] [Quality]
[================ timeline ================================]
```

Initial focus: Play/Pause when the overlay opens. Left/right follows visual
order; down reaches the timeline and up returns to the nearest control. Back
hides controls before leaving playback. Controls fade after four seconds of no
input, reappear on any input, and remain visible while focused, paused, or in
an error state. Reduced motion uses an immediate visibility change.

## Component contract

All interactive components expose rest, focused, hover, pressed, disabled,
loading, error, and selected states where meaningful. Poster and landscape
cards present artwork, title, metadata, progress, and badges in that order.
Rails own a heading, optional action, horizontal viewport, and end behavior.
Grids own spatial focus and restoration. Dialogs trap focus and require an
explicit primary action; toasts announce status without moving focus. Empty
states include a reason and next action. Skeletons are non-focusable and use
the final component's dimensions.

Keyboard focus must always be visible. Screen-reader names describe the action
and media title rather than artwork. The current application text scale supports
100/125/150%; 200% text remains a future layout target, distinct from OS DPI.
No essential status is communicated only through
motion, color, artwork, or sound.

## Shell validation

Headless Avalonia tests exercise actual controls and routed keyboard/mouse events,
including initial focus, deterministic spatial movement, removed/disabled controls,
saved-account selection, modal trapping/restoration, controller text entry, active
device prompts, inactive-window rejection, gallery round trips, and settings.
Layout tests cover 1920x1080 and 3840x2160, plus a 1x/2x DPI transition.

Run the affected surfaces with:

```shell
dotnet test tests/Cindara.Desktop.Tests --configuration Release --filter "FullyQualifiedName~Navigation|FullyQualifiedName~Input|FullyQualifiedName~DesignSystem|FullyQualifiedName~MainViewModel"
```

Setting `CINDARA_NAV_CAPTURE` to a local artifact directory also saves synthetic
1080p/4K shell, rail, settings, account, and keyboard/dialog renderings from the
UI tests. These contain no real accounts or media. Synthetic 1080p/4K and 200%-DPI
renderings were visually reviewed during #9 implementation. Rendered review
does not replace physical couch testing:
before release, exercise D-pad/stick hold, controller switching/unplug, alt-tab
with a held button, modal Back, text entry, and windowed/fullscreen transitions
on a real 1080p/4K display and Bazzite/Steam Game Mode.
