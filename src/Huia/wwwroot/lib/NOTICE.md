# Vendored third-party assets

These files are vendored (not fetched at build time) so Huia.UI's pages don't have a hard runtime
dependency on public CDNs; `_Layout.cshtml` falls back to the CDN only if a local file fails to load.

| File | Project | Version | License |
|---|---|---|---|
| `basecoat/basecoat.min.css`, `basecoat/basecoat.min.js` | [Basecoat UI](https://basecoatui.com/) ([source](https://github.com/hunvreus/basecoat)) | 1.0.2 | MIT |
| `lucide/lucide.min.js` | [Lucide](https://lucide.dev/) | 0.469.0 | ISC |
| `aspnet-validation/aspnet-validation.min.js` | [aspnet-client-validation](https://github.com/ryanelian/aspnet-validation) | 0.11.1 | MIT |
| `libphonenumber-js/libphonenumber-js.min.js` | [libphonenumber-js](https://gitlab.com/catamphetamine/libphonenumber-js) (min metadata bundle) | 1.13.11 | MIT |
| `flag-icons/css/flag-icons.min.css`, `flag-icons/flags/4x3/*.svg` | [flag-icons](https://flagicons.lipis.dev/) ([source](https://github.com/lipis/flag-icons)) — 4x3 SVG set only, the 1x1 square variant isn't used | 7.5.0 | MIT |

To update: re-download from the CDN URLs referenced as fallbacks in `_Layout.cshtml`, bump the version in
both places, and update this table.
