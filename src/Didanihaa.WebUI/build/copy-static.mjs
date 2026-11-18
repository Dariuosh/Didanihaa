import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { stripTypeScriptTypes } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
// Vite emits the pages and bundled scripts/styles. Preserve font source files and licenses.
for (const directory of ['assets']) {
  await mkdir(path.join(root, 'dist', directory), { recursive: true });
  await cp(path.join(root, directory), path.join(root, 'dist', directory), {
    recursive: true,
    filter: source => !source.endsWith('.ts')
  });
}
await mkdir(path.join(root, 'dist', 'tv'), { recursive: true });
await mkdir(path.join(root, 'dist', 'shared'), { recursive: true });
await cp(path.join(root, 'tv', 'compatibility.js'), path.join(root, 'dist', 'tv', 'compatibility.js'));
for (const name of ['player-controls', 'simple-tv']) {
  const source = await readFile(path.join(root, 'shared', `${name}.ts`), 'utf8');
  // These two sources deliberately use ES5 syntax plus erasable types. No second compiler or runtime polyfill bundle.
  await writeFile(path.join(root, 'dist', 'shared', `${name}.js`), stripTypeScriptTypes(source, { mode: 'strip' }));
}
for (const name of ['player-controls', 'simple-tv']) await cp(path.join(root, 'shared', `${name}.css`), path.join(root, 'dist', 'shared', `${name}.css`));
// Keep notices with redistributed browser code, including Lit's transitive runtime packages.
const notices = path.join(root, 'dist', 'third-party');
await mkdir(notices, { recursive: true });
for (const [name, source] of [
  ['lit', 'lit/LICENSE'], ['lit-element', 'lit-element/LICENSE'], ['lit-html', 'lit-html/LICENSE'],
  ['@lit/reactive-element', '@lit/reactive-element/LICENSE'],
  ['@lit-labs/ssr-dom-shim', 'lit/LICENSE'], // This package uses the monorepo license and does not ship a separate file.
  ['vite', 'vite/LICENSE.md']
]) {
  await cp(path.join(root, 'node_modules', source), path.join(notices, `${name.replaceAll('/', '-')}.LICENSE`));
}
const shimLicense = path.join(notices, '@lit-labs-ssr-dom-shim.LICENSE');
await writeFile(shimLicense, 'Copyright 2019 Google LLC\n\n' + await readFile(shimLicense, 'utf8'));
console.log('Didanihaa web assets built in src/Didanihaa.WebUI/dist.');
