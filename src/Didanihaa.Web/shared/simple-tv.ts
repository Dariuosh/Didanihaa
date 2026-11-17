// ES5 output, no fetch, modules, CSS Grid, custom elements or Promise dependency.
(function () {
  var token = ''; var favorites: string[] = []; var autoplay = true; var canRecord = false; var reported = false;
  var media = document.querySelector('video,audio') as HTMLMediaElement | null;
  var favorite = document.getElementById('simple-favorite') as HTMLButtonElement | null;
  var status = document.getElementById('simple-status');
  function request<T>(path: string, body: unknown, done: (value: T) => void, quiet?: boolean): void {
    var xhr = new XMLHttpRequest(); xhr.open(body ? 'POST' : 'GET', path, true);
    if (body) { xhr.setRequestHeader('Content-Type', 'application/json'); xhr.setRequestHeader('X-Didanihaa-Profile', token); }
    xhr.onload = function () {
      if (xhr.status >= 200 && xhr.status < 300) { try { done(JSON.parse(xhr.responseText)); } catch (_error) { if (status) status.textContent = 'پاسخ پروفایل خوانده نشد.'; } }
      else if (status && !quiet) status.textContent = 'ارتباط با پروفایل انجام نشد. صفحه را دوباره باز کن.';
    };
    xhr.onerror = function () { if (status && !quiet) status.textContent = 'ارتباط شبکه قطع است.'; }; xhr.send(body ? JSON.stringify(body) : null);
  }
  function reportPlayback(): void {
    if (!media || !canRecord || !token || reported) return;
    var played = 0; for (var index = 0; index < media.played.length; index++) played += media.played.end(index) - media.played.start(index);
    var threshold = isFinite(media.duration) && media.duration > 0 ? Math.min(5, Math.max(.1, media.duration / 2)) : 5;
    if (played < threshold) return; reported = true;
    if (favorite) request<{ recorded: boolean }>('/api/profile/playback', { mediaId: favorite.getAttribute('data-media-id') }, function () {}, true);
  }
  request<Didanihaa.ProfileSession>('/api/profile', null, function (session) {
    token = session.mutationToken; favorites = session.favoriteIds; autoplay = session.profile.autoplay; canRecord = typeof session.profile.navListLimit === 'number'; reportPlayback();
    if (favorite) { favorite!.disabled = false; favorite!.textContent = favorites.indexOf(favorite!.getAttribute('data-media-id') || '') >= 0 ? 'حذف از علاقه‌مندی‌ها' : '♡ افزودن به علاقه‌مندی‌ها'; }
  });
  if (favorite) favorite!.onclick = function () {
    var id = favorite!.getAttribute('data-media-id') || ''; favorite!.disabled = true;
    request<{ favoriteIds: string[] }>('/api/profile/favorites', { mediaId: id, favorite: favorites.indexOf(id) < 0 }, function (value) {
      favorites = value.favoriteIds; favorite!.disabled = false; favorite!.textContent = favorites.indexOf(id) >= 0 ? 'حذف از علاقه‌مندی‌ها' : '♡ افزودن به علاقه‌مندی‌ها';
    });
  };
  if (media) {
    var container = document.getElementById('simple-media')!;
    if (window.DidanihaaPlayer) window.DidanihaaPlayer.attach(media, container);
    media.addEventListener('timeupdate', reportPlayback); media.addEventListener('ended', reportPlayback);
    media.addEventListener('ended', function () { var next = document.getElementById('simple-next') as HTMLAnchorElement | null; if (autoplay && next) window.location.replace(next.href); });
  }
  document.addEventListener('keydown', function (event) {
    var active = document.activeElement as HTMLElement; if (!active || /^(INPUT|TEXTAREA|VIDEO|AUDIO)$/.test(active.tagName)) return;
    var zone = active.closest ? active.closest('[role=grid],[role=toolbar],.simple-toolbar,.media-controls') : active.parentElement;
    if (!zone) return;
    var direction = event.key || ({ 37: 'ArrowLeft', 38: 'ArrowUp', 39: 'ArrowRight', 40: 'ArrowDown' } as Record<number, string>)[event.keyCode];
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].indexOf(direction) < 0) return;
    var all = document.querySelectorAll<HTMLElement>('a[href],button:not(:disabled),input:not([type=hidden])');
    var origin = active.getBoundingClientRect(); var best: HTMLElement | null = null; var score = Infinity;
    for (var i = 0; i < all.length; i++) {
      var item = all[i]; if (item === active || item.getClientRects().length === 0) continue;
      var rect = item.getBoundingClientRect(); var horizontal = direction === 'ArrowLeft' || direction === 'ArrowRight';
      var gap = direction === 'ArrowLeft' ? origin.left - rect.right : direction === 'ArrowRight' ? rect.left - origin.right : direction === 'ArrowUp' ? origin.top - rect.bottom : rect.top - origin.bottom;
      if (gap < -2) continue;
      var secondary = horizontal ? Math.abs(rect.top + rect.height / 2 - origin.top - origin.height / 2) : Math.abs(rect.left + rect.width / 2 - origin.left - origin.width / 2);
      var candidate = gap + secondary * 3; if (candidate < score) { score = candidate; best = item; }
    }
    if (best) { event.preventDefault(); best.focus(); }
  });
})();
