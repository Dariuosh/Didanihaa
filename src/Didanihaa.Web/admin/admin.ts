import './components';
import { showAdminPage } from './navigation';
import { renderFolders } from '../shared/folders';

(() => {
  const element = <T extends HTMLElement = HTMLElement>(id: string): T => document.getElementById(id) as T;
  const text = (tag: string, content: string, className = ''): HTMLElement => {
    const node = document.createElement(tag); node.textContent = content; node.className = className; return node;
  };
  const fa = (value: number): string => value.toLocaleString('fa-IR');
  const size = (bytes: number): string => bytes >= 1073741824 ? `${(bytes / 1073741824).toFixed(1)} GB` : `${(bytes / 1048576).toFixed(1)} MB`;
  let session: Didanihaa.Session;
  let sources: Didanihaa.Source[] = [];
  let status: Didanihaa.Status | null = null;
  let page: Didanihaa.Page<Didanihaa.Candidate> = { items: [], total: 0, offset: 0, limit: 30 };
  let offset = 0; let generation = 0; let polling = false; let serviceBusy = false;
  let jobSignature = ''; let toastTimer = 0;
  let publicationBusy = false;
  let currentFolder = new URLSearchParams(location.search).get('folder') || '';
  let listing: Didanihaa.FolderListing | null = null;
  let sourcesRenderKey = '';
  let profileFormDirty = false;
  let profileFavoritesRenderKey = '';
  const selected = new Set<string>();
  const serviceLabels: Record<string, string> = { 'not-installed': 'هنوز در ویندوز نصب نشده', stopped: 'متوقف', running: 'در حال اجرا', starting: 'در حال راه‌اندازی', stopping: 'در حال توقف', paused: 'مکث‌شده', 'access-denied': 'دسترسی به وضعیت سرویس ممکن نیست', unsupported: 'این سیستم‌عامل ویندوز نیست' };
  const jobLabels: Record<string, string> = { queued: 'در صف اسکن', running: 'در حال اسکن', completed: 'اسکن کامل شد', 'completed-with-errors': 'اسکن با چند خطا پایان یافت', failed: 'اسکن ناموفق', interrupted: 'اسکن قطع شده' };

  async function api<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetch(path.replace(/^\/api\//, '/admin/api/'), body === undefined ? { cache: 'no-store' } : {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Didanihaa-Token': session.token }, body: JSON.stringify(body)
    });
    const value = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(value.error || value.detail || `درخواست انجام نشد (${response.status})`);
    return value as T;
  }
  function notify(message: string, error = false): void {
    window.clearTimeout(toastTimer);
    const node = element('message'); node.className = error ? 'notice error' : 'notice'; node.hidden = false;
    element('message-text').textContent = message;
    if (!error) toastTimer = window.setTimeout(() => { node.hidden = true; }, 7000);
  }
  function selectionChanged(): void {
    element('selection-count').textContent = `${fa(selected.size)} مورد انتخاب‌شده`;
    element<HTMLButtonElement>('publish').disabled = selected.size === 0 || publicationBusy;
    element<HTMLButtonElement>('unpublish').disabled = selected.size === 0 || publicationBusy;
    element('clear-selection').hidden = selected.size === 0;
    const visible = page.items.filter(item => selected.has(item.id)).length;
    element('selection-context').textContent = selected.size ? `${fa(visible)} مورد در این صفحه، ${fa(selected.size - visible)} مورد در صفحات دیگر · سقف هر عملیات ۱۰۰ رسانه` : 'انتخاب همه فقط موارد این صفحه را شامل می‌شود.';
    const available = page.items.filter(item => item.available);
    const all = element<HTMLInputElement>('select-all');
    all.checked = available.length > 0 && available.every(item => selected.has(item.id));
    all.indeterminate = !all.checked && available.some(item => selected.has(item.id));
    element<HTMLButtonElement>('publish-folder').disabled = publicationBusy || !listing?.directAvailableFiles;
    element<HTMLButtonElement>('unpublish-folder').disabled = publicationBusy || !listing?.directPublishedFiles;
  }
  function renderMedia(): void {
    const focusedRow = document.activeElement instanceof HTMLInputElement && element('media-list').contains(document.activeElement)
      ? document.activeElement.closest<HTMLTableRowElement>('tr')?.dataset.mediaId : null;
    const body = element('media-list'); body.replaceChildren();
    for (const item of page.items) {
      const row = document.createElement('tr'); row.dataset.mediaId = item.id;
      const checkCell = document.createElement('td'); const checkbox = document.createElement('input');
      checkbox.type = 'checkbox'; checkbox.checked = selected.has(item.id); checkbox.disabled = !item.available && !item.published;
      checkbox.setAttribute('aria-label', `انتخاب ${item.title}`);
      checkbox.addEventListener('change', () => {
        if (checkbox.checked && selected.size >= 100) { checkbox.checked = false; notify('در هر نوبت حداکثر ۱۰۰ رسانه انتخاب کن.'); }
        else if (checkbox.checked) selected.add(item.id); else selected.delete(item.id);
        selectionChanged();
      });
      checkCell.append(checkbox); row.append(checkCell);
      const titleCell = document.createElement('th'); titleCell.scope = 'row'; const title = text('div', '', 'media-title');
      const icon = text('span', item.kind === 'audio' ? '♫' : '▷', 'media-icon');
      if (item.kind === 'video') {
        const preview = document.createElement('img'); preview.src = `/admin/api/thumbnails/${item.id}`; preview.alt = ''; preview.loading = 'lazy'; preview.width = 72; preview.height = 44; icon.replaceChildren(preview);
      }
      title.append(icon);
      const titleText = document.createElement('div'); titleText.append(text('div', item.title, 'media-name'), text('div', item.relativePath, 'media-path mono'));
      title.append(titleText); titleCell.append(title); row.append(titleCell);
      const typeCell = document.createElement('td'); typeCell.dataset.label = 'نوع / حجم'; typeCell.append(text('div', item.kind === 'audio' ? 'صوت' : 'ویدیو'), text('div', size(item.sizeBytes), 'media-details mono')); row.append(typeCell);
      const metadataCell = document.createElement('td');
      metadataCell.dataset.label = 'مدت / کیفیت';
      if (item.metadata.durationSeconds !== null) metadataCell.append(text('div', item.metadata.durationSeconds < 60 ? 'کمتر از یک دقیقه' : `${fa(Math.round(item.metadata.durationSeconds / 60))} دقیقه`));
      const streams = item.metadata.streams.slice(0, 2).map(stream => [stream.codec, stream.width && stream.height ? `${stream.width}×${stream.height}` : '', stream.language].filter(Boolean).join(' · ')).join(' / ');
      metadataCell.append(text('div', streams || (item.metadata.status === 'basic' ? 'مشخصات پایه' : 'خطای خواندن مشخصات'), 'media-details'));
      if (item.metadata.error) metadataCell.title = item.metadata.error;
      row.append(metadataCell);
      const stateCell = document.createElement('td'); stateCell.dataset.label = 'وضعیت'; stateCell.append(text('span', !item.available ? 'خارج از دسترس' : item.published ? 'منتشرشده' : 'نیازمند انتخاب', `badge ${!item.available ? 'bad' : item.published ? 'good' : 'warn'}`)); row.append(stateCell);
      body.append(row);
    }
    element('catalog-empty').hidden = page.items.length > 0;
    element('media-table-region').hidden = page.items.length === 0;
    const filtered = filtersActive();
    element('empty-title').textContent = filtered ? 'رسانه‌ای با این جست‌وجو و فیلترها پیدا نشد.' : 'کتابخانه از انتخاب‌های تو شروع می‌شود.';
    element('empty-description').textContent = filtered ? 'عبارت جست‌وجو را کوتاه‌تر کن یا فیلترها را بردار.' : 'یک پوشه اضافه کن و بعد از اسکن، فایل‌های دلخواه را منتشر کن.';
    element('empty-reset').hidden = !filtered;
    element('clear-filters').hidden = !filtered;
    const countText = `${fa(page.total)} رسانه${filtered ? ' در این نتیجه' : ''}`;
    if (element('catalog-count').textContent !== countText) element('catalog-count').textContent = countText;
    element('page-label').textContent = page.total ? `${fa(page.offset + 1)} تا ${fa(page.offset + page.items.length)} از ${fa(page.total)}` : 'بدون رسانه';
    element<HTMLButtonElement>('previous').disabled = offset === 0;
    element<HTMLButtonElement>('next').disabled = offset + page.limit >= page.total;
    selectionChanged();
    if (focusedRow) element('media-list').querySelector<HTMLInputElement>(`tr[data-media-id="${focusedRow}"] input`)?.focus({ preventScroll: true });
  }
  function filtersActive(): boolean {
    return !!element<HTMLInputElement>('search').value || ['state-filter', 'kind-filter'].some(id => !!element<HTMLSelectElement>(id).value);
  }
  function resetFilters(): void {
    element<HTMLInputElement>('search').value = '';
    for (const id of ['state-filter', 'kind-filter']) element<HTMLSelectElement>(id).value = '';
    offset = 0; selected.clear(); void loadMedia().catch(error => notify(error.message, true));
  }
  function updateNextStep(): void {
    if (!status) return;
    const counts = status.statistics;
    const reviewing = Math.max(0, counts.candidates - counts.published - counts.unavailable);
    const active = status.jobs.filter(job => job.status === 'queued' || job.status === 'running');
    const action = element<HTMLAnchorElement>('next-step-action');
    action.removeAttribute('target'); action.dataset.review = '';
    let title: string; let description: string;
    if (!counts.sources) {
      title = 'اولین پوشه‌ات را معرفی کن'; description = 'آرشیو روی همین رایانه می‌ماند؛ تو انتخاب می‌کنی چه چیزی منتشر شود.'; action.href = '#sources'; action.textContent = 'معرفی پوشه';
    } else if (active.length) {
      title = 'کتابخانه در حال آماده‌شدن است'; description = `${fa(active.length)} اسکن فعال یا در صف است. فایل‌های شناسایی‌شده همین حالا قابل بازبینی‌اند.`; action.href = '#catalog'; action.textContent = 'بازبینی فایل‌ها';
    } else if (reviewing) {
      title = `${fa(reviewing)} رسانه آمادهٔ انتخاب است`; description = 'موارد دلخواه را انتخاب و منتشر کن تا در صفحهٔ تلویزیون دیده شوند.'; action.href = '#catalog'; action.textContent = 'بازبینی و انتشار'; action.dataset.review = 'true';
    } else if (!counts.published) {
      title = 'هنوز رسانه‌ای برای تماشا منتشر نشده'; description = 'مسیر پوشه‌ها و نتیجهٔ اسکن را بررسی کن.'; action.href = '#sources'; action.textContent = 'بررسی پوشه‌ها';
    } else if (!status.mediaOnline) {
      title = 'رسانه‌ها منتشر شده‌اند؛ پخش را راه‌اندازی کن'; description = 'پنل مدیریت فعال است. وضعیت برنامهٔ پخش را در بخش سرویس ببین.'; action.href = '#service'; action.textContent = 'بررسی پخش';
    } else {
      title = 'کتابخانه آمادهٔ تماشاست'; description = `${fa(counts.published)} رسانه در صفحهٔ تلویزیون در دسترس است.`; action.href = element<HTMLAnchorElement>('open-tv').href; action.target = '_blank'; action.rel = 'noopener'; action.textContent = 'بازکردن تلویزیون';
    }
    element('next-step-title').textContent = title; element('next-step-description').textContent = description;
  }
  async function loadMedia(): Promise<void> {
    const current = ++generation;
    const query = new URLSearchParams({ offset: String(offset), limit: '30', search: element<HTMLInputElement>('search').value, sourceId: element<HTMLSelectElement>('source-filter').value,
      state: element<HTMLSelectElement>('state-filter').value, kind: element<HTMLSelectElement>('kind-filter').value });
    if (query.get('sourceId')) query.set('folder', currentFolder);
    element('catalog-results').setAttribute('aria-busy', 'true'); element('catalog-loading').hidden = false;
    try {
      const [folders, result] = await Promise.all([
        api<Didanihaa.FolderListing>(`/api/folders?${query}`),
        query.get('sourceId') ? api<Didanihaa.Page<Didanihaa.Candidate>>(`/api/media?${query}`) : Promise.resolve({ items: [], total: 0, offset: 0, limit: 30 })
      ]);
      if (current !== generation) return;
      listing = folders; page = result; renderFolders(folders, navigateFolder); renderMedia();
      element('folder-heading').hidden = !folders.sourceId;
      element('folder-title').textContent = currentFolder.split('/').pop() || folders.sourceName || '';
      const preview = element<HTMLAnchorElement>('preview-folder'); preview.hidden = !folders.sourceId || folders.directPublishedFiles + folders.folders.reduce((count, child) => count + child.publishedFiles, 0) === 0;
      const previewQuery = new URLSearchParams(); if (folders.sourceId) { previewQuery.set('sourceId', folders.sourceId); previewQuery.set('folder', currentFolder); }
      if (query.get('kind')) previewQuery.set('kind', query.get('kind')!); if (query.get('search')) previewQuery.set('search', query.get('search')!); preview.href = `/tv/?${previewQuery}`;
      element('publish-folder').textContent = `انتشار ${fa(folders.directAvailableFiles)} فایل همین پوشه${filtersActive() ? ' در این نتیجه' : ''}`;
      element('selection-count').closest<HTMLElement>('.selection-bar')!.hidden = !folders.sourceId || !page.total;
      element('previous').closest<HTMLElement>('footer')!.hidden = !folders.sourceId || page.total <= page.limit;
      element('catalog-empty').hidden = page.items.length > 0 || folders.folders.length > 0;
      if (!page.items.length && !folders.folders.length && folders.sourceId && !filtersActive()) {
        element('empty-title').textContent = 'در این پوشه رسانه‌ای شناسایی نشده است.';
        element('empty-description').textContent = 'مسیر منبع را بررسی و اسکن را تازه‌سازی کن.';
      }
    } finally {
      if (current === generation) { element('catalog-results').setAttribute('aria-busy', 'false'); element('catalog-loading').hidden = true; }
    }
  }
  function navigateFolder(sourceId: string | null, path: string): void {
    element<HTMLSelectElement>('source-filter').value = sourceId || ''; currentFolder = path;
    offset = 0; selected.clear();
    const query = new URLSearchParams(); if (sourceId) { query.set('sourceId', sourceId); query.set('folder', path); }
    history.pushState(null, '', `${location.pathname}?${query}#catalog`); showAdminPage('catalog', false);
    void loadMedia().then(() => element('catalog-heading').focus({ preventScroll: true })).catch(error => notify(error.message, true));
  }
  function renderSources(): void {
    const key = JSON.stringify([sources, status?.jobs]); if (key === sourcesRenderKey) return; sourcesRenderKey = key;
    const list = element('source-list'); list.replaceChildren();
    if (sources.length === 0) list.append(text('div', 'هنوز پوشه‌ای معرفی نشده است.', 'empty'));
    for (const source of sources) {
      const row = text('div', '', 'source-row'); const description = text('div', '', 'source-description');
      description.append(text('strong', source.name), text('div', source.resolvedPath, 'source-path mono'));
      const job = status?.jobs.find(job => job.sourceId === source.id);
      if (job) description.append(text('div', `${jobLabels[job.status] || job.status} · ${fa(job.scanned)} رسانه${job.errors ? ` · ${fa(job.errors)} خطا` : ''}${job.message ? ` — ${job.message}` : ''}`, 'job-description'));
      const button = document.createElement('button'); button.type = 'button'; button.className = 'button'; button.textContent = '↻ اسکن مجدد'; button.disabled = job?.status === 'running' || job?.status === 'queued';
      button.addEventListener('click', () => { button.disabled = true; void api(`/api/sources/${source.id}/scan`, {}).then(() => refresh()).catch(error => notify(String(error.message), true)); });
      const browse = document.createElement('button'); browse.type = 'button'; browse.className = 'button'; browse.textContent = 'بازبینی پوشه ↗';
      browse.addEventListener('click', () => { navigateFolder(source.id, ''); element('catalog').scrollIntoView({ block: 'start' }); });
      row.append(description, browse, button); list.append(row);
    }
  }
  async function loadSources(): Promise<void> {
    sources = await api<Didanihaa.Source[]>('/api/sources');
    const filter = element<HTMLSelectElement>('source-filter'); const previous = filter.value;
    filter.replaceChildren(new Option('همهٔ پوشه‌ها', ''));
    for (const source of sources) filter.add(new Option(source.name, source.id));
    filter.value = previous; renderSources();
  }
  async function refresh(): Promise<void> {
    if (polling) return; polling = true;
    try {
      status = await api<Didanihaa.Status>('/api/status');
      for (const key of ['sources', 'candidates', 'published', 'unavailable'] as const) element(`stat-${key}`).setAttribute('value', fa(status.statistics[key]));
      const live = element('live-state'); live.setAttribute('label', status.mediaOnline ? 'پخش در دسترس است' : 'سرویس پخش در دسترس نیست'); live.setAttribute('tone', status.mediaOnline ? 'good' : 'warn');
      element('service-state').textContent = serviceLabels[status.service.state] || status.service.state;
      const ordinaryPlayback = status.mediaOnline && status.service.state !== 'running';
      element('service-mode').hidden = !ordinaryPlayback;
      element('service-mode').textContent = 'پخش به‌صورت عادی فعال است و به نصب سرویس نیاز ندارد. پیش از راه‌اندازی Windows Service، نمونهٔ عادی را متوقف کن.';
      document.querySelectorAll<HTMLButtonElement>('[data-service]').forEach(button => {
        const action = button.dataset.service;
        button.disabled = serviceBusy || !status!.service.supported || (action !== 'install' && !status!.service.installed)
          || (action === 'stop' && status!.service.state === 'stopped') || (action === 'start' && status!.service.state === 'running')
          || (ordinaryPlayback && (action === 'start' || action === 'restart'));
      });
      renderSources();
      updateNextStep();
      const signature = status.jobs.map(job => `${job.id}:${job.scanned}:${job.status}`).join('|');
      if (signature !== jobSignature) { jobSignature = signature; await loadMedia(); }
    } finally { polling = false; }
  }
  element<HTMLFormElement>('source-form').addEventListener('submit', event => {
    event.preventDefault(); const button = element('source-form').querySelector('button')!; button.disabled = true;
    void api('/api/sources', { name: element<HTMLInputElement>('source-name').value, path: element<HTMLInputElement>('source-path').value })
      .then(async () => { element<HTMLFormElement>('source-form').reset(); await loadSources(); await refresh(); notify('پوشه اضافه شد و اسکن در صف قرار گرفت.'); })
      .catch(error => notify(error.message, true)).finally(() => { button.disabled = false; });
  });
  element<HTMLInputElement>('select-all').addEventListener('change', () => {
    const checked = element<HTMLInputElement>('select-all').checked;
    for (const item of page.items.filter(item => item.available)) { if (checked && selected.size < 100) selected.add(item.id); else if (!checked) selected.delete(item.id); }
    if (checked && selected.size === 100) notify('سقف انتخاب هر نوبت ۱۰۰ رسانه است.');
    renderMedia();
  });
  for (const [id, published] of [['publish', true], ['unpublish', false]] as const) {
    element(id).addEventListener('click', () => {
      if (publicationBusy || !selected.size) return;
      publicationBusy = true; selectionChanged();
      void api<{ updated: number }>('/api/publication', { ids: Array.from(selected), published })
        .then(async result => { selected.clear(); offset = 0; await loadMedia(); await refresh(); notify(`${fa(result.updated)} رسانه ${published ? 'منتشر شد' : 'از انتشار خارج شد'}.`); })
        .catch(error => notify(error.message, true)).finally(() => { publicationBusy = false; selectionChanged(); });
    });
  }
  let debounce = 0;
  element('search').addEventListener('input', () => { window.clearTimeout(debounce); debounce = window.setTimeout(() => { offset = 0; selected.clear(); void loadMedia().catch(error => notify(error.message, true)); }, 300); });
  element('source-filter').addEventListener('change', () => navigateFolder(element<HTMLSelectElement>('source-filter').value || null, ''));
  for (const id of ['state-filter', 'kind-filter']) element(id).addEventListener('change', () => { offset = 0; selected.clear(); void loadMedia().catch(error => notify(error.message, true)); });
  for (const [id, published] of [['publish-folder', true], ['unpublish-folder', false]] as const) element(id).addEventListener('click', () => {
    if (publicationBusy || !listing?.sourceId) return;
    publicationBusy = true; selectionChanged();
    const body = { sourceId: listing.sourceId, folder: currentFolder, published, search: element<HTMLInputElement>('search').value || null,
      kind: element<HTMLSelectElement>('kind-filter').value || null, state: element<HTMLSelectElement>('state-filter').value || null };
    void api<{ updated: number }>('/api/folders/publication', body).then(async result => {
      selected.clear(); offset = 0; await loadMedia(); await refresh(); notify(`${fa(result.updated)} فایل همین پوشه ${published ? 'منتشر شد' : 'از انتشار خارج شد'}.`);
    }).catch(error => notify(error.message, true)).finally(() => { publicationBusy = false; selectionChanged(); });
  });
  element('clear-filters').addEventListener('click', resetFilters);
  element('empty-reset').addEventListener('click', resetFilters);
  element('clear-selection').addEventListener('click', () => { selected.clear(); renderMedia(); });
  element('dismiss-message').addEventListener('click', () => { element('message').hidden = true; });
  element('next-step-action').addEventListener('click', () => {
    if (element('next-step-action').dataset.review) { element<HTMLInputElement>('search').value = ''; element<HTMLSelectElement>('source-filter').value = ''; element<HTMLSelectElement>('kind-filter').value = ''; element<HTMLSelectElement>('state-filter').value = 'review'; offset = 0; selected.clear(); void loadMedia().catch(error => notify(error.message, true)); }
  });
  element('refresh').addEventListener('click', () => { void Promise.all([loadSources(), loadMedia(), refresh()]).catch(error => notify(error.message, true)); });
  element('previous').addEventListener('click', () => { offset = Math.max(0, offset - page.limit); void loadMedia().catch(error => notify(error.message, true)); });
  element('next').addEventListener('click', () => { offset += page.limit; void loadMedia().catch(error => notify(error.message, true)); });
  document.querySelectorAll<HTMLButtonElement>('[data-service]').forEach(button => button.addEventListener('click', () => {
    serviceBusy = true; document.querySelectorAll<HTMLButtonElement>('[data-service]').forEach(button => { button.disabled = true; });
    notify('در انتظار تأیید دسترسی ویندوز و انجام عملیات…');
    void api<{ message: string }>(`/api/service/${button.dataset.service}`, {})
      .then(result => notify(result.message)).catch(error => notify(error.message, true))
      .finally(() => { serviceBusy = false; void refresh().catch(error => notify(error.message, true)); });
  }));
  const profileLink = document.createElement('a'); profileLink.href = '#profile'; profileLink.className = 'nav-link'; profileLink.textContent = '♡ پروفایل خانه'; document.querySelector('.sidebar nav')!.append(profileLink);
  window.addEventListener('popstate', () => {
    if (!session) return;
    const query = new URLSearchParams(location.search); currentFolder = query.get('folder') || '';
    element<HTMLSelectElement>('source-filter').value = query.get('sourceId') || ''; offset = 0; selected.clear();
    showAdminPage(location.hash.slice(1), false); void loadMedia().catch(error => notify(error.message, true));
  });
  async function loadProfile(): Promise<void> {
    const data = await api<{ profile: Didanihaa.HomeProfile; favoriteIds: string[]; items: { id: string; title: string; visible: boolean }[] }>('/api/profile');
    if (!profileFormDirty) {
      element<HTMLInputElement>('profile-name').value = data.profile.name;
      element<HTMLInputElement>('profile-autoplay').checked = data.profile.autoplay;
      element<HTMLInputElement>('profile-repeat').checked = data.profile.repeatFolder;
      element<HTMLInputElement>('profile-nav-limit').value = String(data.profile.navListLimit ?? 5);
    }
    element('profile-summary').textContent = `${fa(data.favoriteIds.length)} علاقه‌مندی در SQLite این دستگاه ذخیره شده است. تنظیمات این پروفایل بین TV و مرورگرهای شبکه مشترک است.`;
    let list = document.getElementById('profile-favorites');
    if (!list) { list = document.createElement('div'); list.id = 'profile-favorites'; element('profile').append(list); }
    const favoritesKey = JSON.stringify(data.items); if (favoritesKey === profileFavoritesRenderKey) return; profileFavoritesRenderKey = favoritesKey;
    list.replaceChildren();
    for (const item of data.items) {
      const row = text('div', '', 'source-row'); row.append(text('span', item.title + (item.visible ? '' : ' · در TV در دسترس نیست')));
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'button'; remove.textContent = 'حذف از علاقه‌مندی‌ها'; remove.setAttribute('aria-label', `حذف ${item.title} از علاقه‌مندی‌ها`);
      remove.addEventListener('click', () => { remove.disabled = true; void api('/api/profile/favorites', { mediaId: item.id, favorite: false }).then(() => loadProfile()).catch(error => notify(error.message, true)); });
      row.append(remove); list.append(row);
    }
  }
  element('profile-form').addEventListener('input', () => { profileFormDirty = true; });
  window.addEventListener('hashchange', () => { if (session && location.hash === '#profile') void loadProfile().catch(error => notify(error.message, true)); });
  element('profile-form').addEventListener('submit', event => {
    event.preventDefault(); const button = element('profile-form').querySelector('button')!; button.disabled = true;
    void api('/api/profile', { name: element<HTMLInputElement>('profile-name').value, autoplay: element<HTMLInputElement>('profile-autoplay').checked, repeatFolder: element<HTMLInputElement>('profile-repeat').checked, navListLimit: Number(element<HTMLInputElement>('profile-nav-limit').value) })
      .then(async () => { profileFormDirty = false; await loadProfile(); notify('پروفایل مشترک ذخیره شد.'); }).catch(error => notify(error.message, true)).finally(() => { button.disabled = false; });
  });
  async function start(): Promise<void> {
    session = await api<Didanihaa.Session>('/api/session');
    element('catalog-context').textContent = `کتابخانهٔ «${session.root.split(/[\\/]/).pop()}»`;
    element('catalog-context').title = session.root;
    element<HTMLAnchorElement>('open-tv').href = '/tv/';
    element('runtime-root').textContent = session.root;
    const settings = await api<{ ffprobePath: string; thumbnailsAvailable: boolean }>('/api/settings');
    element<HTMLInputElement>('probe-path').value = settings.ffprobePath;
    element('metadata-state').textContent = session.metadataAvailable ? 'خواندن مشخصات پیشرفته فعال است.' : 'مشخصات پایه فعال است؛ ابزار خواندن مشخصات پیشرفته هنوز اضافه نشده.';
    element('viewer-addresses').textContent = session.viewerUrls.length ? session.viewerUrls.join('\n') : `http://<IP این رایانه>:${session.publicPort}/tv/`;
    await loadSources(); element<HTMLSelectElement>('source-filter').value = new URLSearchParams(location.search).get('sourceId') || '';
    await loadMedia(); await refresh();
    await loadProfile(); showAdminPage(location.hash.slice(1), false);
    window.setInterval(() => { void refresh().catch(error => notify(`اتصال به مدیریت قطع شد: ${error.message}`, true)); }, 3000);
  }
  element<HTMLFormElement>('settings-form').addEventListener('submit', event => {
    event.preventDefault(); const button = element('settings-form').querySelector('button')!; button.disabled = true;
    void api<{ ffprobePath: string; metadataAvailable: boolean }>('/api/settings', { ffprobePath: element<HTMLInputElement>('probe-path').value })
      .then(result => { element<HTMLInputElement>('probe-path').value = result.ffprobePath; element('metadata-state').textContent = result.metadataAvailable ? 'خواندن مشخصات پیشرفته فعال است.' : 'ابزار در دسترس نیست.'; notify('مسیر ابزار ذخیره شد.'); })
      .catch(error => notify(error.message, true)).finally(() => { button.disabled = false; });
  });
  void start().catch(error => notify(error.message, true));
})();
