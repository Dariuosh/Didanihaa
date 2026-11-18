type Route = { kind: string; favorites: boolean; sourceId: string | null; folder: string };
const categories = [ ['favorites', '', 'پوشه‌های علاقه‌مندی'], ['video', 'video', 'پوشه‌های ویدیو'], ['audio', 'audio', 'پوشه‌های صدا'] ] as const;
let generation = 0;

export async function renderNavigation(home: Didanihaa.ProfileSession, route: Route, navigate: (query: URLSearchParams) => void): Promise<void> {
  // An older running backend can serve the current source preview until the user rebuilds it.
  if (home.profile.navListLimit === undefined) return;
  const request = ++generation;
  const limit = home.profile.navListLimit;
  await Promise.all(categories.map(async ([category, kind, label]) => {
    const heading = document.querySelector<HTMLAnchorElement>(`.tv-navigation > [data-category="${category}"]`)!;
    let group = document.getElementById(`nav-lists-${category}`);
    if (!group) { group = document.createElement('div'); group.id = `nav-lists-${category}`; group.className = 'nav-lists'; group.setAttribute('role', 'group'); group.setAttribute('aria-label', label); heading.after(group); }
    const query = new URLSearchParams({ limit: String(limit) }); if (kind) query.set('kind', kind); if (category === 'favorites') query.set('favorites', 'true');
    try {
      const response = await fetch(`/api/playlists?${query}`, { cache: 'no-store' }); if (!response.ok) return;
      const page = await response.json() as Didanihaa.Page<Didanihaa.PublicPlaylist>;
      if (request !== generation) return;
      group.replaceChildren(); group.classList.toggle('is-expanded', category === (route.favorites ? 'favorites' : route.kind));
      const link = (title: string, params: URLSearchParams, className = ''): HTMLAnchorElement => {
        const node = document.createElement('a'); node.className = className; node.href = `/tv/?${params}`; node.textContent = title;
        node.addEventListener('click', event => { if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.button !== 0) return; event.preventDefault(); navigate(params); }); group!.append(node); return node;
      };
      if (page.items.length) {
        const title = document.createElement('span'); title.className = 'nav-list-label'; title.textContent = 'پوشه‌ها'; group.append(title);
        for (const item of page.items) {
          const params = new URLSearchParams({ sourceId: item.sourceId, folder: item.folder }); if (kind) params.set('kind', kind); if (category === 'favorites') params.set('view', 'favorites');
          const node = link(item.name, params, 'nav-folder'); node.setAttribute('aria-label', `${item.name} · ${item.sourceName}`); node.title = `${item.sourceName} / ${item.folder || item.name}`;
          if (route.sourceId === item.sourceId && route.folder === item.folder && category === (route.favorites ? 'favorites' : route.kind)) node.setAttribute('aria-current', 'page');
        }
      }
      if (category !== 'favorites') {
        const popular = new URLSearchParams({ kind, view: 'popular' }); link('↗ پربازدیدها', popular, 'nav-dynamic');
        const all = new URLSearchParams({ kind, view: 'playlists' }); link(`نمایش همه${page.total ? ` (${page.total.toLocaleString('fa-IR')})` : ''}`, all, 'nav-show-all');
      } else if (page.total > limit) link('نمایش همهٔ علاقه‌مندی‌ها', new URLSearchParams({ view: 'favorites' }), 'nav-show-all');
      group.hidden = category === 'favorites' && !page.items.length;
    } catch { /* The library and playback remain usable during a temporary navigation failure. */ }
  }));
}
