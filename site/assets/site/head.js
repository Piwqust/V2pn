// The head lines of the kit (starter.html, moments.md) as a file of their own: the site runs under
// script-src 'self', which allows no inline script. Load it plainly (not deferred) after the stylesheets.
document.documentElement.classList.add('m-enter');
setTimeout(() => document.documentElement.classList.add('m-enter-done'), 700);
document.documentElement.classList.add('mo-js');
setTimeout(() => document.documentElement.classList.add('mo-safe'), 2500);
