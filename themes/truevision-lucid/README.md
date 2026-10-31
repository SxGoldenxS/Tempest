# TrueVision for Lucid

Gold controls, ivory text, teal accents and luminous analytical arrays, blended over Lucid's existing album-derived backgrounds. Keeps Lucid's layout and saved background settings. This supersedes the earlier red-orange Raphael palette; do not enable both overlays.

## Install

Keep Lucid enabled in dark mode. Run `./install.ps1` from this folder to install a local style-only extension and restart Spotify. Run `./install.ps1 -Uninstall` to disable it. The installer backs up configuration locally. Alternatively add `truevision.css` as a custom Marketplace snippet. Remove/disable the previous Raphael overlay first. Do not use the active Marketplace theme user.css: Marketplace removes that stylesheet when it loads Lucid.

Use Lucid's album/player image mode for track-derived backgrounds. The overlay does not change that setting. Array artwork uses 80% opacity. Album art remains unchanged; only the decorative background is blended. Reduced-motion preferences select the non-animated array; Lucid's own animation preferences remain separate.

## Attribution and privacy

Lucid is the separately installed layout/theme foundation: https://github.com/sanoojes/spicetify-lucid . Array geometry comes from the user-supplied TrueVision theme. This package contains only its decorative SVG backgrounds and the Spotify adaptation, not the Discord imports, account information or personal home icon. No remote assets or external requests are added. The local extension only inserts the bundled stylesheet; it does not read user data. Artwork is not covered by the neighbouring Raphael overlay's license.

Text contrast is checked against opaque palette surfaces; album imagery can affect translucent panel contrast. Compatibility depends on Lucid's colour tokens and Lucid main-view background surface.
