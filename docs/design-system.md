# Cindara ten-foot design system

Cindara uses a cinematic, calm interface optimized for a controller at TV
distance. Modern media apps are a usability benchmark, but Cindara's teal
accent, near-black layered surfaces, typography, copy, shapes, and artwork
treatment are original. Product UI must not reuse another product's assets,
branding, exact layout, or proprietary interaction.

## Information architecture

The persistent left rail exposes **Home**, **Libraries**, **Search**,
**Downloads**, and **Settings** in that order. It is collapsed to icons during
browsing and expands when focused. Downloads is a visible placeholder until
offline media ships so later work does not destabilize navigation order.
Details are entered from content rather than added to the rail. Playback is a
temporary full-screen layer.

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

`DesignGalleryView.axaml` is a non-production, compile-checked catalog for the
navigation rail, hero, landscape rail, poster grid, card states, dialog, toast,
empty state, and loading skeleton.

## Screen wireframes and focus graphs

Directional links below are deterministic. **A/Enter/Space** activates,
**B/Escape** returns or dismisses, and **Start/F11** toggles fullscreen. Mouse
click maps to activation, pointer hover maps to hover (not keyboard focus), and
wheel/trackpad scroll maps to rail or grid scrolling.

### Home

```text
[Rail]  [Hero title and Play]
        [Continue watching  > > >]
        [Recently added     > > >]
```

Initial focus: hero primary action. Left enters the selected rail item; down
enters the first card of the first rail. Within a rail, left/right moves cards;
up/down chooses the nearest card in the adjacent rail. Up from the hero stays
on the hero. Returning from details restores the originating card.

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
