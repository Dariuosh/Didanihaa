import { defineConfig, type ProxyOptions } from 'vite';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const projectRoot = fileURLToPath(new URL('.', import.meta.url));
const localConfig = new URL('../../didanihaa.json', import.meta.url);
const configuration = JSON.parse(readFileSync(existsSync(localConfig) ? localConfig : new URL('../../didanihaa.example.json', import.meta.url), 'utf8'));
const gatewayOrigin = `http://localhost:${configuration.publicPort}`;
const apiProxy: ProxyOptions = {
  target: gatewayOrigin, changeOrigin: true,
  configure: proxy => {
    proxy.on('proxyReq', (request, incoming) => {
      // Translate only this loopback preview's own Origin; the gateway rejects all others.
      const original = incoming.headers.origin;
      if (typeof original === 'string') {
        try {
          const candidate = new URL(original); const expected = new URL(`http://${incoming.headers.host}`);
          if (candidate.origin !== 'null' && candidate.origin === expected.origin) request.setHeader('Origin', gatewayOrigin);
        } catch { /* Keep invalid origins unchanged. */ }
      }
    });
  }
};

export default defineConfig({
  plugins: [{ name: 'didanihaa-tv-compatibility', transformIndexHtml: { order: 'post', handler: html => html.replace('<!--TV_COMPATIBILITY-->', '<script src="/tv/compatibility.js"></script>') } }],
  root: projectRoot,
  publicDir: false,
  server: {
    host: '127.0.0.1', port: 5211, strictPort: true,
    watch: { ignored: ['**/bin/**', '**/obj/**', '**/dist/**'] },
    proxy: {
      '/tv/simple': { target: gatewayOrigin, changeOrigin: true },
      '/shared/player-controls.js': { target: gatewayOrigin, changeOrigin: true },
      '/shared/simple-tv.js': { target: gatewayOrigin, changeOrigin: true },
      '/api': apiProxy,
      '/media': { target: gatewayOrigin, changeOrigin: true },
      '/thumbnails': { target: gatewayOrigin, changeOrigin: true },
      '/health': { target: gatewayOrigin, changeOrigin: true },
      '/admin/api': apiProxy
    }
  },
  build: {
    target: 'es2017', outDir: 'dist', emptyOutDir: true, modulePreload: false,
    rollupOptions: {
      input: {
        admin: fileURLToPath(new URL('admin/index.html', import.meta.url)),
        tv: fileURLToPath(new URL('tv/index.html', import.meta.url))
      }
    }
  }
});
