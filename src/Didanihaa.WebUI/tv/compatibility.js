/* ES5 bootstrap. Keep readable by browsers which cannot parse the module bundle. */
(function () {
  if (!('noModule' in document.createElement('script')) || !window.fetch || !window.Promise || !window.URLSearchParams) {
    window.location.replace('/tv/simple' + window.location.search); return;
  }
  if (!Element.prototype.replaceChildren) {
    Element.prototype.replaceChildren = function () {
      while (this.firstChild) this.removeChild(this.firstChild);
      for (var i = 0; i < arguments.length; i++) this.appendChild(typeof arguments[i] === 'string' ? document.createTextNode(arguments[i]) : arguments[i]);
    };
  }
})();
