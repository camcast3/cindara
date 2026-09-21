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
Saved, TV, Movies, Anime, and More, plus Home/Trending/Activity/Profile header
buttons. This PR establishes their appearance and focus behavior, not the
destination screens. Production navigation and library/details/playback flows
remain assigned to #9, #3, #5, and #4.

## Tokens and accessibility

`Styles/Tokens.axaml` is the source of truth. The base palette uses `#080B12`
behind raised `#131925` and `#1C2535` surfaces. Primary text (`#F5F8FC`) and
secondary text (`#B3BED0`) exceed WCAG AA against those surfaces; muted text is
reserved for large, non-essential captions. Teal `#64D8CB` identifies focus,
selection, and primary action. Error, warning, and success never rely on color
alone.

Inter is used at 48/32/24/18/14 logical pixels for display, title, heading,
body, and caption roles. Spacing follows a 4/8/16/24/40 scale. Corners use
8/14/24 radii. Every action has at least a 48 by 48 logical-pixel focus target
at 1080p. Focus uses a high-contrast three- or four-pixel outline plus
elevation; hover may raise the surface, pressed reduces emphasis, selected
keeps a teal outline, disabled reduces opacity, loading uses skeletons, and
errors add a labeled pink boundary.

Standard motion is 200 ms, with 120 ms for direct feedback and 320 ms for
large context changes. Scale focused cards to at most 1.04 in production so
neighbors do not shift. When reduced motion is enabled, remove scale and
translation, shorten fades to 80 ms, and keep the focus outline instantaneous.

## TV viewport behavior

Design at 1920x1080 logical pixels with a 48-pixel safe margin on every edge.
At 3840x2160, scale dimensions and safe margins to 2x (96 pixels); do not fit
more content merely because physical pixels doubled. Other 16:9 sizes scale
from the 1080p reference. `ViewportProfile` provides the deterministic scale,
safe area, and focus-target calculation. Keep critical text and actions inside
the safe area to tolerate overscan.

Poster cards use a 2:3 ratio; landscape cards use 16:9. Home rails reveal part
of the next card as an affordance. Library grids maximize complete columns
inside the safe area. Hero artwork carries a dark Cindara gradient so text
remains readable. Dialogs dim, but do not blur, the context. Toasts do not take
focus. Skeletons preserve final geometry and respect reduced motion.

`DesignGalleryView.axaml` is a non-production authenticated showcase for the
navigation rail, header, hero, landscape cards, and poster rails. The reusable
styles also define dialog, toast, empty-state actions, and loading skeleton
treatments; the wireframes below specify the remaining screens.

## Screen wireframes and focus graphs

Directional links below describe the target production shell. **A/Enter/Space** activates,
**B/Escape** returns or dismisses, and **Start/F11** toggles fullscreen. Mouse
click maps to activation, pointer hover maps to hover (not keyboard focus), and
wheel/trackpad scroll maps to rail or grid scrolling.
The current preview implements header/rail directional navigation. Controller
Back closes the gallery without leaving fullscreen; outside the gallery it
provides a fullscreen escape. Start toggles fullscreen in either context.
The complete keyboard shortcuts, reduced-motion
settings, modal behavior, and production destinations are follow-up shell and
accessibility work.

### Home

```text
[Rail]  [Home] [Trending] [Activity] [Profile]
        [Hero title / episode / facts / overview]
        [Continue watching  > > >]
        [Recently added     > > >]
```

Initial focus: Home in the header. Left/right traverses the header; down enters
the first card of the active media row. Within a rail, left/right moves cards;
up/down enters the first card of the adjacent rail. Up from the first row
returns to the header. The hero is informational, with no Play or More Info
buttons. Returning from production details will restore the originating card.

The authenticated preview uses one right-aligned hero image. Its width is 60%
of the content viewport, independent of hero height, so its left fade begins
near the midpoint on both 16:9 TVs and ultrawide monitors. Preserve the source
aspect ratio and top alignment; clip any excess height at the bottom. Apply
opacity masks relative to the artwork's visible bounds, not the entire window,
with fully transparent left and bottom edges over an opaque background. This
prevents seams and keeps scrolled cards from bleeding through the hero.

Preview hero text starts at the upper left immediately below the Home/Trending
header. Title, subtitle, metadata, description, and their spacing scale together
from 1080p to 4K; long titles and descriptions truncate within the hero rather
than overlapping the rails. Poster and landscape cards have no border at rest.
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

The preview opens with focus on Home in the top bar. Left/right traverses
Home, Trending, Activity, and Profile; up from the first media row returns to
the last focused header button, and down returns to the first card of the
active row. Left from Home enters the sidebar; right from the sidebar's Home
icon returns to the header. Header destinations remain design placeholders.
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
DPI scaling is not applied twice. Moving the native window between mixed-DPI
screens during testing required a resize refresh; display migration and startup
screen selection remain part of the production shell work.

This gallery intentionally caps rows at 20 items and preloads a bounded subset
of recently-added backdrops, falling back to card artwork elsewhere. Header and
sidebar destinations, playback, mutations, paging, and production image caching
are not implemented here. Session tokens are sent only in authenticated headers;
the preview transport rejects redirects rather than forwarding those headers.
Rejected preview sessions clear the active gallery, invalidate only the rejected
saved token, and return the account to sign-in. Other accounts and any replacement
token saved during the request are preserved; storage failures are surfaced.

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
[Rail]  [Category list]  [Setting rows / value / toggle]
```

Initial focus: first category. Right enters its first setting; left returns to
the selected category. Up/down stays within a column. A modal choice traps
focus between options and its primary action until selection or Back.

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
and media title rather than artwork. Text supports 200% scaling without
clipping critical controls. No essential status is communicated only through
motion, color, artwork, or sound.
