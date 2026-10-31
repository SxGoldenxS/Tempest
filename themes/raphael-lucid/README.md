# Raphael for Lucid

A red-orange palette overlay for [Lucid](https://github.com/sanoojes/spicetify-lucid), inspired by Raphael from That Time I Got Reincarnated as a Slime. Ember-orange controls, warm amber secondary accents, pale warm text, and charcoal/crimson surfaces.

## Install

1. Keep your existing Lucid theme installed and enabled. Select dark mode in Lucid.
2. Open Spicetify Marketplace, then Snippets, and create a custom snippet.
3. Name it **Raphael for Lucid**, paste the complete contents of `raphael.css` into its CSS field, and enable it.
4. If your current background uses album art, it will remain unchanged. For a fully warm appearance, use Lucid's own background settings to choose a solid background of `#100D0D`.

Disable or remove the snippet to restore Lucid's original colours. No theme reinstall or settings reset is required. Keep the snippet enabled to hold Raphael's colours when tracks change; it deliberately overrides Lucid's dynamic colour tokens.

## What stays yours

Your layout, spacing, player style, navigation, fonts, panel transparency, background image, animations, and saved Lucid settings stay unchanged. This is a colour overlay, not a replacement theme or a full settings import. Background artwork is not recoloured.

## Compatibility and validation

Uses Lucid's `--clr-*` colour tokens and comma-separated RGB companion tokens. Checked against upstream source at the revision in `UPSTREAM.txt`. Palette text/background pairs are checked for at least 4.5:1 contrast on opaque surfaces. Translucent panels over artwork can reduce contrast. Live Spotify appearance still needs visual verification; future Lucid changes may require token updates.

No remote resources, JavaScript, telemetry, personal data, or configuration exports are included. No Lucid source is bundled; Lucid remains separately installed and licensed by its authors. This independently authored overlay is provided under the MIT license in this directory.
