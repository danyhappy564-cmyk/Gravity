/* Gravity — 빌드 없는 소개 페이지. 언어 전환(?lang=en, 저장), 사진 탭, 나타나기 효과. */
(() => {
  'use strict';
  const root = document.documentElement;
  const key = 'gravity-site-language';
  const ok = v => v === 'ko' || v === 'en';
  const copy = {
    ko: { title: 'Gravity — SPT 봇 스폰·장비 관리', description: 'SPT 4.1 레이드에 누가, 언제, 어디서, 무엇을 들고 나오는지 웹 화면 한 곳에서 정합니다. 스폰 모듈과 장비 모듈, 설치 방법과 주의할 점을 확인합니다.' },
    en: { title: 'Gravity — Bot spawns & loadouts for SPT', description: 'Decide who shows up in your SPT 4.1 raids, when, where, and with what gear, all from one web page. See the spawn and loadout modules, how to install, and what to know first.' }
  };

  let lang = new URLSearchParams(location.search).get('lang');
  if (!ok(lang)) {
    try { const s = localStorage.getItem(key); if (ok(s)) lang = s; } catch { /* 저장소를 못 쓰는 환경 */ }
  }
  if (!ok(lang)) lang = (navigator.languages?.[0] || navigator.language || 'en').toLowerCase().startsWith('ko') ? 'ko' : 'en';
  root.lang = lang; root.dataset.lang = lang; document.title = copy[lang].title;

  function setLang(next, remember) {
    if (!ok(next)) return;
    lang = next; root.lang = next; root.dataset.lang = next; document.title = copy[next].title;
    for (const s of ['meta[name="description"]', 'meta[property="og:description"]', 'meta[name="twitter:description"]'])
      document.querySelector(s)?.setAttribute('content', copy[next].description);
    for (const s of ['meta[property="og:title"]', 'meta[name="twitter:title"]'])
      document.querySelector(s)?.setAttribute('content', copy[next].title);
    document.querySelectorAll(`[data-alt-${next}]`).forEach(el => el.setAttribute('alt', el.getAttribute(`data-alt-${next}`)));
    document.querySelectorAll(`[data-label-${next}]`).forEach(el => el.setAttribute('aria-label', el.getAttribute(`data-label-${next}`)));
    // 사진은 언어마다 다른 파일(웹 화면 한/영)
    document.querySelectorAll('img[data-src-ko]').forEach(img => {
      const src = img.getAttribute(`data-src-${next}`);
      if (src && img.getAttribute('src') !== src) img.setAttribute('src', src);
    });
    document.querySelectorAll('a[data-href-ko]').forEach(a => a.setAttribute('href', a.getAttribute(`data-href-${next}`)));
    document.querySelectorAll('[data-language]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.language === next)));
    if (remember) { try { localStorage.setItem(key, next); } catch { /* 저장 없이도 동작 */ } }
  }

  function init() {
    document.querySelectorAll('[data-language]').forEach(b => b.addEventListener('click', () => setLang(b.dataset.language, true)));
    setLang(lang);

    // 화면 사진 탭
    const tabs = [...document.querySelectorAll('.shot-tab')];
    const panels = [...document.querySelectorAll('[data-shot]')];
    const pick = id => {
      tabs.forEach(t => t.setAttribute('aria-selected', String(t.dataset.target === id)));
      panels.forEach(p => { p.hidden = p.dataset.shot !== id; });
    };
    tabs.forEach(t => t.addEventListener('click', () => pick(t.dataset.target)));
    tabs.forEach((t, i) => t.addEventListener('keydown', e => {
      if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
      const n = tabs[(i + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
      n.focus(); pick(n.dataset.target);
    }));
    if (tabs[0]) pick(tabs[0].dataset.target);

    // 스크롤 진행 막대·나타나기·현재 구역
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const bar = document.createElement('div'); bar.className = 'progress'; document.body.append(bar);
    const onScroll = () => {
      const h = document.documentElement.scrollHeight - innerHeight;
      bar.style.setProperty('--p', h > 0 ? scrollY / h : 0);
      root.classList.toggle('scrolled', scrollY > 10);
    };
    addEventListener('scroll', onScroll, { passive: true }); onScroll();
    if (!reduced && 'IntersectionObserver' in window) {
      const items = document.querySelectorAll('.card, .section-heading, .shot-frame, .step, .notice, .stat, .credit, .feature-row .copy, .flow li, .faq details');
      items.forEach((el, i) => { el.classList.add('rv'); el.style.setProperty('--d', (i % 4) * 0.06 + 's'); });
      const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } }), { threshold: 0.08 });
      items.forEach(el => io.observe(el));
    }
    const links = [...document.querySelectorAll('.main-nav a')];
    const spy = new IntersectionObserver(es => es.forEach(e => {
      if (e.isIntersecting) links.forEach(a => a.classList.toggle('is-here', a.getAttribute('href') === '#' + e.target.id));
    }), { rootMargin: '-45% 0px -50% 0px' });
    document.querySelectorAll('main section[id]').forEach(s => spy.observe(s));
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
