# Fladder flow parity contract

Cindara independently implements the same desktop/TV user journeys as Fladder
while retaining Cindara branding, code, assets, accessibility rules, controller
behavior, diagnostics, and Windows recovery. "Parity" means equivalent screen
order, navigation hierarchy, primary actions, modal behavior, Back behavior, and
state restoration. It does not mean copying Fladder source, wording, artwork, or
exact visual composition.

The behavioral reference is DonutWare/Fladder `develop` at commit
`ac96cd5fef08c7fdacdafe9e011cf309a79a0611`. Re-audit this contract before a
parity release if the reference revision changes.

## Global navigation

Current navigation batch: the shared rail, Dashboard/Libraries stacks, nested
Search/Settings, detail/modal Back ordering, and a basic Libraries landing page
are implemented. This is not full-app parity: Favorites, Sync/offline, Discover,
the richer library overview, playback, and remaining authentication/settings
flows are still pending. Search currently has one account-scoped query/result set,
not independent query histories for each primary root.

- The authenticated app has stable primary roots for **Dashboard**,
  **Libraries**, **Favorites**, optional **Discover**, and **Sync**.
- Search, details, playback, settings, and administrative tools are secondary
  flows, not peer primary roots.
- Selecting an already-active primary destination returns to that destination's
  root rather than pushing a duplicate route.
- Back dismisses the deepest active state in this order: modal or sheet,
  selection/control mode, nested details/search, primary destination. Back from
  a non-Dashboard primary root returns to Dashboard.
- Every primary root owns an independent nested stack and focus memory. Player
  exit restores the invoking item and scroll position.
- Desktop/TV uses persistent side or top navigation. Cindara does not reproduce
  the mobile bottom-navigation layout.

## Startup and authentication

1. Splash restores the most recently active auto-login account.
2. No restorable account opens the saved-account chooser when accounts exist,
   otherwise server connection.
3. Server connection accepts a typed URL, saved server, discovered LAN server,
   or authentication link where supported.
4. A successful server probe loads server identity, public users, branding
   notice, and Quick Connect availability.
5. Authentication supports public-user selection, manual credentials, and Quick
   Connect with a refreshable code.
6. Successful authentication replaces the unauthenticated stack with Dashboard.
7. Switch user stops playback, clears user-scoped state, and returns to the
   account chooser. Logout confirms removal and returns to Login.
8. Local account locking may interrupt a restored session but cannot be bypassed
   with Back.

Cindara's Windows safe-start recovery is an additional failure-only startup path.
It must rejoin the same authentication or Dashboard flow after recovery.

## Dashboard

- Dashboard loads progressively and may contain a hero/carousel, Continue
  Watching, Continue Listening/Reading when supported, Next Up, Live TV, recently
  added library rows, and user-saved filter rows.
- Dashboard composition and ordering are user configurable.
- Opening a row heading enters a pre-filtered library search.
- Refresh reloads Dashboard without destroying the active account or primary
  navigation state.

## Libraries and search

1. Libraries opens a library landing page.
2. The user chooses a library from the library selector.
3. The landing page exposes recommendations, favorites, genres, and other
   supported sections.
4. Search or a section heading opens a scoped results route.
5. Search/results preserves query, filters, sort, view mode, selection mode, and
   scroll state.
6. Back exits selection mode before leaving results.
7. Pointer context menus and controller-accessible dialogs expose the same
   actions.

## Details

- A generic details route resolves to movie, series, season, or episode details.
- A failed item lookup leaves the dead route and returns to a usable parent.
- Movie Play resumes by default; restart and source selection are secondary.
- Series Play uses the computed next-up episode.
- Season details center the episode list/grid and retain parent-series access.
- Episode details retain parent-series access, chapters, and sibling episodes.
- Favorite, watched, sync, genre, cast, extras, related, recommendations, and
  source/track actions appear only when supported.
- Details refresh in place and preserve the originating browse state.

## Playback

1. Starting video presents a cancelable loading state.
2. Resume is the default when progress exists unless Restart is chosen.
3. Successful video playback enters a full-screen player.
4. Controls auto-hide and include play/pause, seek, previous/next, source quality,
   audio, subtitles, speed, queue, chapters, volume, fullscreen, and segment
   actions when available.
5. Back stops/closes playback and restores the invoking route.
6. Next Up appears near the end with a countdown and actions to play now, resume
   the current item, or close playback.
7. Player errors remain recoverable on the player surface; launch failures return
   to the invoking screen with a visible error.

## Sync and offline

- Sync is a primary destination when supported or local items exist.
- It shows root synced items, active download count, progress, and an empty state.
- Selecting a synced item opens a dialog with download, sync, pause, resume, stop,
  delete-files, delete-definition, and parent navigation actions as applicable.
- Destructive actions require confirmation.
- Offline is an app-wide state. Details, lists, seasons, episodes, and playback
  use local data when available rather than switching to a disconnected
  downloads-only app mode.
- Connectivity probes the configured server identity instead of trusting only the
  network-interface state.

## Settings

- Settings is a nested master/detail secondary flow.
- Top-level categories are Client, administrative Control panel when authorized,
  Profile, Player, and About.
- Single-pane layouts open category selection first; wide layouts use dual pane.
- Controller configuration remains under Client/Controls while preserving the
  same category-entry flow.
- HTPC mode exposes an explicit confirmed Exit action.

## Optional Discover

- Discover is a user-specific primary destination only when Seerr/Jellyseerr is
  configured.
- It exposes recent requests, trending, popular, expected, and upcoming sections.
- Search/details link to an existing Jellyfin item when possible; otherwise
  request flows validate seasons, permissions, quotas, and manager actions.
- Discover is unavailable or visibly inactive while offline.

## Controller and desktop additions

- Every Fladder-equivalent action must be reachable by D-pad/left stick, keyboard,
  and pointer.
- Initial focus, modal focus traps, focus restoration, and controller prompts are
  mandatory Cindara acceptance criteria even where Fladder's D-pad behavior is
  incomplete.
- Xbox, PlayStation, Nintendo, and generic profiles may remap actions and
  calibrate axes without changing the route flow.
- Pointer-only context actions require an equivalent controller-accessible menu.

## Parity verification

Each implemented flow requires automated route/state tests plus real-media review
for:

- initial focus and Back order;
- primary-root reselection;
- query/filter/selection/scroll restoration;
- account switching and account-state isolation;
- player entry and exact origin restoration;
- offline fallback and reconnect;
- compact, 1080p, 4K, ultrawide, and display-scaled layouts;
- mouse, keyboard, and remapped controller operation.
