# Third-party notices

These components retain their own licenses independently of Didanihaa's source-code license. Pinned versions are recorded in npm and NuGet lockfiles.

## Redistributed browser assets

| Component | License | Notice location |
| --- | --- | --- |
| [Vazirmatn](https://github.com/rastikerdar/vazirmatn) | SIL Open Font License 1.1 | `src/Didanihaa.Web/assets/fonts/OFL.txt` |
| [Lit, lit-element, lit-html, @lit/reactive-element](https://github.com/lit/lit) | BSD-3-Clause | Their package LICENSE files, copied into `web/third-party/` in a published build |
| [@lit-labs/ssr-dom-shim](https://github.com/lit/lit/tree/main/packages/labs/ssr-dom-shim) | BSD-3-Clause | Lit monorepo license, copied into `web/third-party/` |
| [Vite](https://github.com/vitejs/vite) | MIT and bundled third-party notices | Package LICENSE.md, copied into `web/third-party/` |

Vazirmatn copyright: **2015 The Vazirmatn Project Authors**. The font is preserved beside its full OFL license. The logo lockup embeds font data and remains subject to that font license as applicable.

Lit package notices identify the Lit/Google authors; the ssr-dom-shim entry point includes **Copyright 2019 Google LLC**. The build preserves upstream license texts with redistributed browser assets. These assets and their license files must stay together.

## .NET dependencies and runtime

The application uses Microsoft .NET, Microsoft.Data.Sqlite, Windows hosting support, YARP and their transitive dependencies. Test tooling includes Microsoft.NET.Test.Sdk and xUnit. NuGet package metadata and accompanying license notices remain authoritative for each version; see Directory.Packages.props and packages.lock.json files.

| Runtime component family | License / attribution |
| --- | --- |
| .NET, ASP.NET Core, Microsoft.Data.Sqlite, Windows hosting, YARP, System.IO.Hashing | MIT; .NET Foundation and Contributors / Microsoft Corporation |
| SQLitePCLRaw core, provider, config and bundle | Apache-2.0; Copyright 2014–2025 / 2014–2026 SourceGear, LLC, as recorded in the pinned packages |
| SQLite native library | Public domain; upstream statement preserved |

Full MIT, Apache-2.0 and SQLite statements are under `docs/licenses/`, copied to `third-party/` in the portable package. Build-Publish also preserves license and notice files shipped in the actual restored NuGet packages and runtime packs under `third-party/nuget/`. Do not remove those files. Recheck these attributions and license texts when updating dependencies.

## Optional external tools

FFprobe and FFmpeg are not included in the source repository or copied automatically into the application package. Their licensing depends on the supplied build and enabled components. Operators who redistribute those binaries must also distribute the corresponding upstream license information and satisfy the terms of that build. See [FFmpeg's license information](https://ffmpeg.org/legal.html).

## Build tools

TypeScript, Vite and their dependencies are obtained through npm; build and test tools retain their upstream licenses. Dependency directories are excluded from Git. A dependency audit is a point-in-time check and is not a license audit or a guarantee that every issue is known.
