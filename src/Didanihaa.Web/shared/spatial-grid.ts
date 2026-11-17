export function spatialTarget(active: HTMLElement, nodes: HTMLElement[], key: string): HTMLElement | undefined {
  const origin = active.getBoundingClientRect(); const horizontal = key === 'ArrowLeft' || key === 'ArrowRight';
  const candidates = nodes.filter(node => node !== active && node.getClientRects().length > 0 && !node.closest('[hidden]')).map(node => {
    const rect = node.getBoundingClientRect();
    const gap = key === 'ArrowRight' ? rect.left - origin.right : key === 'ArrowLeft' ? origin.left - rect.right : key === 'ArrowDown' ? rect.top - origin.bottom : origin.top - rect.bottom;
    const overlap = horizontal ? rect.top < origin.bottom && rect.bottom > origin.top : rect.left < origin.right && rect.right > origin.left;
    const secondary = horizontal ? Math.abs(rect.top + rect.height / 2 - origin.top - origin.height / 2) : Math.abs(rect.left + rect.width / 2 - origin.left - origin.width / 2);
    return { node, gap, overlap, secondary };
  }).filter(item => item.gap >= -2);
  const aligned = candidates.filter(item => item.overlap);
  return (aligned.length ? aligned.sort((a, b) => a.gap - b.gap || a.secondary - b.secondary) : candidates.sort((a, b) => a.gap + a.secondary * 3 - b.gap - b.secondary * 3))[0]?.node;
}
export function focusTv(node: HTMLElement): void { node.focus({ preventScroll: true }); node.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
const observers = new WeakMap<HTMLElement, ResizeObserver>();
export function enableSpatialGrid(grid: HTMLElement, label: string, selector = '.media-card,.folder-card'): void {
  observers.get(grid)?.disconnect(); grid.setAttribute('role', 'grid'); grid.setAttribute('aria-label', label);
  const cards = Array.from(grid.querySelectorAll<HTMLElement>(selector));
  const active = document.activeElement as HTMLElement;
  let current = cards.includes(active) ? active : cards[0];
  function rows(): void {
    const focused = document.activeElement as HTMLElement;
    const groups: HTMLElement[][] = []; let top = -Infinity;
    cards.forEach(card => { const y = card.getBoundingClientRect().top; if (Math.abs(y - top) > 4) { groups.push([]); top = y; } groups[groups.length - 1].push(card); });
    const fragment = document.createDocumentFragment();
    groups.forEach((group, index) => {
      const row = document.createElement('div'); row.setAttribute('role', 'row'); row.setAttribute('aria-rowindex', String(index + 1)); row.style.display = 'contents';
      group.forEach(card => { const cell = document.createElement('div'); cell.setAttribute('role', 'gridcell'); cell.style.display = 'contents'; cell.append(card); row.append(cell); }); fragment.append(row);
    });
    grid.replaceChildren(fragment); grid.setAttribute('aria-rowcount', String(groups.length));
    if (cards.includes(focused)) focused.focus({ preventScroll: true });
  }
  cards.forEach(card => { card.tabIndex = card === current ? 0 : -1; card.addEventListener('focus', () => { current = card; cards.forEach(node => { node.tabIndex = node === current ? 0 : -1; }); }); });
  rows();
  if (typeof ResizeObserver !== 'undefined') { let width = grid.clientWidth; const observer = new ResizeObserver(() => { if (grid.clientWidth !== width) { width = grid.clientWidth; rows(); } }); observer.observe(grid); observers.set(grid, observer); }
  grid.onkeydown = event => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const focused = (event.composedPath ? event.composedPath()[0] : event.target) as HTMLElement;
    if (!cards.includes(focused)) return;
    const target = event.key === 'Home' ? cards[0] : event.key === 'End' ? cards[cards.length - 1] : ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) ? spatialTarget(focused, cards, event.key) : undefined;
    if (target && target !== focused) { event.preventDefault(); focusTv(target); }
  };
}
export function enableToolbar(toolbar: HTMLElement): void {
  const controls = (): HTMLElement[] => Array.from(toolbar.querySelectorAll<HTMLElement>('button:not(:disabled),a[href],input:not(:disabled)')).filter(node => node.getClientRects().length > 0 && !node.closest('[hidden]'));
  const items = controls(); let current = items.includes(document.activeElement as HTMLElement) ? document.activeElement as HTMLElement : items[0];
  items.forEach(node => { node.tabIndex = node === current ? 0 : -1; });
  if (!toolbar.dataset.rovingFocusBound) {
    toolbar.addEventListener('focusin', event => { const node = event.target as HTMLElement; if (controls().includes(node)) { current = node; controls().forEach(item => { item.tabIndex = item === current ? 0 : -1; }); } });
    toolbar.dataset.rovingFocusBound = 'true';
  }
  toolbar.onkeydown = event => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const node = event.target as HTMLElement;
    if (node.matches('input,textarea,select,[contenteditable]')) return;
    const nodes = controls(); if (!nodes.includes(node)) return;
    const target = event.key === 'Home' ? nodes[0] : event.key === 'End' ? nodes[nodes.length - 1] : ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) ? spatialTarget(node, nodes, event.key) : undefined;
    if (target && target !== node) { event.preventDefault(); focusTv(target); }
  };
}
