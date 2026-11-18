const pages: Record<string, string> = { catalog: 'کتابخانه', overview: 'نمای کلی', sources: 'پوشه‌های منبع', profile: 'پروفایل خانه', service: 'سرویس پخش', settings: 'تنظیمات ابزار' };

export function showAdminPage(id: string, focus = true): void {
  if (!pages[id]) id = 'catalog';
  document.querySelectorAll<HTMLElement>('main > section[id]').forEach(section => { section.hidden = section.id !== id; });
  document.querySelectorAll<HTMLAnchorElement>('.nav-link').forEach(link => {
    const current = link.hash === `#${id}`; link.classList.toggle('active', current);
    if (current) link.setAttribute('aria-current', 'page'); else link.removeAttribute('aria-current');
  });
  const heading = document.getElementById('page-title')!; heading.textContent = pages[id];
  document.title = `${pages[id]} · مدیریت دیدنی‌ها`;
  if (focus) { heading.focus({ preventScroll: true }); window.scrollTo({ top: 0, behavior: 'instant' }); }
}

window.addEventListener('hashchange', () => showAdminPage(location.hash.slice(1)));
document.addEventListener('click', event => {
  const link = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="#"]');
  if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
  const id = link.hash.slice(1);
  if (pages[id] && link.hash === location.hash) showAdminPage(id);
});
if (!location.hash || !pages[location.hash.slice(1)]) history.replaceState(null, '', `${location.pathname}${location.search}#catalog`);
showAdminPage(location.hash.slice(1), false);
