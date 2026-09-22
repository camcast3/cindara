# Cindara ten-foot design system

Cindara uses a cinematic, calm interface optimized for a controller at TV
distance. Modern media apps are a usability benchmark, but Cindara's teal
accent, near-black layered surfaces, typography, copy, shapes, and artwork
treatment are original. Product UI must not reuse another product's assets,
branding, exact layout, or proprietary interaction.

## Information architecture

The production information architecture exposes **Home**, **Libraries**, **Search**,
**Downloads**, and **Settings** in that order. It is collapsed to icons during
browsing and expands when focused. Downloads is a visible placeholder until
offline media ships so later work does not destabilize navigation order.
Details are entered from content rather than added to the rail. Playback is a
temporary full-screen layer.

The approved authenticated preview uses a compact icon rail with Search, Home,
Saved, TV, Movies, Anime, and Settings. The product owner removed the speculative
Home/Trending/Activity/Profile top bar. Home focuses the media content and Settings
opens the signed-in settings; other preview icons remain placeholders.
The production shell implements the five destinations above; library content,
details, and playback flows remain assigned to #3, #5, and #4.

## Implemented shell navigation

`ShellView` owns the production frame, not browsing data. Sign-in opens media
Home automatically, initially focusing a card (or the sidebar Home if empty).
There is no intermediate preview launcher or redundant Home-screen back button. Libraries,
Search, and Downloads show honest unavailable-content states and retain rail
focus. Settings exposes exactly Language: English, Exit, and Back to Home;
initial focus is Language. Up/down traverses the three actions, and left
returns to the rail. Button labels are centered with consistent padding.
Entering Settings from another screen resets focus to Language; moving between
its actions, language dialog, and rail preserves focus within the same visit.
Expanded settings are deferred to [#27](https://github.com/camcast3/cindara/issues/27).

The rail expands on focus or hover and collapses to original vector icons when
content is focused. Up/down follows Home, Libraries, Search, Downloads, Settings
without wrapping. Accept opens the focused destination. Right returns to remembered
content in the current destination; left or Back from content enters its selected
rail item. Back from the settings rail returns to media Home without reloading it.
Exit closes the app; Back to Home restores media and focus without reloading.
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

The production frame and dialogs fit a 1920x1080 reference surface uniformly,
including the existing 48-pixel safe-area token. At 3840x2160 logical pixels they
scale 2x; a physical 4K display at 200% DPI uses 1920x1080 logical pixels and is not
scaled twice. Layout follows viewport and DPI changes without cached physical
dimensions. The approved preview keeps its separate density policy below.

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

`ViewportProfile` defines the production reference: 1920x1080 logical pixels
with a 48-pixel safe margin. Its uniform scale is
`min(width / 1920, height / 1080)`: 0.667 at 1280x720 and 2 at 3840x2160.
This supplies reference safe areas and focus targets; it is not the density
policy used by the approved non-production gallery.

`GalleryViewportProfile` deliberately preserves the density approved on the
monitor and TV. Cards, their captions, and gutters use
`clamp(logicalWidth / 1600, 1, 1.55)` to avoid oversized posters. Hero text,
header text, and text spacing use `clamp(ViewportProfile.Scale, 1, 2)` for
couch readability. Hero height is `clamp(logicalHeight * 0.48, 420, 1080)`.
The minimum scale keeps small-window text legible; these intentional caps mean
cards and hero text do not scale uniformly with one another.

| Logical viewport | Card scale | Hero text scale | Hero height |
| --- | --- | --- | --- |
| 1280x720 | 1 | 1 | 420 |
| 1920x1080 | 1.2 | 1 | 518.4 |
| 3440x1440 | 1.55 | 1.333 | 691.2 |
| 3840x2160 | 1.55 | 2 | 1036.8 |

All inputs are Avalonia logical dimensions. A physical 3840x2160 TV at 200%
OS scaling therefore uses the 1920x1080 row; the OS applies the remaining 2x,
not the gallery. Overscan-safe margins remain the production shell's target.

Poster cards use a 2:3 ratio; landscape cards use 16:9. Home rails reveal part
of the next card as an affordance. Library grids maximize complete columns
inside the safe area. Hero artwork carries a dark Cindara gradient so text
remains readable. Dialogs dim, but do not blur, the context. Toasts do not take
focus. Skeletons preserve final geometry and respect reduced motion.

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

Preview hero text starts at the upper left without a top tab bar.
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
the three-action settings panel; returning Home preserves the
loaded media and focused card. The top tab bar has been removed pending a
product decision about its purpose.
TV uses a screen-and-stand glyph; Anime has its own torii gate glyph, accessible
name, and tooltip rather than sharing the TV icon.
Both the sidebar's library icons and recently-added rows use TV, Movies, Anime
order; Continue Watching stays first. Anime libraries are distinguished by
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
of recently-added backdrops, falling back to card artwork elsewhere. Full
library destinations, playback, mutations, paging, and production image caching
are not implemented here. Metadata and artwork overlap under a six-request cap,
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
retains it until replacement or authentication ends.

### Library

```text
[Rail]  [Library title] [Filter] [Sort]
        [Poster] [Poster] [Poster] [Poster]
        [Poster] [Poster] [Poster] [Poster]
```

Initial focus: first poster, or Filter when the grid is empty. The grid moves
spatially; up from row one reaches Filter, then Sort to its right. Left from
column one enters Libraries in the rail. Paging preserves the nearest column.

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
[Rail]  [Search field] [Clear]
        [Suggested/result grid]
```

Initial focus: search field. Down enters the first result; right reaches Clear
when text exists. Controller text entry invokes the platform keyboard. Results
use library-grid navigation. An empty query shows suggestions; no results shows
an explanatory empty state and returns up to the field.

### Settings

```text
[Rail]  Settings
        [Language: English]
        [Exit]
        [Back to Home]
```

Initial focus: Language. Up/down moves between the three actions; left returns
to the rail. The language picker traps focus until selection or Back and currently
offers English only. Exit closes the app, while Back to Home restores media
without a new load. No category navigation or expanded settings are exposed yet.

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
