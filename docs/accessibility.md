# Accessibility and localization foundation

This is a foundation for the current authentication, navigation shell, settings,
dialogs, and authenticated design preview—not a claim of WCAG conformance or
screen-reader certification. Playback, production browsing, and further
translations must retain these contracts as they are implemented.

## Current settings surface

The login screen and **Settings** expose **Language: English**, with
English as the only supported choice. The selected language is exposed in
visible text and automation state. The signed-in Settings screen contains only
**Language: English**, **Library layout**, **Exit**, and **Back to Home**, with consistently centered
button labels. Account, display, input, and appearance settings are deferred to
[the expanded settings feature request](https://github.com/camcast3/cindara/issues/27).
Library layout uses separate focus-trapped sidebar/Home editors. Each library
has an explicit Hide/Show action. Shown/Hidden states are exposed as text and
automation item status; each library's Move
up/down buttons have distinct accessible names. Reordering preserves logical
focus on the moved library, and Save/Cancel returns to the Settings launcher.
There is no intermediate Home/preview launcher or top Home/Trending/Activity/Profile
bar. Back/Escape in media Home moves focus to its single Home navigation action.
The login/shell footer also offers **Diagnostics**, separate from Settings actions.
Its focus-trapped dialogs support controller/keyboard review of paged support
files, explicit export, and cancellation; see [the support workflow](diagnostics.md).

The product owner deferred appearance controls from the UI. The underlying
presentation primitives remain available for developer/headless coverage only:

| Preference | Default | Supported overrides |
| --- | --- | --- |
| Text size | 100% | 125%, 150% |
| High contrast | Off | Black surfaces, white text/boundaries, yellow actions/focus |
| Reduced motion | Off | Immediate scrolling/state changes; no button press scaling or indeterminate busy animation |

The normal app always starts with the defaults above. It neither reads nor writes
the earlier `accessibility.json`, so hidden appearance overrides cannot remain
active after their UI was removed. The persistence helper is retained and tested
as foundation code, not wired into the current application.

These are **application overrides**. The app does not currently detect OS
high-contrast, reduced-motion, or text-size preferences. It does respect the
existing logical-pixel/DPI layout policy. Text size is a separate multiplier,
not a second DPI scale. The maximum application text setting is currently 150%;
200% application text support remains a future layout target, not a tested claim.

### Implementation contract

`Cindara.Desktop.Accessibility.PresentationPreferences` is an immutable record:
`new PresentationPreferences(TextScale: 1.25, HighContrast: true,
ReducedMotion: true)`. Both construction and `with` expressions reject text
scales other than `1`, `1.25`, and `1.5`.
`PresentationSettingsStore(path).Load()` / `.Save(preferences)` leave JSON and
storage failures explicit. `PresentationTheme.Apply(window, preferences)` is
called on the UI thread. It updates window-scoped brushes, colors, typography,
motion resources, Fluent control resources, and `large-text`, `high-contrast`,
and `reduced-motion` classes. Applying defaults restores all three modes.
Unrelated windows and application resources are not mutated.

Do not assume changing a color resource updates an app-scoped brush: the theme
also supplies window-scoped brushes. Do not assume zero-duration Cindara tokens
stop every framework animation: Fluent press transforms are separately disabled,
and busy UI must stop its indeterminate progress animation and retain localized
busy text. Future animations must explicitly participate in reduced motion.

The normal palette's lowest text contrast is the caption color on the raised
surface (approximately **4.77:1**). Every tested text-role/surface combination,
including status text, passes **4.5:1**; large text consequently also passes
**3:1**. Ordinary focus outlines exceed **3:1** on supported surfaces.
Primary buttons use a dark inner focus boundary against their accent fill plus
a light outer boundary against the surrounding dark surface. A pale teal
outline against a teal button does not provide sufficient contrast.

Navigation selection has a bottom border, and cards retain their selected
outline. Errors must include explanatory text; loading must include a textual
status. Automation states and names supplement these visual indicators.
Controls must remain identifiable in grayscale.
High-contrast disabled buttons use 65% opacity rather than looking identical to
enabled actions. The composited text remains above 4.5:1 against the black
surfaces; re-enabling restores full opacity.

## Locale and pseudo-locales

`Loc.Get(key)` currently resolves the English invariant catalog only;
unknown keys remain visible as `[key]` for diagnosis. `Loc.Format(key, args)`
preserves culture-aware composite formatting. Use `LocaleFormat` for metadata
instead of concatenating translated fragments. Server-provided names and media
titles are content, not resource keys.

The startup `CINDARA_CULTURE` environment override accepts .NET culture names.
Without an override, formatting uses the current process/OS culture. UI language
and flow remain English/LTR regardless of regional culture; only the explicit
developer RTL pseudo-locale changes flow. Translation resources are not shipped
until another language is supported. Culture selection happens at startup.

```powershell
$env:CINDARA_CULTURE = 'fr-FR'
dotnet run --project src\Cindara.Desktop

$env:CINDARA_CULTURE = 'qps-ploc'
dotnet run --project src\Cindara.Desktop

$env:CINDARA_CULTURE = 'qps-plocm'
dotnet run --project src\Cindara.Desktop
Remove-Item Env:CINDARA_CULTURE
```

For a pseudo-localized build without setting an environment variable:

```powershell
dotnet build src\Cindara.Desktop --configuration Release -p:CindaraPseudoLocale=qps-ploc
dotnet src\Cindara.Desktop\bin\Release\net10.0\Cindara.Desktop.dll
```

Use `qps-plocm` instead for RTL. A startup `CINDARA_CULTURE` override takes
precedence over the build setting. Run the built DLL as shown: `dotnet run`
without the same build property can rebuild and remove the embedded pseudo-locale.

`qps-ploc` accents and expands local resource strings while preserving format
placeholders. `qps-plocm` also exercises right-to-left flow and bidirectional
text. Neither is a human translation or proof of Arabic/Hebrew usability.
Keep URLs and other direction-sensitive input readable; never reverse typed
values or server identifiers. Layout uses wrapping action labels and scrollable
content rather than assuming English-length text. In the design preview,
Page Up/Page Down scroll the full hero description; high contrast hides the
decorative backdrop rather than placing text over uncontrolled artwork.
Physical text entry/paste remains important: the controller keyboard is not a
full international IME.

## Validation

Automated coverage includes preference validation, persistence and failure
paths, window isolation, restoring defaults, live font scaling and action-label
wrapping, Fluent motion overrides, non-color selection, and WCAG contrast
calculations against actual headless control brushes and focus boundaries.

```powershell
dotnet test tests\Cindara.Desktop.Tests --configuration Release --filter "FullyQualifiedName~Accessibility|FullyQualifiedName~Localization|FullyQualifiedName~Navigation|FullyQualifiedName~CardFocusStyleTests"
```

Headless automation cannot establish platform screen-reader behavior, physical
TV readability, or the quality of an actual translation. The following manual
matrix is a **release checklist**, not a record of completed platform testing:

| Configuration | Required checks |
| --- | --- |
| Windows keyboard + Narrator or NVDA | Authentication labels and password masking; button names/values; focus order; dialogs announced, trapped, dismissible, and focus restored; errors/busy state announced without losing focus |
| Linux desktop + Orca (supported Avalonia accessibility backend) | Repeat keyboard/announcement checks; record backend/version and any unsupported automation behavior rather than assuming Windows parity |
| macOS keyboard + VoiceOver | Repeat names, roles, values, focus restoration, text entry, and status-announcement checks |
| 1080p and 4K TV, including 200% OS DPI | Read at couch distance; no double DPI scaling; text at 100/125/150%; no clipped critical controls; scroll actions into view |
| Keyboard only | Tab/Shift+Tab and arrows; Enter/Space; Escape/Back; F11; English language selection; saved-account and load-cancel/retry recovery; physical Unicode input/paste |
| Developer-injected presentation modes | Every page and dialog; focus visible on dark and accent fills; grayscale selection; immediate gallery scrolling; stationary busy indication |
| Regional formatting, expanded pseudo, RTL pseudo at 150% | Long action names/metadata; wrapping and scrollability; mixed-direction URLs; logical reading order; no critical controls outside the focus scope |
| Controller plus keyboard | Language choice preserves focus; Back restores launcher; controller disconnect leaves keyboard usable |

Record the tested OS, assistive-technology version, Avalonia backend, locale,
display scale, and preferences with any defect. Do not mark screen-reader
validation complete merely because `AutomationProperties.Name` exists.

## Subtitle accessibility requirements for playback

These are future acceptance requirements, not implemented subtitle features:

- Distinguish subtitles, closed captions/SDH, forced tracks, language, and
  default/off state in both visible labels and accessible names. Preserve
  meaningful server labels and distinguish duplicate-language tracks.
- Expose subtitle choices before playback and during playback using keyboard,
  controller, and screen reader; preserve the focused choice and announce the
  active track without relying on color.
- Provide legible text size, foreground/background, opacity, outline, and
  placement controls where the renderer supports them. Protect captions from
  overscan and playback-overlay obstruction. Honor saved accessibility
  preferences without unexpectedly overriding an explicit track choice.
- Explain unavailable text styling for bitmap or burned-in subtitles. Do not
  promise that a player can restyle burned-in video. Surface rendering and
  track-selection failures with a recovery action.
- Keep captions synchronized across seeks, resume, speed changes, and source
  changes. Validate Unicode/bidirectional captions and overlapping speakers;
  test forced-only and SDH behavior with real media.
- Preserve semantic sound descriptions and speaker identification in captions.
  UI reduced motion must not suppress captions or alter their timing.

Track this against [#19: scoped audio/subtitle preference rules](https://github.com/camcast3/cindara/issues/19),
[#20: pre-play quality/audio/subtitle selector](https://github.com/camcast3/cindara/issues/20),
[#4: couch playback controls and Jellyfin session reporting](https://github.com/camcast3/cindara/issues/4),
and the playback implementation in #6/#8.

These links follow the actual issue titles. Roadmap #16 currently labels #17/#18
as playback work and #19/#20 as Seerr work, but the linked issues themselves are
[#17: Seerr authentication](https://github.com/camcast3/cindara/issues/17) and
[#18: Seerr requests](https://github.com/camcast3/cindara/issues/18). Resolve that
roadmap numbering mismatch before scheduling those milestones; do not redirect
subtitle accessibility work to the Seerr issues.
