import '../shared/player-controls';
import { renderFolders } from '../shared/folders';
import { enableSpatialGrid, enableToolbar, focusTv, spatialTarget } from '../shared/spatial-grid';
import { loadHomeProfile, saveProfile, recordPlayback } from './profile';
import { renderNavigation } from './navigation';

const element = <T extends HTMLElement = HTMLElement>(id: string): T => document.getElementById(id) as T;
const fa = (value: number): string => value.toLocaleString('fa-IR');
const text = (tag: string, value: string, className = ''): HTMLElement => { const node = document.createElement(tag); node.textContent = value; node.className = className; return node; };
let sourceId: string | null = null; let folder = ''; let kind = ''; let favoritesView = false; let offset = 0; let total = 0; const limit = 20;
let playlistView: '' | 'playlists' | 'popular' = '';
let generation = 0; let playbackGeneration = 0; let lastFocus: HTMLElement | null = null;
let media: HTMLMediaElement | null = null; let disposeControls: (() => void) | null = null;
let listing: Didanihaa.FolderListing | null = null; let profile: Didanihaa.ProfileSession | null = null;
let currentItem: Didanihaa.PublicMedia | null = null; let queueBusy = false; let profileBusy = false; let nativeModal = false;
type Queue = { query: string; index: number; total: number; name: string };
let queue: Queue | null = null;
let renderedRoute = '';
let queuePage: Didanihaa.Page<Didanihaa.PublicMedia> | null = null;
let queuePageOffset = 0; let queuePageGeneration = 0; let lastFullscreenExit = 0;
const fullscreenDocument = document as Document & { webkitFullscreenElement?: Element; webkitExitFullscreen?: () => void };
function fullscreenElement(): Element | null { return document.fullscreenElement || fullscreenDocument.webkitFullscreenElement || null; }
function exitFullscreen(): void {
  if (document.exitFullscreen) void document.exitFullscreen().catch(() => {});
  else fullscreenDocument.webkitExitFullscreen?.();
}
type State = { tv: true; depth: number; focusKey?: string; scroll?: number; player?: Queue };
function state(): State { return history.state?.tv ? history.state as State : { tv: true, depth: 0 }; }
function parseRoute(): void {
  const query = new URLSearchParams(location.search); sourceId = query.get('sourceId'); folder = query.get('folder') || '';
  kind = ['video', 'audio'].includes(query.get('kind') || '') ? query.get('kind')! : ''; favoritesView = query.get('view') === 'favorites';
  playlistView = query.get('view') === 'popular' ? 'popular' : query.get('view') === 'playlists' ? 'playlists' : '';
  offset = Math.max(0, Number(query.get('offset')) || 0); element<HTMLInputElement>('search').value = query.get('search') || '';
}
function remember(): void {
  const focused = document.activeElement as HTMLElement;
  history.replaceState({ ...state(), focusKey: focused?.dataset.focusKey, scroll: window.scrollY }, '', location.href);
}
function restoreFocus(saved: State, first = false): void {
  const nodes = Array.from(document.querySelectorAll<HTMLElement>('[data-focus-key]'));
  const target = !first && saved.focusKey ? nodes.find(node => node.dataset.focusKey === saved.focusKey) : undefined;
  if (target) { target.focus({ preventScroll: true }); window.scrollTo(0, saved.scroll || 0); }
  else {
    const next = element('folder-grid').querySelector<HTMLElement>('a') || element('grid').querySelector<HTMLElement>('button') || element('collection-title');
    next.focus({ preventScroll: true }); if (first) window.scrollTo(0, 0);
  }
}
function go(query: URLSearchParams): void {
  remember(); history.pushState({ tv: true, depth: state().depth + 1 }, '', `${location.pathname}?${query}`);
  parseRoute(); element('search-form').hidden = true; void load().then(() => restoreFocus(state(), true));
}
function back(): void {
  if (state().depth > 0) { history.back(); return; }
  if (!sourceId) return;
  const query = new URLSearchParams(location.search); query.delete('offset');
  if (folder) query.set('folder', folder.split('/').slice(0, -1).join('/')); else { query.delete('sourceId'); query.delete('folder'); }
  history.replaceState({ tv: true, depth: 0 }, '', `${location.pathname}?${query}`); parseRoute(); void load().then(() => restoreFocus(state(), true));
}
async function get<T>(path: string): Promise<T> {
  const response = await fetch(path, { cache: 'no-store' }); if (!response.ok) throw new Error('کتابخانه در دسترس نیست. اتصال شبکه را بررسی و دوباره تلاش کن.');
  return response.json() as Promise<T>;
}
function showError(message: string): void { element('error-text').textContent = message; element('error').hidden = false; }
function updateProfile(): void {
  if (!profile) return;
  element('home-profile-name').textContent = profile.profile.name; element('favorites-count').textContent = profile.favoriteIds.length ? fa(profile.favoriteIds.length) : '';
  element('autoplay-next').setAttribute('aria-pressed', String(profile.profile.autoplay)); element('repeat-playlist').setAttribute('aria-pressed', String(profile.profile.repeatFolder));
  element('favorite-current').setAttribute('aria-pressed', String(!!currentItem && profile.favoriteIds.includes(currentItem.id)));
  element('favorite-current').textContent = currentItem && profile.favoriteIds.includes(currentItem.id) ? '♥ علاقه‌مند' : '♡ علاقه‌مندی';
  for (const id of ['favorite-current', 'autoplay-next', 'repeat-playlist']) element<HTMLButtonElement>(id).disabled = profileBusy || !currentItem;
}
function disposeMedia(): void {
  disposeControls?.(); disposeControls = null;
  if (media) { media.pause(); media.removeAttribute('src'); media.load(); media = null; }
}
function openOverlay(): void {
  const dialog = element<HTMLDialogElement>('player'); dialog.hidden = false;
  try {
    if (typeof dialog.showModal === 'function') {
      if (!dialog.open) dialog.showModal(); nativeModal = true; return;
    }
  } catch { /* Some embedded browsers expose an unusable dialog implementation. */ }
  {
    nativeModal = false; dialog.setAttribute('role', 'dialog'); dialog.setAttribute('aria-modal', 'true');
    for (const id of ['tv-main', 'tv-header', 'tv-rail', 'tv-footer']) element(id).hidden = true;
  }
}
function finishClose(): void {
  if (fullscreenElement()) exitFullscreen();
  playbackGeneration++; queuePageGeneration++; disposeMedia(); queue = null; currentItem = null; queueBusy = false; queuePage = null;
  element('queue-list').replaceChildren();
  element('player-shell').classList.remove('media-fullscreen', 'player-idle');
  const dialog = element<HTMLDialogElement>('player'); if (nativeModal && dialog.open) dialog.close(); dialog.hidden = true;
  if (!nativeModal) for (const id of ['tv-main', 'tv-header', 'tv-rail', 'tv-footer']) element(id).hidden = false;
  if (lastFocus && document.body.contains(lastFocus)) lastFocus.focus(); else restoreFocus(state());
  if (favoritesView) void load().then(() => restoreFocus(state()));
}
function requestClose(): void { if (state().player) history.back(); else finishClose(); }
function updateQueue(): void {
  element<HTMLButtonElement>('previous-track').disabled = queueBusy || !queue || (queue.index <= 0 && !profile?.profile.repeatFolder);
  element<HTMLButtonElement>('next-track').disabled = queueBusy || !queue || (queue.index >= queue.total - 1 && !profile?.profile.repeatFolder);
  element('queue-position').textContent = queue ? `${fa(queue.index + 1)} از ${fa(queue.total)} · ${queue.name}` : '';
  updateProfile(); enableToolbar(element('playlist-toolbar'));
  element('queue-count').textContent = queue ? `${fa(queue.total)} رسانه · ترتیب پوشه` : '';
  element('queue-list').querySelectorAll<HTMLButtonElement>('.queue-item').forEach(button => {
    const current = Number(button.dataset.index) === queue?.index;
    button.classList.toggle('is-current', current);
    if (current) button.setAttribute('aria-current', 'true'); else button.removeAttribute('aria-current');
  });
}
async function loadQueuePage(start: number): Promise<void> {
  const context = queue; if (!context) return;
  const request = ++queuePageGeneration; queuePageOffset = Math.max(0, start);
  const list = element('queue-list'); list.setAttribute('aria-busy', 'true'); element('queue-error').hidden = true;
  try {
    const query = new URLSearchParams(context.query); query.set('offset', String(queuePageOffset)); query.set('limit', '20');
    const page = await get<Didanihaa.Page<Didanihaa.PublicMedia>>(`/api/catalog?${query}`);
    if (request !== queuePageGeneration || context !== queue) return;
    queuePage = page; list.replaceChildren();
    page.items.forEach((item, index) => {
      const position = queuePageOffset + index; const button = document.createElement('button'); button.type = 'button'; button.className = 'queue-item'; button.dataset.index = String(position); button.setAttribute('aria-label', `پخش ${item.title}`);
      const number = text('span', fa(position + 1), 'queue-item-number'); number.setAttribute('aria-hidden', 'true');
      const cover = text('span', item.kind === 'video' ? '▷' : '♫', 'queue-item-cover'); cover.setAttribute('aria-hidden', 'true');
      if (item.thumbnailUrl) { const image = document.createElement('img'); image.src = item.thumbnailUrl; image.alt = ''; image.width = 120; image.height = 68; image.loading = 'lazy'; cover.append(image); }
      const title = text('span', '', 'queue-item-info'); title.append(text('strong', item.title), text('small', item.durationSeconds ? `${fa(Math.max(1, Math.round(item.durationSeconds / 60)))} دقیقه` : item.kind === 'video' ? 'ویدیو' : 'صوت'));
      button.append(number, cover, title); button.addEventListener('click', () => void loadTrack(position)); list.append(button);
    });
    enableSpatialGrid(list, 'قسمت‌های پوشهٔ در حال پخش', '.queue-item'); updateQueue();
    element('queue-pagination').hidden = page.total <= 20;
    element<HTMLButtonElement>('queue-next').disabled = queuePageOffset + 20 >= page.total;
    element<HTMLButtonElement>('queue-previous').disabled = queuePageOffset === 0;
    element('queue-page-label').textContent = `${fa(Math.floor(queuePageOffset / 20) + 1)} / ${fa(Math.ceil(page.total / 20))}`;
    enableToolbar(element('queue-pagination'));
  } catch (error) { if (request === queuePageGeneration) { element('queue-error').hidden = false; element('queue-error').textContent = (error as Error).message; } }
  finally { if (request === queuePageGeneration) list.setAttribute('aria-busy', 'false'); }
}
function play(item: Didanihaa.PublicMedia): void {
  disposeMedia(); currentItem = item; element('player-title').textContent = item.title;
  element('playback-error').hidden = true; element('queue-status').textContent = ''; openOverlay();
  const player = document.createElement(item.kind === 'audio' ? 'audio' : 'video'); player.controls = true; player.preload = 'metadata';
  let reported = false;
  const reportPlayback = (): void => {
    if (reported || media !== player) return;
    let played = 0; for (let index = 0; index < player.played.length; index++) played += player.played.end(index) - player.played.start(index);
    const threshold = Number.isFinite(player.duration) && player.duration > 0 ? Math.min(5, Math.max(.1, player.duration / 2)) : 5;
    if (played >= threshold) { reported = true; void recordPlayback(item.id).catch(() => {}); }
  };
  player.addEventListener('timeupdate', reportPlayback); player.addEventListener('ended', reportPlayback);
  player.src = item.streamUrl; player.setAttribute('playsinline', ''); player.setAttribute('aria-label', item.title);
  if (item.kind === 'video') { player.setAttribute('width', '1280'); player.setAttribute('height', '720'); if (item.thumbnailUrl) (player as HTMLVideoElement).poster = item.thumbnailUrl; }
  player.addEventListener('error', () => { if (media === player) { element('playback-error').textContent = 'این فرمت اصلی در مرورگر دستگاه پخش نشد. می‌توانی مورد بعدی یا کنترل بومی را امتحان کنی.'; element('playback-error').hidden = false; } });
  player.addEventListener('ended', () => {
    if (media !== player) return;
    if (profile?.profile.autoplay && queue && (queue.index < queue.total - 1 || profile.profile.repeatFolder)) void moveTrack(1);
    else element('queue-status').textContent = queue && queue.index === queue.total - 1 ? 'به پایان این پلی‌لیست رسیدی.' : 'پخش تمام شد. برای ادامه، بعدی را انتخاب کن.';
  });
  const container = element('player-media'); container.replaceChildren();
  if (item.kind === 'audio') container.append(text('div', '♫', 'audio-cover'));
  container.append(player); media = player;
  try { disposeControls = window.DidanihaaPlayer?.attach(player, container) || null; } catch { player.controls = true; }
  const transport = container.querySelector<HTMLElement>('[role=toolbar]'); if (transport) enableToolbar(transport);
  const attempt = player.play(); if (attempt && typeof attempt.catch === 'function') attempt.catch(() => { if (media === player) element('queue-status').textContent = 'برای شروع، دکمهٔ پخش را بزن.'; });
  updateQueue();
  if (document.activeElement === element('close-player')) container.querySelector<HTMLElement>('[data-player-surface]')?.focus();
}
function startQueue(context: Queue, trigger: HTMLElement): void {
  remember(); lastFocus = trigger; queue = context; queuePage = null;
  history.pushState({ ...state(), depth: state().depth + 1, player: { ...context } }, '', location.href);
  openOverlay(); element('close-player').focus(); void loadTrack(context.index); void loadQueuePage(Math.floor(context.index / 20) * 20);
}
async function loadTrack(index: number): Promise<void> {
  const context = queue; if (!context) return;
  const request = ++playbackGeneration; queueBusy = true; updateQueue(); element('queue-status').textContent = 'در حال آماده‌سازی…';
  try {
    const query = new URLSearchParams(context.query); query.set('offset', String(index)); query.set('limit', '1');
    const page = await get<Didanihaa.Page<Didanihaa.PublicMedia>>(`/api/catalog?${query}`);
    if (request !== playbackGeneration || queue !== context) return;
    if (!page.items.length) throw new Error('این مورد دیگر در دسترس نیست. کتابخانه را تازه‌سازی کن.');
    context.index = index; context.total = page.total; history.replaceState({ ...state(), player: { ...context } }, '', location.href); play(page.items[0]);
    if (queuePage && (index < queuePageOffset || index >= queuePageOffset + queuePage.items.length)) void loadQueuePage(Math.floor(index / 20) * 20);
  } catch (error) {
    if (request === playbackGeneration) { element('playback-error').textContent = (error as Error).message; element('playback-error').hidden = false; element('queue-status').textContent = ''; }
  } finally {
    if (request === playbackGeneration) { queueBusy = false; updateQueue(); if ((document.activeElement as HTMLButtonElement)?.disabled) element('player-media').querySelector<HTMLElement>('[data-player-play]')?.focus(); }
  }
}
async function moveTrack(delta: number): Promise<void> {
  if (!queue || queueBusy) return; let index = queue.index + delta;
  if (index < 0 || index >= queue.total) { if (!profile?.profile.repeatFolder) return; index = (index + queue.total) % queue.total; }
  await loadTrack(index);
}
function catalogQuery(): URLSearchParams {
  const query = new URLSearchParams({ search: element<HTMLInputElement>('search').value, kind });
  if (favoritesView) query.set('ids', (profile?.favoriteIds || []).join(','));
  if (sourceId) { query.set('sourceId', sourceId); query.set('folder', folder); }
  return query;
}
function choose(item: Didanihaa.PublicMedia, index: number, trigger: HTMLElement): void {
  if (favoritesView) {
    void get<Didanihaa.MediaLocation>(`/api/location/${item.id}`).then(location => startQueue({ query: new URLSearchParams({ sourceId: location.sourceId, folder: location.folder }).toString(), index: location.index, total: location.total, name: location.name }, trigger)).catch(error => showError(error.message));
  } else startQueue({ query: catalogQuery().toString(), index, total, name: folder.split('/').pop() || listing?.sourceName || 'پلی‌لیست' }, trigger);
}
function navigateFolder(source: string | null, path: string): void {
  const query = new URLSearchParams(location.search); query.delete('offset'); query.delete('sourceId'); query.delete('folder');
  if (playlistView) { query.delete('view'); query.delete('search'); }
  if (source) { query.set('sourceId', source); query.set('folder', path); } go(query);
}
async function load(): Promise<void> {
  const request = ++generation; element('error').hidden = true; element('collection').setAttribute('aria-busy', 'true');
  try {
    const home = await loadHomeProfile(); if (request !== generation) return; profile = home; updateProfile();
    const query = catalogQuery(); query.set('offset', String(offset)); query.set('limit', String(limit));
    const read = async (): Promise<[Didanihaa.FolderListing, Didanihaa.Page<Didanihaa.PublicMedia>]> => {
      if (playlistView) {
        const parameters = new URLSearchParams({ kind, sort: playlistView === 'popular' ? 'popular' : 'name', offset: String(offset), limit: String(limit), search: query.get('search') || '' });
        const playlists = await get<Didanihaa.Page<Didanihaa.PublicPlaylist>>(`/api/playlists?${parameters}`);
        return [{ sourceId: null, path: '', sourceName: null, folders: playlists.items.map(item => ({ sourceId: item.sourceId, path: item.folder, name: item.name, totalFiles: item.mediaCount, directFiles: item.mediaCount, publishedFiles: item.mediaCount })), directFiles: 0, directAvailableFiles: 0, directPublishedFiles: 0 }, { items: [], total: playlists.total, offset, limit }];
      }
      return Promise.all([
        favoritesView ? Promise.resolve({ sourceId, path: folder, sourceName: sourceId ? 'پوشهٔ علاقه‌مندی‌ها' : null, folders: [], directFiles: 0, directAvailableFiles: 0, directPublishedFiles: 0 }) : get<Didanihaa.FolderListing>(`/api/viewer-folders?${query}`),
        sourceId || favoritesView ? get<Didanihaa.Page<Didanihaa.PublicMedia>>(`/api/catalog?${query}`) : Promise.resolve({ items: [], total: 0, offset: 0, limit })
      ]);
    };
    const [[folders, page]] = await Promise.all([read(), renderNavigation(home, { kind, favorites: favoritesView, sourceId, folder }, go)]);
    if (request !== generation) return;
    renderedRoute = location.pathname + location.search;
    listing = folders; total = page.total; renderFolders(folders, navigateFolder, true); enableSpatialGrid(element('folder-grid'), 'پوشه‌های قابل تماشا');
    const title = playlistView === 'popular' ? 'پربازدیدها' : playlistView === 'playlists' ? 'پلی‌لیست‌های پوشه‌ای' : favoritesView ? 'علاقه‌مندی‌ها' : sourceId ? folder.split('/').pop() || folders.sourceName || 'پلی‌لیست' : kind === 'video' ? 'ویدیوها' : kind === 'audio' ? 'صداها' : 'کتابخانه';
    element('collection-title').textContent = title; document.title = `${title} · دیدنی‌ها`;
    element<HTMLInputElement>('search').placeholder = playlistView ? 'نام پوشه…' : 'نام رسانه…';
    element('collection-description').textContent = playlistView === 'popular' ? 'پوشه‌ها بر اساس پخش‌های واقعی همین خانه؛ بدون آمار ساختگی.' : playlistView ? 'پوشه‌های منتشرشده؛ هر پوشه یک پلی‌لیست مستقل.' : favoritesView ? `انتخاب‌های مشترک ${home.profile.name}` : sourceId ? 'فایل‌های همین پوشه؛ به ترتیب نام و شماره.' : 'پوشه‌ای را برای تماشا یا شنیدن انتخاب کن.';
    element('count').textContent = playlistView ? `${fa(total)} پلی‌لیست` : sourceId || favoritesView ? `${fa(total)} رسانه` : `${fa(folders.folders.length)} پوشه`;
    element<HTMLButtonElement>('play-folder').hidden = !sourceId || !total || favoritesView;
    element<HTMLButtonElement>('go-back').disabled = !sourceId && state().depth === 0;
    element('go-back').setAttribute('aria-label', `بازگشت به ${folder ? folder.split('/').slice(0, -1).pop() || folders.sourceName || 'کتابخانه' : 'کتابخانه'}`);
    document.querySelectorAll<HTMLElement>('[data-category]').forEach(node => {
      const current = node.dataset.category === (favoritesView ? 'favorites' : kind || 'all');
      if (current) node.setAttribute('aria-current', 'page'); else node.removeAttribute('aria-current');
    });
    const grid = element('grid'); grid.replaceChildren();
    page.items.forEach((item, index) => {
      const card = document.createElement('button'); card.type = 'button'; card.className = 'media-card'; card.dataset.focusKey = `media:${item.id}`; card.setAttribute('aria-label', `پخش ${item.title}`);
      const art = text('div', '', `card-art cover-${index % 4}`); art.append(text('span', item.kind === 'audio' ? '♫' : '▷', 'art-symbol'));
      if (item.thumbnailUrl) { const image = document.createElement('img'); image.src = item.thumbnailUrl; image.loading = 'lazy'; image.alt = ''; image.width = 640; image.height = 360; art.append(image); }
      art.append(text('span', item.kind === 'audio' ? 'صوت' : 'ویدیو', 'card-kind'), text('span', '▷', 'card-play'));
      const metadata = text('div', '', 'card-meta'); metadata.append(text('span', favoritesView ? '♡' : `${fa(offset + index + 1)} / ${fa(page.total)}`), text('span', item.durationSeconds === null ? 'زمان نامشخص' : item.durationSeconds < 60 ? 'کمتر از یک دقیقه' : `${fa(Math.round(item.durationSeconds / 60))} دقیقه`));
      card.append(art, text('h2', item.title, 'card-title'), metadata); card.addEventListener('click', () => choose(item, offset + index, card)); grid.append(card);
    });
    enableSpatialGrid(grid, 'رسانه‌ها'); element('empty').hidden = page.items.length > 0 || folders.folders.length > 0;
    element('empty-title').textContent = playlistView === 'popular' ? 'هنوز آمار پخشی ثبت نشده است.' : favoritesView ? 'هنوز چیزی را نشان نکرده‌ایم.' : 'در این بخش رسانه‌ای پیدا نشد.';
    element('empty-description').textContent = playlistView === 'popular' ? 'با تماشا و شنیدن، پوشه‌های پرپخش اینجا نمایان می‌شوند.' : favoritesView ? 'هنگام پخش، دکمهٔ علاقه‌مندی را بزن؛ این انتخاب روی همهٔ دستگاه‌ها دیده می‌شود.' : 'جست‌وجو را تغییر بده یا رسانه‌های این بخش را در ادمین منتشر کن.';
    element<HTMLButtonElement>('previous').disabled = offset === 0; element<HTMLButtonElement>('next').disabled = offset + limit >= total;
    element('pagination').hidden = total <= limit; element('page-label').textContent = total ? `${fa(Math.floor(offset / limit) + 1)} / ${fa(Math.ceil(total / limit))}` : '';
    ['library-toolbar', 'pagination'].forEach(id => enableToolbar(element(id))); enableToolbar(document.querySelector<HTMLElement>('.tv-navigation')!);
  } catch (error) { if (request === generation) showError((error as Error).message); }
  finally { if (request === generation) element('collection').setAttribute('aria-busy', 'false'); }
}
document.querySelectorAll<HTMLAnchorElement>('[data-category]').forEach(link => link.addEventListener('click', event => {
  if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
  event.preventDefault(); go(new URL(link.href).searchParams);
}));
element('go-back').addEventListener('click', back);
element('focus-search').addEventListener('click', () => { element('search-form').hidden = false; element('search').focus(); });
element('search-form').addEventListener('submit', event => { event.preventDefault(); const query = new URLSearchParams(location.search); query.set('search', element<HTMLInputElement>('search').value); query.delete('offset'); go(query); });
element('clear-search').addEventListener('click', () => { const query = new URLSearchParams(location.search); query.delete('search'); query.delete('offset'); go(query); });
element('previous').addEventListener('click', () => { const query = new URLSearchParams(location.search); query.set('offset', String(Math.max(0, offset - limit))); go(query); });
element('next').addEventListener('click', () => { const query = new URLSearchParams(location.search); query.set('offset', String(offset + limit)); go(query); });
element('play-folder').addEventListener('click', () => startQueue({ query: catalogQuery().toString(), index: 0, total, name: folder.split('/').pop() || listing?.sourceName || 'پلی‌لیست' }, element('play-folder')));
element('retry').addEventListener('click', () => void load()); element('close-player').addEventListener('click', requestClose);
element('player').addEventListener('cancel', event => { event.preventDefault(); if (fullscreenElement()) { exitFullscreen(); return; } if (Date.now() - lastFullscreenExit > 350) requestClose(); });
element('queue-next').addEventListener('click', () => void loadQueuePage(queuePageOffset + 20));
element('queue-previous').addEventListener('click', () => void loadQueuePage(Math.max(0, queuePageOffset - 20)));
for (const event of ['fullscreenchange', 'webkitfullscreenchange']) document.addEventListener(event, () => { if (!fullscreenElement()) lastFullscreenExit = Date.now(); });
element('previous-track').addEventListener('click', () => void moveTrack(-1)); element('next-track').addEventListener('click', () => void moveTrack(1));
element('favorite-current').addEventListener('click', () => {
  if (!currentItem || !profile || profileBusy) return; profileBusy = true; updateProfile();
  void saveProfile('favorites', { mediaId: currentItem.id, favorite: !profile.favoriteIds.includes(currentItem.id) }).then(value => { profile = value; }).catch(error => { element('queue-status').textContent = error.message; }).finally(() => { profileBusy = false; updateProfile(); });
});
for (const id of ['autoplay-next', 'repeat-playlist']) element(id).addEventListener('click', () => {
  if (!profile || profileBusy) return; profileBusy = true; updateProfile();
  void saveProfile('preferences', { autoplay: id === 'autoplay-next' ? !profile.profile.autoplay : profile.profile.autoplay, repeatFolder: id === 'repeat-playlist' ? !profile.profile.repeatFolder : profile.profile.repeatFolder })
    .then(value => { profile = value; }).catch(error => { element('queue-status').textContent = error.message; }).finally(() => { profileBusy = false; updateQueue(); });
});
window.addEventListener('popstate', () => {
  if (state().player) { queue = { ...state().player! }; queuePage = null; openOverlay(); element('close-player').focus(); void loadTrack(queue.index); void loadQueuePage(Math.floor(queue.index / 20) * 20); return; }
  const hadPlayer = !element('player').hidden;
  if (hadPlayer) { finishClose(); if (renderedRoute === location.pathname + location.search) return; }
  parseRoute(); void load().then(() => restoreFocus(state()));
});
document.addEventListener('keydown', event => {
  const code = event.keyCode; const key = event.key && event.key !== 'Unidentified' ? event.key : ({ 37: 'ArrowLeft', 38: 'ArrowUp', 39: 'ArrowRight', 40: 'ArrowDown' } as Record<number, string>)[code];
  const isBack = key === 'Escape' || code === 10009 || code === 461 || (key === 'Backspace' && !(event.target instanceof HTMLInputElement));
  if (isBack) {
    if (fullscreenElement()) { event.preventDefault(); exitFullscreen(); return; }
    if (Date.now() - lastFullscreenExit < 350) { event.preventDefault(); return; }
    if (!element('player').hidden) { event.preventDefault(); requestClose(); } else if (sourceId || state().depth > 0) { event.preventDefault(); back(); } return;
  }
  if (!element('player').hidden) {
    if (key === 'MediaTrackNext') { event.preventDefault(); void moveTrack(1); }
    if (key === 'MediaTrackPrevious') { event.preventDefault(); void moveTrack(-1); }
    if (key === 'MediaPlayPause' || code === 10252) { event.preventDefault(); if (media?.paused) void media.play().catch(() => {}); else media?.pause(); }
    if (key === 'Tab' && !nativeModal) {
      const nodes = Array.from(element('player').querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),[controls]')).filter(node => node.getClientRects().length > 0);
      const index = nodes.indexOf(document.activeElement as HTMLElement);
      if (event.shiftKey && index <= 0) { event.preventDefault(); nodes[nodes.length - 1]?.focus(); } else if (!event.shiftKey && index === nodes.length - 1) { event.preventDefault(); nodes[0]?.focus(); }
    }
  }
  if (event.defaultPrevented || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(key)) return;
  const active = (event.composedPath ? event.composedPath()[0] : event.target) as HTMLElement;
  if (!(active instanceof HTMLElement) || active.matches('input,textarea,select,[contenteditable]') || !active.closest('[data-tv-zone]')) return;
  const root = element('player').hidden ? document : element('player');
  const nodes = Array.from(root.querySelectorAll<HTMLElement>('[data-tv-zone] button:not(:disabled),[data-tv-zone] a[href]')).filter(node => node.getClientRects().length > 0 && !node.closest('[hidden]'));
  const next = spatialTarget(active, nodes, key); if (next) { event.preventDefault(); focusTv(next); }
});
parseRoute(); history.replaceState({ ...state(), player: undefined }, '', location.href);
void load().then(() => { if (document.activeElement === document.body) restoreFocus(state()); });
