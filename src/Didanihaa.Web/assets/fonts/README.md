# Vazirmatn

The variable WOFF2 font supplied for this project is packaged here with its
SIL Open Font License in `OFL.txt`. Keep both files when distributing CaptureDesk.

- Project: https://github.com/rastikerdar/vazirmatn
- Google Fonts specimen: https://fonts.google.com/specimen/Vazirmatn
- Font file: `Vazirmatn[wght].woff2` (weight range 100–900)

The side panel loads this bundled asset through a path relative to its stylesheet.
There is no absolute computer path, installed-font dependency, Google Fonts CSS
import or runtime CDN request. Moving the complete extension directory or packaging
it preserves the font. The font directory must be included in the package and in
version control. Extension-owned pages can load it without exposing the font to
websites through `web_accessible_resources`.

`font-display: swap` keeps text readable while the font loads; system fonts remain
fallbacks if the bundled asset is unavailable.
