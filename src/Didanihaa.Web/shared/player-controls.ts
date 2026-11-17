// No imports: this file is also emitted as ES5 for the simple TV view.
interface DidanihaaPlayerApi { attach(media: HTMLMediaElement, container: HTMLElement): () => void; }
interface Window { DidanihaaPlayer?: DidanihaaPlayerApi; }

(function () {
  var sequence = 0;
  var paths: Record<string, string> = {
    play: 'M8 5v14l11-7z', pause: 'M7 5h3v14H7zM14 5h3v14h-3z',
    volume: 'M4 9v6h4l5 4V5L8 9H4m12-1a6 6 0 010 8m3-11a10 10 0 010 14',
    muted: 'M4 9v6h4l5 4V5L8 9H4m12 0 6 6m0-6-6 6',
    fullscreen: 'M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5',
    restore: 'M3 8h5V3m8 0v5h5M8 21v-5H3m18 0h-5v5',
    native: 'M3 5h18v14H3zM7 9v6m5-6v6m5-6v6',
    rewind: 'M6 8H2V4m1 4a9 9 0 111 10', forward: 'M18 8h4V4m-1 4a9 9 0 10-1 10'
  };
  function icon(name: string): SVGSVGElement {
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('width', '24'); svg.setAttribute('height', '24'); svg.setAttribute('aria-hidden', 'true');
    var shape = document.createElementNS('http://www.w3.org/2000/svg', 'path'); shape.setAttribute('d', paths[name]);
    shape.setAttribute('fill', name === 'play' || name === 'pause' ? 'currentColor' : 'none'); shape.setAttribute('stroke', 'currentColor');
    shape.setAttribute('stroke-width', '1.7'); shape.setAttribute('stroke-linecap', 'round'); shape.setAttribute('stroke-linejoin', 'round'); svg.appendChild(shape);
    if (name === 'rewind' || name === 'forward') {
      var number = document.createElementNS('http://www.w3.org/2000/svg', 'text'); number.setAttribute('x', '12'); number.setAttribute('y', '15'); number.setAttribute('text-anchor', 'middle'); number.setAttribute('fill', 'currentColor'); number.setAttribute('font-size', '8'); number.textContent = '10'; svg.appendChild(number);
    }
    return svg;
  }
  function time(value: number): string {
    if (!isFinite(value) || value < 0) return '0:00';
    var seconds = Math.floor(value); var minutes = Math.floor(seconds / 60); var remainder = seconds % 60;
    return (minutes >= 60 ? Math.floor(minutes / 60) + ':' + (minutes % 60 < 10 ? '0' : '') : '') + (minutes >= 60 ? minutes % 60 : minutes) + ':' + (remainder < 10 ? '0' : '') + remainder;
  }
  window.DidanihaaPlayer = { attach: function (media, container) {
    var video = media.tagName.toLowerCase() === 'video';
    var shell = (container.closest ? container.closest('.player-shell') as HTMLElement : null) || container;
    var listeners: { target: EventTarget; type: string; fn: EventListener }[] = [];
    var idleTimer = 0; var clickTimer = 0; var feedbackTimer = 0; var keyboard = false; var waiting = false; var disposed = false;
    var artwork: HTMLElement | null = null;
    var details = shell.querySelector<HTMLElement>('.player-details');
    var detailsObserver: ResizeObserver | null = null;
    function measureDetails(): void { if (details) shell.style.setProperty('--player-details-height', details.getBoundingClientRect().height + 'px'); }
    var fullDocument = document as Document & { webkitFullscreenElement?: Element; webkitExitFullscreen?: () => void };
    var fullscreenTarget = shell as HTMLElement & { webkitRequestFullscreen?: () => void };
    var nativeVideo = media as HTMLVideoElement & { webkitEnterFullscreen?: () => void };
    function on(target: EventTarget, type: string, fn: EventListener): void { target.addEventListener(type, fn); listeners.push({ target: target, type: type, fn: fn }); }
    function setIcon(node: HTMLElement, name: string): void { while (node.firstChild) node.removeChild(node.firstChild); node.appendChild(icon(name)); }
    var status = document.createElement('span'); status.className = 'media-playback-status'; status.setAttribute('role', 'status');
    var bar = document.createElement('div'); bar.className = 'media-controls'; bar.setAttribute('role', 'toolbar'); bar.setAttribute('aria-label', 'کنترل پخش'); bar.setAttribute('data-tv-zone', 'transport');
    var row = document.createElement('div'); row.className = 'media-controls-row';
    function button(label: string, name: string, action: () => void): HTMLButtonElement {
      var node = document.createElement('button'); node.type = 'button'; node.tabIndex = -1; node.className = 'player-control'; node.setAttribute('aria-label', label); node.setAttribute('title', label); node.setAttribute('data-tooltip', label); setIcon(node, name);
      on(node, 'click', action); row.appendChild(node); return node;
    }
    function reveal(): void { shell.classList.remove('player-idle'); window.clearTimeout(idleTimer); scheduleHide(); }
    function scheduleHide(): void {
      window.clearTimeout(idleTimer);
      if (!video || media.paused || media.ended || waiting || media.controls) return;
      idleTimer = window.setTimeout(function () {
        var active = document.activeElement;
        if (keyboard && active !== hit && active !== media && shell.contains(active)) return;
        shell.classList.add('player-idle');
      }, 3000);
    }
    function toggle(): void {
      reveal();
      if (media.paused || media.ended) {
        var attempt = media.play(); if (attempt && typeof attempt.catch === 'function') attempt.catch(function () { if (!disposed) { status.textContent = 'برای شروع، پخش را دوباره بزن.'; reveal(); } });
      } else media.pause();
      shell.classList.add('player-feedback'); window.clearTimeout(feedbackTimer); feedbackTimer = window.setTimeout(function () { shell.classList.remove('player-feedback'); }, 450);
    }
    function isFullscreen(): boolean { var active = document.fullscreenElement || fullDocument.webkitFullscreenElement; return active === shell; }
    function toggleFullscreen(): void {
      reveal();
      try {
        var result: Promise<void> | void = undefined;
        if (isFullscreen()) { if (document.exitFullscreen) result = document.exitFullscreen(); else if (fullDocument.webkitExitFullscreen) fullDocument.webkitExitFullscreen(); }
        else if (fullscreenTarget.requestFullscreen) result = fullscreenTarget.requestFullscreen();
        else if (fullscreenTarget.webkitRequestFullscreen) fullscreenTarget.webkitRequestFullscreen();
        else if (nativeVideo.webkitEnterFullscreen) { media.controls = true; nativeVideo.webkitEnterFullscreen(); updateNative(); }
        if (result && typeof result.catch === 'function') result.catch(function () { if (!disposed) { status.textContent = 'مرورگر تمام‌صفحه را فعال نکرد؛ کنترل مرورگر هم در دسترس است.'; reveal(); } });
      } catch (_error) { status.textContent = 'تمام‌صفحه در این مرورگر فعال نشد.'; }
    }
    var play = button('پخش', 'play', toggle); play.tabIndex = 0; play.setAttribute('data-player-play', '');
    var rewind = button('۱۰ ثانیه عقب', 'rewind', function () { seekTo(media.currentTime - 10); }); rewind.setAttribute('data-player-rewind', '');
    var forward = button('۱۰ ثانیه جلو', 'forward', function () { seekTo(media.currentTime + 10); }); forward.setAttribute('data-player-forward', '');
    var mute = button('بی‌صدا کردن', 'volume', function () { media.muted = !media.muted; if (!media.muted && media.volume === 0) media.volume = .5; }); mute.setAttribute('data-player-mute', '');
    var volumeLabel = document.createElement('label'); volumeLabel.className = 'player-sr-only'; volumeLabel.htmlFor = 'media-volume-' + (++sequence); volumeLabel.textContent = 'بلندی صدا';
    var volume = document.createElement('input'); volume.type = 'range'; volume.tabIndex = -1; volume.id = volumeLabel.htmlFor; volume.className = 'media-volume'; volume.min = '0'; volume.max = '1'; volume.step = '.05'; volume.value = String(media.volume); volume.dir = 'ltr';
    row.appendChild(volumeLabel); row.appendChild(volume);
    var clock = document.createElement('span'); clock.className = 'media-clock'; clock.dir = 'ltr'; clock.textContent = '0:00 / 0:00'; row.appendChild(clock);
    var spacer = document.createElement('span'); spacer.className = 'media-control-spacer'; row.appendChild(spacer);
    var native = button('کنترل مرورگر', 'native', function () { media.controls = !media.controls; updateNative(); }); native.setAttribute('data-player-native', ''); native.setAttribute('aria-pressed', 'false');
    var nativeLabel = document.createElement('span'); nativeLabel.textContent = 'کنترل مرورگر'; nativeLabel.className = 'media-native-label'; native.appendChild(nativeLabel);
    var fullscreen = button('تمام‌صفحه (F)', 'fullscreen', toggleFullscreen); fullscreen.setAttribute('data-player-fullscreen', '');
    if (!video || !('requestFullscreen' in shell || 'webkitRequestFullscreen' in shell || 'webkitEnterFullscreen' in media)) fullscreen.hidden = true;
    var timeline = document.createElement('div'); timeline.className = 'media-timeline';
    var label = document.createElement('label'); label.className = 'player-sr-only'; var slider = document.createElement('input');
    label.htmlFor = 'media-seek-' + sequence; label.textContent = 'موقعیت پخش';
    slider.type = 'range'; slider.tabIndex = -1; slider.id = label.htmlFor; slider.min = '0'; slider.max = '0'; slider.step = '1'; slider.value = '0'; slider.disabled = true; slider.dir = 'ltr'; slider.className = 'media-seek';
    timeline.appendChild(label); timeline.appendChild(slider); bar.appendChild(timeline); bar.appendChild(row); bar.appendChild(status);
    var hit = document.createElement('button'); hit.type = 'button'; hit.className = 'media-hit'; hit.setAttribute('data-player-surface', ''); hit.setAttribute('aria-label', 'پخش'); hit.setAttribute('aria-keyshortcuts', 'Space K');
    var center = document.createElement('span'); center.className = 'media-action-symbol'; center.setAttribute('aria-hidden', 'true'); setIcon(center, 'play'); hit.appendChild(center);
    var loader = document.createElement('span'); loader.className = 'media-loader'; loader.setAttribute('role', 'progressbar'); loader.setAttribute('aria-label', 'دریافت رسانه'); loader.hidden = true;
    function seekTo(value: number): void {
      if (!isFinite(media.duration) || media.duration <= 0 || media.seekable.length === 0) return;
      var start = media.seekable.start(0); var end = media.seekable.end(media.seekable.length - 1);
      try { media.currentTime = Math.max(start, Math.min(value, end)); reveal(); } catch (_error) { status.textContent = 'جابه‌جایی در این فایل ممکن نیست.'; }
    }
    function updateNative(): void {
      container.classList.toggle('media-native', media.controls); native.setAttribute('aria-pressed', String(media.controls)); hit.hidden = media.controls; reveal();
    }
    function fullscreenChanged(): void {
      shell.classList.toggle('media-fullscreen', isFullscreen());
      fullscreen.setAttribute('aria-label', isFullscreen() ? 'خروج از تمام‌صفحه (F)' : 'تمام‌صفحه (F)'); fullscreen.setAttribute('data-tooltip', isFullscreen() ? 'خروج از تمام‌صفحه (F)' : 'تمام‌صفحه (F)'); setIcon(fullscreen, isFullscreen() ? 'restore' : 'fullscreen');
      var active = document.activeElement as HTMLElement; if (isFullscreen() && active && active.getClientRects().length === 0) hit.focus(); reveal();
      measureDetails();
    }
    var lastPlayLabel = ''; var lastMuted: boolean | null = null;
    function update(): void {
      var paused = media.paused || media.ended; var playLabel = media.ended ? 'پخش دوباره' : paused ? 'پخش' : 'مکث';
      if (lastPlayLabel !== playLabel) { play.setAttribute('aria-label', playLabel); play.setAttribute('data-tooltip', playLabel + ' (K)'); play.setAttribute('title', playLabel); hit.setAttribute('aria-label', playLabel); setIcon(play, paused ? 'play' : 'pause'); setIcon(center, paused ? 'play' : 'pause'); lastPlayLabel = playLabel; }
      container.classList.toggle('media-paused', paused);
      var muted = media.muted || media.volume === 0;
      if (lastMuted !== muted) { mute.setAttribute('aria-label', muted ? 'وصل کردن صدا' : 'بی‌صدا کردن'); mute.setAttribute('data-tooltip', muted ? 'وصل کردن صدا (M)' : 'بی‌صدا کردن (M)'); setIcon(mute, muted ? 'muted' : 'volume'); lastMuted = muted; }
      if (document.activeElement !== volume) volume.value = String(muted ? 0 : media.volume);
      volume.setAttribute('aria-valuetext', Math.round((muted ? 0 : media.volume) * 100) + ' درصد'); volume.style.setProperty('--seek-fill', ((muted ? 0 : media.volume) * 100) + '%');
      var duration = isFinite(media.duration) && media.duration > 0 ? media.duration : 0;
      slider.max = String(Math.floor(duration)); if (document.activeElement !== slider) slider.value = String(Math.floor(media.currentTime || 0));
      slider.disabled = !duration || media.seekable.length === 0; rewind.disabled = slider.disabled; forward.disabled = slider.disabled;
      slider.setAttribute('aria-valuetext', time(media.currentTime) + ' از ' + time(duration));
      slider.style.setProperty('--seek-fill', (duration ? media.currentTime / duration * 100 : 0) + '%');
      var bufferEnd = 0; for (var index = 0; index < media.buffered.length; index++) { if (media.buffered.start(index) <= media.currentTime) bufferEnd = media.buffered.end(index); }
      slider.style.setProperty('--seek-buffer', (duration ? bufferEnd / duration * 100 : 0) + '%'); clock.textContent = time(media.currentTime) + ' / ' + time(duration);
      if (paused) reveal();
    }
    on(hit, 'click', function (event) {
      var click = event as MouseEvent; window.clearTimeout(clickTimer);
      if (click.detail === 0) toggle(); else if (click.detail === 1) clickTimer = window.setTimeout(toggle, 220);
    });
    on(hit, 'dblclick', function (event) { if (video) { event.preventDefault(); window.clearTimeout(clickTimer); toggleFullscreen(); } });
    // Retain the same behavior for direct synthetic/native media clicks, without handling native controls.
    on(media, 'click', function () { if (!media.controls) toggle(); });
    on(hit, 'keydown', function (event) {
      var key = event as KeyboardEvent;
      if (/^Arrow/.test(key.key) || key.keyCode >= 37 && key.keyCode <= 40) { key.preventDefault(); keyboard = true; reveal(); play.focus(); }
    });
    on(slider, 'input', function () { seekTo(Number(slider.value)); });
    on(slider, 'keydown', function (event) { var key = event as KeyboardEvent; if (key.key === 'Enter' || key.keyCode === 13) { key.preventDefault(); play.focus(); } });
    on(volume, 'input', function () { media.volume = Number(volume.value); media.muted = media.volume === 0; reveal(); });
    on(volume, 'keydown', function (event) { var key = event as KeyboardEvent; if (key.key === 'Enter' || key.keyCode === 13) { key.preventDefault(); play.focus(); } });
    on(bar, 'focusin', function (event) { var controls = bar.querySelectorAll<HTMLElement>('button,input'); for (var index = 0; index < controls.length; index++) controls[index].tabIndex = controls[index] === event.target ? 0 : -1; });
    on(shell, 'mousemove', function () { keyboard = false; reveal(); });
    on(shell, 'touchstart', function () { keyboard = false; reveal(); });
    on(shell, 'focusin', reveal);
    on(shell, 'keydown', function (event) {
      var key = event as KeyboardEvent; if (key.defaultPrevented || key.ctrlKey || key.metaKey || key.altKey) return;
      keyboard = true; reveal(); var target = key.target as HTMLElement;
      if (media.controls || /^(INPUT|SELECT|TEXTAREA)$/.test(target.tagName) || target.isContentEditable) return;
      var letter = (key.key || '').toLowerCase();
      if (letter === 'k') { key.preventDefault(); if (!key.repeat) toggle(); }
      else if (letter === 'f' && video) { key.preventDefault(); if (!key.repeat) toggleFullscreen(); }
      else if (letter === 'm') { key.preventDefault(); media.muted = !media.muted; }
      else if (letter === 'j') { key.preventDefault(); seekTo(media.currentTime - 10); }
      else if (letter === 'l') { key.preventDefault(); seekTo(media.currentTime + 10); }
    });
    ['loadedmetadata', 'durationchange', 'timeupdate', 'progress', 'play', 'pause', 'ended', 'volumechange'].forEach(function (event) { on(media, event, update); });
    on(media, 'play', reveal);
    on(media, 'waiting', function () { waiting = true; loader.hidden = false; reveal(); });
    on(media, 'playing', function () { waiting = false; loader.hidden = true; status.textContent = ''; scheduleHide(); });
    on(media, 'pause', function () { waiting = false; loader.hidden = true; reveal(); });
    on(media, 'error', function () { waiting = false; loader.hidden = true; status.textContent = 'پخش فایل در این مرورگر انجام نشد.'; reveal(); });
    on(document, 'fullscreenchange', fullscreenChanged); on(document, 'webkitfullscreenchange', fullscreenChanged);
    if (!video && !container.querySelector('.audio-cover')) { artwork = document.createElement('div'); artwork.className = 'audio-cover'; artwork.setAttribute('aria-hidden', 'true'); artwork.textContent = '♫'; container.appendChild(artwork); }
    container.appendChild(hit); container.appendChild(loader); container.appendChild(bar); container.classList.add('media-enhanced'); container.classList.toggle('media-video', video); container.classList.toggle('media-audio', !video);
    media.controls = false; updateNative(); update(); fullscreenChanged();
    if (details && typeof ResizeObserver !== 'undefined') { detailsObserver = new ResizeObserver(measureDetails); detailsObserver.observe(details); }
    return function () {
      disposed = true; window.clearTimeout(idleTimer); window.clearTimeout(clickTimer); window.clearTimeout(feedbackTimer);
      if (detailsObserver) detailsObserver.disconnect();
      listeners.forEach(function (listener) { listener.target.removeEventListener(listener.type, listener.fn); });
      [bar, hit, loader].forEach(function (node) { if (node.parentNode) node.parentNode.removeChild(node); });
      if (artwork && artwork.parentNode) artwork.parentNode.removeChild(artwork);
      container.classList.remove('media-enhanced', 'media-native', 'media-video', 'media-audio', 'media-paused'); shell.classList.remove('player-idle', 'player-feedback'); media.controls = true;
    };
  }};
})();
