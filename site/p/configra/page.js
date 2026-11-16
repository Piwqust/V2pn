(() => {
  // The texts in both languages and the download links, from the page itself (a strict CSP allows no inline script).
  const data = JSON.parse(document.getElementById("page-data").textContent);
  const T = data.text, U = data.urls;
  const ua = navigator.userAgent || "";
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
  // An iPad asks for the desktop site and says "Macintosh"; its touch screen gives it away.
  const phone = /Android|iPhone|iPad|iPod|Mobile|Windows Phone/i.test(ua) || (/Mac/i.test(platform) && navigator.maxTouchPoints > 1);
  const os = phone ? "phone" : /Win/i.test(platform) || /Windows/i.test(ua) ? "win" : /Mac/i.test(platform) || /Mac OS X/i.test(ua) ? "mac" : "any";
  let arm = false;
  const body = document.body;
  body.classList.remove("os-any");
  body.classList.add("os-" + os);
  if (phone) body.classList.add("is-phone");
  const win = document.getElementById("win"), mac = document.getElementById("mac");
  // The visitor's system first and as the strong button; elsewhere Windows leads, as it is the larger part.
  if (os === "mac") {
    win.classList.remove("primary"); mac.classList.add("primary");
    const dl = document.querySelector(".dl"); dl.insertBefore(dl.querySelector('[data-dl="mac"]'), dl.firstChild);
  }
  let lang = new URLSearchParams(location.search).get("lang") === "en" ? "en" : "ru";
  const apply = () => {
    const s = T[lang];
    document.documentElement.lang = lang;
    document.title = s.title.replace(/&amp;/g, "&");
    document.querySelectorAll("[data-t]").forEach(el => {
      let key = el.dataset.t;
      if (key === "winNote" && arm) key = "winNoteArm";
      el.innerHTML = s[key];
    });
    document.querySelector(".switch").setAttribute("aria-label", s.lang);
    document.querySelectorAll(".switch button").forEach(b => b.setAttribute("aria-pressed", String(b.dataset.lang === lang)));
  };
  document.querySelectorAll(".switch button").forEach(b => b.addEventListener("click", () => {
    lang = b.dataset.lang;
    const url = new URL(location.href);
    if (lang === "en") url.searchParams.set("lang", "en"); else url.searchParams.delete("lang");
    history.replaceState(null, "", url);
    apply();
  }));
  if (os === "win" && navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(["architecture"]).then(v => {
      if (v.architecture === "arm") { arm = true; win.href = U.winArm; apply(); }
    }).catch(() => {});
  }
  const share = document.getElementById("share");
  share.addEventListener("click", async () => {
    const url = location.href;
    try {
      if (navigator.share) { await navigator.share({ title: document.title, url }); return; }
    } catch (e) { if (e && e.name === "AbortError") return; }
    try { await navigator.clipboard.writeText(url); } catch (e) {
      const ta = document.createElement("textarea"); ta.value = url; document.body.appendChild(ta); ta.select();
      try { document.execCommand("copy"); } catch (_) {}
      ta.remove();
    }
    share.textContent = T[lang].copied;
    setTimeout(() => { share.textContent = T[lang].share; }, 2400);
  });
  apply();
})();
