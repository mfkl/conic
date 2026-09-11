# Settings implementation — 2026-09-10

Implemented [Settings in the shared shell](SETTINGS_SCREEN_SPEC.md), specification
commit `fc39deb`. Continued implementation and commits are authorized; user
visual acceptance remains pending.

Preferences is now a full page matching Home: black header/category navigation,
charcoal content, grouped settings, orange selection and focus, and a compact
category selector. SettingsView owns the layout and history confirmations;
MainWindow retains window routing and the attached video surface. Existing
settings/history storage and shortcut commands are preserved.

Search with no matches hides stale category content and shows an empty state.
Closing cancels pending confirmations and restores route focus. Reset all now
refreshes visible shortcut values. Shortcut write/reset failures preserve pending
edits and show a persistent inline error. Audio, Subtitles and Advanced link to
the existing media controls/information drawer. The ineffective theme selector
and disabled snapshot button are removed; capability limits use plain language.
Fluent input accents now use VLC orange, including Home's search field.

Windows validation:

- Release build passed with no warnings/errors in the final incremental build.
- All 165 managed tests passed, including search/no results, reset refresh and
  failed shortcut writes. Existing persistence, migration, corruption, privacy,
  startup options and shortcut normalization/conflict tests passed.
- `--settings-smoke` passed for all eight categories at 1280x720 and 480x360,
  including editor containment, search/empty states, compact reset confirmation,
  Home focus, Media information routing and Escape.
- Home/session smoke proved that Settings preserves the engine reference,
  generation and advancing playback position. PiP native smoke, Playback
  interaction smoke and the opening/drawer/settings layout smoke also passed.

Local captures: `artifacts/settings-review/settings-<category>-wide.png` and
`settings-<category>-compact.png`, plus `settings-no-results.png` and
`settings-reset-confirmation.png`. The local contact sheet is
`artifacts/settings-review/index.html`. Logs are under
`artifacts/playback-validation/settings-*.log`; tests/build logs are
`artifacts/settings-tests.log` and `artifacts/settings-build.log`.
`--settings-review` launches an interactive Preferences page.

Limits: the page explains current unavailable video adjustments and detailed
codec streams; it does not add native bindings. Physical display-scale coverage,
macOS and Linux remain separate qualification work. The final Windows integration
matrix follows this independently validated screen commit.
