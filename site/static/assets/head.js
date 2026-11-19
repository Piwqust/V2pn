// Before the first paint (a file, not inline: the site's CSP allows no inline script). Hidden-until-revealed
// styles wait for html.motion; if the page's script never boots, everything shows after three seconds.
(function () {
  var h = document.documentElement;
  h.classList.add('js');
  if (!matchMedia('(prefers-reduced-motion: reduce)').matches) h.classList.add('motion');
  setTimeout(function () { if (!h.classList.contains('is-booted')) h.classList.remove('motion'); }, 3000);
})();
