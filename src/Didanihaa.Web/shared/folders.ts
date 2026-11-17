export type NavigateFolder = (sourceId: string | null, path: string) => void;

export function renderFolders(listing: Didanihaa.FolderListing, navigate: NavigateFolder, publicView = false): void {
  const nav = document.getElementById('folder-breadcrumbs')!;
  const trail = document.createElement('ol');
  const steps: { label: string; source: string | null; path: string }[] = [{ label: 'کتابخانه', source: null, path: '' }];
  if (listing.sourceId) {
    steps.push({ label: listing.sourceName || 'پوشه', source: listing.sourceId, path: '' });
    const parts = listing.path.split('/').filter(Boolean);
    parts.forEach((part, index) => steps.push({ label: part, source: listing.sourceId, path: parts.slice(0, index + 1).join('/') }));
  }
  steps.forEach((step, index) => {
    const li = document.createElement('li'); const link = document.createElement('a');
    const query = new URLSearchParams(location.search); query.delete('offset'); query.delete('sourceId'); query.delete('folder');
    if (['popular', 'playlists'].includes(query.get('view') || '')) { query.delete('view'); query.delete('search'); }
    if (step.source) { query.set('sourceId', step.source); query.set('folder', step.path); }
    link.href = `${location.pathname}?${query}${location.pathname.startsWith('/admin') ? '#catalog' : ''}`; link.textContent = step.label;
    if (index === steps.length - 1) link.setAttribute('aria-current', 'page');
    link.addEventListener('click', event => { if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return; event.preventDefault(); navigate(step.source, step.path); });
    li.append(link); trail.append(li);
  });
  nav.replaceChildren(trail);
  const grid = document.getElementById('folder-grid')!; grid.replaceChildren();
  listing.folders.forEach((folder, index) => {
    const card = document.createElement('a'); card.className = 'folder-card';
    const query = new URLSearchParams(location.search); query.set('sourceId', folder.sourceId); query.set('folder', folder.path); query.delete('offset');
    if (['popular', 'playlists'].includes(query.get('view') || '')) { query.delete('view'); query.delete('search'); }
    card.href = `${location.pathname}?${query}${location.pathname.startsWith('/admin') ? '#catalog' : ''}`;
    card.dataset.sourceId = folder.sourceId; card.dataset.folder = folder.path; card.style.setProperty('--cover-index', String(index % 4));
    const cover = document.createElement('span'); cover.className = `folder-cover cover-${index % 4}`; cover.setAttribute('aria-hidden', 'true');
    const icon = document.createElement('span'); icon.className = 'folder-glyph'; icon.textContent = '▱';
    const corner = document.createElement('span'); corner.className = 'folder-corner'; corner.textContent = '↗'; cover.append(icon, corner);
    const name = document.createElement('strong'); name.textContent = folder.name;
    const count = document.createElement('span'); count.className = 'folder-count';
    count.textContent = publicView ? `${folder.totalFiles.toLocaleString('fa-IR')} منتشرشده برای تماشا` : `${folder.totalFiles.toLocaleString('fa-IR')} شناسایی‌شده · ${folder.publishedFiles.toLocaleString('fa-IR')} در TV`;
    card.dataset.focusKey = `folder:${folder.sourceId}:${folder.path}`;
    card.append(cover, name, count); card.addEventListener('click', event => { if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return; event.preventDefault(); navigate(folder.sourceId, folder.path); }); grid.append(card);
  });
  grid.hidden = listing.folders.length === 0;
}
