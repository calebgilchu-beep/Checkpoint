/* Checkpoint — lógica de la interfaz (sin dependencias). */
'use strict';

// ───────────────────────────── Puente con la app de Windows ─────────────────────────────
const Host = (() => {
  const wv = window.chrome && window.chrome.webview;
  const pending = new Map();
  const listeners = {};
  let seq = 0;
  if (wv) {
    wv.addEventListener('message', ev => {
      const m = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data;
      if (m.reply != null) {
        const p = pending.get(m.reply);
        if (!p) return;
        pending.delete(m.reply);
        m.ok ? p.resolve(m.result) : p.reject(new Error(m.error || 'Error'));
      } else if (m.event) {
        (listeners[m.event] || []).forEach(f => f(m.data));
      }
    });
  }
  return {
    native: !!wv,
    call(cmd, args = {}) {
      if (!wv) return Mock.call(cmd, args);
      const id = ++seq;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        wv.postMessage(JSON.stringify({ id, cmd, args }));
      });
    },
    on(ev, f) { (listeners[ev] ||= []).push(f); },
    emit(ev, data) { (listeners[ev] || []).forEach(f => f(data)); },
    dropFiles(files) { if (wv && wv.postMessageWithAdditionalObjects) wv.postMessageWithAdditionalObjects(JSON.stringify({ cmd: 'drop' }), files); }
  };
})();

// Modo de prueba en un navegador normal (?mock=1): datos de ejemplo, sin disco ni red.
const Mock = {
  settings: { theme: 'system', view: 'grid', covers: false, updateDate: true, writeStoreIds: true, backup: true, hasSteamKey: false, hasXblKey: false, hasRaKey: false, raUser: '', steamId: '', onboarded: true, resume: 'ask',
    markdown: { frontMatter: true, emojis: true, counts: true, summaryTable: true, taskList: true, storeLinks: true, covers: false, collapsible: false, rules: true } },
  async call(cmd, a) {
    const get = async f => (await fetch(f)).text();
    switch (cmd) {
      case 'init': {
        try { Object.assign(Mock.settings, JSON.parse(localStorage.getItem('lj.mock.settings') || '{}')); } catch { }
        const qs = new URLSearchParams(location.search);
        if (qs.get('theme')) Mock.settings.theme = qs.get('theme');
        if (qs.get('view')) Mock.settings.view = qs.get('view');
        if (qs.get('lang')) Mock.settings.language = qs.get('lang');
        if (qs.get('welcome')) Mock.settings.onboarded = false;
        const open = qs.get('mock') !== 'start';
        const doc = open ? JSON.parse(await get('mock/sample.json')) : null;
        return { settings: Mock.settings, recent: [{ path: 'C:\\Users\\Caleb\\Downloads\\Juegos que me he pasado.txt' }], sagas: ['Final Fantasy', 'Halo', 'Persona', 'Resident Evil'],
          file: doc ? { doc, path: 'C:\\Users\\Caleb\\Downloads\\Juegos que me he pasado.txt', kind: 'txt', encoding: 'utf-8', newLine: 'CRLF' } : null, version: 'prueba',
          resume: !doc && Mock.settings.resume === 'ask' ? { path: 'C:\\Users\\Caleb\\Downloads\\Juegos que me he pasado.txt' } : null };
      }
      case 'file.serialize': return { text: await get(a.kind === 'md' ? 'mock/sample.md' : 'mock/sample.txt') };
      case 'file.new': return { doc: { header: `Lista de los juegos que me he pasado (Actualizado el ${today()})`, hasHeader: true, sections: [{ name: 'Completados', implicit: true, blocks: [] }, { name: 'Inconclusos', blocks: [] }, { name: 'Por jugar', blocks: [] }], layout: {}, markdown: {} } };
      case 'settings.set': Object.assign(Mock.settings, a); try { localStorage.setItem('lj.mock.settings', JSON.stringify(Mock.settings)); } catch { } return Mock.settings;
      case 'file.save': return { doc: a.doc, path: 'C:\\prueba\\lista.' + (a.as || 'txt'), kind: a.as || 'txt', encoding: 'utf-8', newLine: 'CRLF' };
      case 'suggest.category': return { name: null };
      case 'secrets.set': if (a.clear) Object.assign(Mock.settings, { hasSteamKey: false, hasXblKey: false, hasSgdbKey: false, hasRaKey: false });
        else { if (a.raUser != null) Mock.settings.raUser = a.raUser; if (a.raKey) Mock.settings.hasRaKey = true; } return Mock.settings;
      case 'next.suggest': {
        // Versión simplificada de NextGame.Suggest (Core) solo para el modo de prueba en el navegador
        const sagaOf = (b) => b.type === 'category' && b.style === 'colon' && b.name ? norm(b.name) : null;
        const played = new Map();
        for (const s of a.doc.sections) {
          const k = sectionKind(s); if (!['completed', 'hundred', 'unfinished', 'playing'].includes(k)) continue;
          for (const b of s.blocks) { const g = sagaOf(b); if (!g) continue; const p = played.get(g) || { done: 0, started: 0, titles: [] };
            for (const e of b.entries || []) { p.titles.push(e.title); ['completed', 'hundred'].includes(k) ? p.done++ : p.started++; } played.set(g, p); }
        }
        const lenOf = h => h == null ? 'any' : h < 8 ? 'short' : h <= 20 ? 'medium' : 'long';
        const picks = [];
        for (const s of a.doc.sections.filter(s => sectionKind(s) === 'backlog')) for (const b of s.blocks) for (const e of b.entries || []) {
          const d = F.ttb(e), p = played.get(sagaOf(b));
          if (a.platform && !splitPlatforms(e.platforms).includes(a.platform)) continue;
          if (a.length !== 'any' && lenOf(d?.main) !== a.length) continue;
          if (a.sagaOnly && !p) continue;
          picks.push({ entryId: e.id, title: e.title, platforms: e.platforms, section: s.name, hours: d?.main ?? null, fullHours: d?.full ?? null,
            saga: p ? b.name : null, sagaDone: p?.done || 0, sagaStarted: p?.started || 0, sagaTitles: p?.titles || [] });
        }
        picks.sort((x, y) => (!!y.saga - !!x.saga) || ((y.sagaDone * 2 + y.sagaStarted) - (x.sagaDone * 2 + x.sagaStarted)) || ((x.hours == null) - (y.hours == null)) || ((x.hours || 0) - (y.hours || 0)));
        return { picks };
      }
      case 'stats.compute': case 'stats.year': {
        // Versión simplificada de LibraryStats (Core) solo para el modo de prueba en el navegador
        const yearOf = d => { const m = /(\d{4})\s*$/.exec((d || '').trim()) || /^(\d{4})/.exec((d || '').trim()); return m ? +m[1] : null; };
        const all = [], done = [];
        for (const s of a.doc.sections) for (const b of s.blocks) for (const e of b.entries || []) { all.push(e); if (isDone(s)) done.push({ s, b, e }); }
        const avg = v => v.length ? v.reduce((n, x) => n + x, 0) / v.length : null;
        if (cmd === 'stats.year') {
          const list = done.filter(x => yearOf(x.e.date) === a.year).map(x => ({ entryId: x.e.id, title: x.e.title, platforms: x.e.platforms, date: x.e.date,
            hours: F.num(x.e, 'hours'), rating: F.num(x.e, 'rating'), hundred: sectionKind(x.s) === 'hundred' }));
          const rated = list.filter(g => g.rating != null);
          return { year: a.year, games: list.length, hours: list.reduce((n, g) => n + (g.hours || 0), 0), rating: avg(rated.map(g => g.rating)), list,
            top: [...rated].sort((x, y) => y.rating - x.rating).slice(0, 3), available: [] };
        }
        const count = arr => { const m = new Map(); for (const n of arr) if (n) { const k = norm(n); const v = m.get(k) || { name: n, games: 0 }; v.games++; m.set(k, v); }
          return [...m.values()].sort((x, y) => y.games - x.games || x.name.localeCompare(y.name)); };
        const ys = new Map();
        for (const x of done) { const y = yearOf(x.e.date); if (!y) continue; const r = ys.get(y) || { year: y, games: 0, hours: 0, rating: null, rated: 0 }; r.games++; r.hours += F.num(x.e, 'hours') || 0; ys.set(y, r); }
        const ks = [...ys.keys()].sort(), years = [];
        if (ks.length) for (let y = ks[0]; y <= ks.at(-1); y++) years.push(ys.get(y) || { year: y, games: 0, hours: 0, rating: null, rated: 0 });
        const hrs = all.map(e => F.num(e, 'hours')).filter(v => v != null), rts = all.map(e => F.num(e, 'rating')).filter(v => v != null);
        const dated = years.reduce((n, y) => n + y.games, 0);
        return { total: all.length, completed: done.length, dated, undated: done.length - dated, hours: hrs.reduce((n, v) => n + v, 0), withHours: hrs.length,
          rating: avg(rts), rated: rts.length, years, platforms: count(done.map(x => splitPlatforms(x.e.platforms)[0])),
          noPlatform: done.filter(x => !splitPlatforms(x.e.platforms).length).length,
          sagas: count(done.map(x => x.b.type === 'category' && x.b.style === 'colon' ? x.b.name : null)) };
      }
      case 'export.png': return { path: 'C:\\prueba\\' + a.name };
      case 'ttb.lookup': return { results: [{ id: 1, name: a.title, year: 2017, platforms: ['PC', 'Switch'], main: 27, full: 63, count: 124, exact: true, field: 'ttb: 27h / 63h' }] };
      case 'ttb.fill': { // datos de ejemplo: 12 h a todos los que no tienen duración
        const s = a.doc.sections[a.section];
        const todo = s.blocks.flatMap(b => b.entries || []).filter(e => !F.ttb(e) && e.title);
        return { changes: todo.map(e => ({ entryId: e.id, title: e.title, field: 'ttb: 12h', igdbName: e.title })), notFound: [], unsure: [], skipped: 0 };
      }
      case 'igdb.check': return { ok: true };
      case 'window.fullscreen': return { on: a.on ?? !S.fullScreen };
      case 'import.scan': {
        // Datos de ejemplo de RetroAchievements y de los emuladores (sin red ni disco)
        const ago = d => new Date(Date.now() - d * 864e5).toISOString();
        const all = [
          { source: 'retroachievements', title: 'Super Mario World', storeKey: 'ra', storeId: '228', platform: 'SNES', note: 'emulación', achievements: 100, hardcore: 100, lastPlayed: ago(90), suggestedSection: 'Completados al 100%', suggestedCategory: 'Super Mario', selected: true },
          { source: 'retroachievements', title: 'The Legend of Zelda: A Link to the Past', storeKey: 'ra', storeId: '1', platform: 'SNES', note: 'emulación', achievements: 42, hardcore: 30, lastPlayed: ago(3), suggestedSection: 'Jugando ahora', suggestedCategory: 'The Legend of Zelda', selected: true },
          { source: 'emu-PS1', title: 'Final Fantasy VII', platform: 'PS1', note: 'emulación', installed: true, hours: 12.5, lastPlayed: ago(60), suggestedSection: 'Inconclusos', suggestedCategory: 'Final Fantasy', selected: true },
          { source: 'emu-GameCube', title: 'Metroid Prime', platform: 'GameCube', note: 'emulación', installed: true, suggestedSection: 'Por jugar', suggestedCategory: 'Metroid', selected: true }
        ].filter(c => (a.sources.includes('raOnline') && c.source === 'retroachievements') || (a.sources.includes('emuLocal') && c.source.startsWith('emu-')));
        return { candidates: all, messages: a.sources.includes('emuLocal') ? [{ source: 'emuladores', ok: true, message: 'Emuladores: RetroArch: 1 juego · Dolphin: 1 juego.' }] : [] };
      }
      case 'covers.resolve': {
        const have = ['2050650', '1113000', '391540', '268910', '418370'];
        setTimeout(() => a.items.forEach(it => { if (have.includes(it.steam)) Host.emit('cover', { key: it.key, url: `mock/covers/steam_${it.steam}.jpg` }); }), 50);
        return null;
      }
      default: return null;
    }
  }
};

// ───────────────────────────── Utilidades ─────────────────────────────
// append/replaceChildren ignoran null y false (para poder escribir «cond ? el : null»).
for (const proto of [Element.prototype, DocumentFragment.prototype]) {
  for (const m of ['append', 'replaceChildren']) {
    const orig = proto[m];
    proto[m] = function (...kids) { return orig.apply(this, kids.flat().filter(k => k != null && k !== false)); };
  }
}
const $ = s => document.querySelector(s);
// Atributos que lee la persona (o el lector de pantalla): se traducen igual que el texto (i18n.js)
const TR_ATTRS = new Set(['title', 'aria-label', 'placeholder']);
function h(tag, props = {}, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = tr(v);
    else if (k === 'html') el.innerHTML = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'dataset') Object.assign(el.dataset, v);
    else if (v === true) el.setAttribute(k, '');
    else el.setAttribute(k, TR_ATTRS.has(k) ? tr(String(v)) : v);
  }
  for (const c of kids.flat()) if (c != null && c !== false) el.append(c instanceof Node ? c : document.createTextNode(tr(String(c))));
  return el;
}
function icon(name, cls = 'i') {
  const ns = 'http://www.w3.org/2000/svg';
  const s = document.createElementNS(ns, 'svg');
  s.setAttribute('class', cls); s.setAttribute('aria-hidden', 'true');
  const u = document.createElementNS(ns, 'use'); u.setAttribute('href', `icons/sprite.svg#${name}`);
  s.append(u); return s;
}
const norm = s => (s || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
const uid = () => Math.random().toString(36).slice(2, 14);
const pad = n => String(n).padStart(2, '0');
function today() { const d = new Date(); return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`; }
function dmyToIso(s) { const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec((s || '').trim()); return m ? `${m[3]}-${pad(m[2])}-${pad(m[1])}` : null; }
function isoToDmy(s) { const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || ''); return m ? `${m[3]}/${m[2]}/${m[1]}` : null; }
const fmtNum = v => (Math.abs(v - Math.round(v)) < 0.05 ? String(Math.round(v)) : (Math.round(v * 10) / 10).toString().replace('.', ','));
const parseNum = s => { const v = parseFloat(String(s).replace(',', '.')); return isFinite(v) ? v : null; };
const plural = (n, a, b) => `${n} ${n === 1 ? a : b}`;
const initials = t => ((t || '?').match(/[\p{L}\p{N}]+/gu) || ['?']).slice(0, 2).map(w => w[0].toUpperCase()).join('');

// Campos {…} — mismo criterio que ListaJuegos.Core.Fields
const RX = {
  rating: /^★\s*(\d+(?:[.,]\d+)?)\s*\/\s*10$/,
  hours: /^(\d+(?:[.,]\d+)?)\s*h$/i,
  ach: /^Logros\s*(\d+(?:[.,]\d+)?)\s*%$/i,
  store: /^(steam|xbox|epic|gog|psn|nintendo|ea|ubisoft|ra):\s*(\S+)$/i,
  sgdb: /^sgdb:\s*(\d+)$/i,
  // {ttb: 12h} o {ttb: 12h / 35h}: duración estimada (historia principal / completista)
  ttb: /^ttb\s*:\s*(\d+(?:[.,]\d+)?)\s*h?(?:\s*\/\s*(\d+(?:[.,]\d+)?)\s*h?)?$/i,
  kv: /^([^:]{1,40}):\s*(.+)$/
};
const F = {
  kind(f) { f = f.trim(); for (const k of ['rating', 'hours', 'ach', 'store', 'sgdb', 'ttb']) if (RX[k].test(f)) return k; return RX.kv.test(f) ? 'kv' : 'tag'; },
  ttb(e) { for (const f of e.fields || []) { const m = RX.ttb.exec(f.trim()); if (m) return { main: parseNum(m[1]), full: m[2] ? parseNum(m[2]) : null }; } return null; },
  makeTtb(main, full) { return full > 0 ? `ttb: ${fmtNum(main)}h / ${fmtNum(full)}h` : `ttb: ${fmtNum(main)}h`; },
  num(e, k) { for (const f of e.fields || []) { const m = RX[k].exec(f.trim()); if (m) return parseNum(m[1]); } return null; },
  // {sgdb:5245268}: id de juego de SteamGridDB para la portada
  sgdb(e) { for (const f of e.fields || []) { const m = RX.sgdb.exec(f.trim()); if (m) return m[1]; } return null; },
  store(e) { for (const f of e.fields || []) { const m = RX.store.exec(f.trim()); if (m) return { store: m[1].toLowerCase(), id: m[2] }; } return null; },
  make(k, v) { return k === 'rating' ? `★ ${fmtNum(v)}/10` : k === 'hours' ? `${fmtNum(v)} h` : `Logros ${fmtNum(v)}%`; },
  set(e, k, value) {
    e.fields ||= [];
    const i = e.fields.findIndex(f => F.kind(f) === k);
    if (value == null) { if (i >= 0) e.fields.splice(i, 1); return; }
    if (i >= 0) { e.fields[i] = value; return; }
    const si = e.fields.findIndex(f => F.kind(f) === 'store');
    if (k !== 'store' && si >= 0) e.fields.splice(si, 0, value); else e.fields.push(value);
  },
  others(e) { return (e.fields || []).filter(f => ['kv', 'tag'].includes(F.kind(f))); }
};
function storeUrl(st) {
  if (!st) return null;
  if (st.store === 'steam') return `https://store.steampowered.com/app/${st.id}`;
  if (st.store === 'xbox' && /^[A-Za-z0-9]{12}$/.test(st.id)) return `https://www.microsoft.com/store/productId/${st.id}`;
  if (st.store === 'ra' && /^\d+$/.test(st.id)) return `https://retroachievements.org/game/${st.id}`;
  return null;
}

// Tipos de sección — mismo criterio que ListaJuegos.Core.SectionKinds
function sectionKind(s) {
  if (s.implicit) return 'completed';
  const n = norm(s.name);
  if (n.includes('100') || n.includes('platin')) return 'hundred';
  if (n.includes('jugando') || n.includes('en curso') || n.includes('playing') || n.includes('jogando')) return 'playing';
  if (n.includes('nclus') || n.includes('sin terminar') || n.includes('a medias') || n.includes('unfinish') || n.includes('inacab')) return 'unfinished';
  if (n.includes('abandon') || n.includes('dropped')) return 'abandoned';
  if (n.includes('deseo') || n.includes('wish') || n.includes('quiero') || n.includes('desejo')) return 'wishlist';
  if (n.includes('por jugar') || n.includes('pendiente') || n.includes('backlog') || n.includes('para jogar') || n.includes('pendente')) return 'backlog';
  if (n.includes('pasad') || n.includes('complet') || n.includes('terminad') || n.includes('acabad') || n.includes('conclu')) return 'completed';
  return 'custom';
}
const KIND_ICON = { completed: 'trophy', hundred: 'sparkles', playing: 'gamepad-2', unfinished: 'pause', backlog: 'list-todo', abandoned: 'ban', wishlist: 'heart', custom: 'folder' };
const isDone = s => ['completed', 'hundred'].includes(sectionKind(s));
const SUGGESTED_SECTIONS = ['Completados', 'Completados al 100%', 'Jugando ahora', 'Inconclusos', 'Por jugar', 'Abandonados', 'Lista de deseos'];

// Plataformas → icono real (Xbox y Nintendo van en texto: no hay marca con licencia permitida)
function platformIcon(p) {
  const n = norm(p);
  if (n.includes('steam')) return { src: 'icons/steam.svg', ink: true };
  if (n.includes('epic')) return { src: 'icons/epic-games.svg', ink: true };
  if (n.includes('gog')) return { src: 'icons/gog.svg', ink: true };
  if (/^ps|playstation|ps vita|psp/.test(n)) return { src: 'icons/playstation.svg', ink: true };
  if (n === 'ea' || n.includes('ea app') || n.includes('origin')) return { src: 'icons/ea.svg', ink: true };
  if (n.includes('ubisoft') || n.includes('uplay')) return { src: 'icons/ubisoft.svg', ink: true };
  return null;
}
// Familia de cada plataforma → color de su etiqueta (app.css, [data-fam])
function platformFamily(p) {
  const n = norm(p);
  if (/switch|nintendo|\b3ds\b|\bds\b|\bwii|gamecube|\bgba\b|\bgbc?\b|snes|\bnes\b|n64|game ?boy/.test(n)) return 'nintendo';
  if (/^ps|playstation|psp|vita/.test(n)) return 'playstation';
  // las portátiles Windows (ROG Ally, Legion Go) comparten el verde esmeralda de Xbox
  if (/xbox|game ?pass|rog ally|legion go/.test(n)) return 'xbox';
  if (/\bpc\b|steam|epic|gog|windows|ea app|origin|ubisoft|uplay|battle\.?net|linux|mac/.test(n)) return 'pc';
  if (/android|ios|iphone|ipad|movil|mobile/.test(n)) return 'mobile';
  return 'other';
}
const mainFamily = e => platformFamily(splitPlatforms(e.platforms)[0] || '');
// Transiciones de entrada y salida (View Transitions de Chromium). Sin soporte o con «reducir movimiento», cambio directo.
let vtSeq = 0;
function transition(kind, fn) {
  const root = document.documentElement;
  if (!document.startViewTransition || root.dataset.motion === 'off' || matchMedia('(prefers-reduced-motion: reduce)').matches) { fn(); return; }
  const n = ++vtSeq;
  root.dataset.vt = kind;
  document.startViewTransition(fn).finished.finally(() => { if (n === vtSeq) { delete root.dataset.vt; delete root.dataset.vtDir; } });
}
const splitPlatforms = s => (s || '').split('/').map(x => x.trim()).filter(Boolean);

// ───────────────────────────── Estado ─────────────────────────────
const S = {
  doc: null, path: null, kind: 'txt', encoding: 'utf-8', newLine: 'CRLF',
  settings: {}, recent: [], sagas: [],
  sec: 0, selected: null, query: '', platform: '', textMode: false, textKind: 'txt', textEditing: false,
  undo: [], redo: [], saved: null, collapsed: new Set(),
  covers: new Map(), coverReq: new Set(), suggestion: null
};
const snapshot = () => JSON.stringify(S.doc);
const isDirty = () => S.doc != null && snapshot() !== S.saved;
function commit(before, label) {
  if (before === snapshot()) return;
  S.undo.push({ doc: before, sel: S.selected, sec: S.sec, label });
  if (S.undo.length > 200) S.undo.shift();
  S.redo = [];
  dirtyChanged();
}
function mutate(label, fn) {
  const before = snapshot();
  const r = fn();
  commit(before, label);
  render();
  return r;
}
function undo() {
  const u = S.undo.pop(); if (!u) return;
  S.redo.push({ doc: snapshot(), sel: S.selected, sec: S.sec, label: u.label });
  S.doc = JSON.parse(u.doc); S.selected = u.sel; S.sec = Math.min(u.sec, S.doc.sections.length - 1);
  render(); dirtyChanged(); announce(`Deshecho: ${u.label}`);
}
function redo() {
  const r = S.redo.pop(); if (!r) return;
  S.undo.push({ doc: snapshot(), sel: S.selected, sec: S.sec, label: r.label });
  S.doc = JSON.parse(r.doc); S.selected = r.sel; S.sec = Math.min(r.sec, S.doc.sections.length - 1);
  render(); dirtyChanged(); announce(`Rehecho: ${r.label}`);
}
let lastDirty = null;
function dirtyChanged() {
  const d = isDirty();
  $('#btnUndo').disabled = !S.undo.length;
  $('#btnRedo').disabled = !S.redo.length;
  renderFileInfo();
  if (d !== lastDirty) { lastDirty = d; Host.call('state.dirty', { dirty: d, title: tr(fileTitle()) }); }
}

// Recorrer el documento
const sections = () => S.doc?.sections || [];
const curSection = () => sections()[S.sec];
function* allEntries() { for (const s of sections()) for (const b of s.blocks) for (const e of b.entries || []) yield { s, b, e }; }
function findEntry(id) { for (const x of allEntries()) if (x.e.id === id) return x; return null; }
function categoriesOf(s) { return s.blocks.filter(b => b.type === 'category'); }
function sectionCount(s) { return s.blocks.reduce((n, b) => n + (b.entries?.length || 0), 0); }
function allPlatforms() {
  const m = new Map();
  for (const { e } of allEntries()) for (const p of splitPlatforms(e.platforms)) m.set(p, (m.get(p) || 0) + 1);
  for (const p of ['PC', 'Steam', 'Nintendo Switch', 'PS5', 'PS4', 'PS Vita', 'Xbox Series', 'Xbox PC', 'Epic Games', 'GOG', 'ROG Ally X', 'Steam Deck', '3DS'])
    if (!m.has(p)) m.set(p, 0);
  return [...m.entries()].sort((a, b) => b[1] - a[1]).map(x => x[0]);
}
function blockLabel(b) { return b.style === 'paren' ? `(${b.name})` : b.style === 'none' ? 'Sin categoría' : b.name; }

// ───────────────────────────── Avisos ─────────────────────────────
function announce(msg) { msg = tr(msg); const l = $('#live'); l.textContent = ''; setTimeout(() => (l.textContent = msg), 30); $('#statusLeft').textContent = msg; }
let toastTimer;
function toast(msg, action) {
  const t = $('#toast');
  t.replaceChildren(h('span', { text: msg }));
  if (action) t.append(h('button', { class: 'btn btn--ghost btn--sm', onclick: () => { t.hidden = true; action.fn(); } }, action.label));
  t.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (t.hidden = true), 6000);
  announce(msg);
}

// ───────────────────────────── Renderizado ─────────────────────────────
function fileTitle() { return S.path ? S.path.split(/[\\/]/).pop() : (S.doc ? 'Lista nueva' : 'Checkpoint'); }
function renderFileInfo() {
  const name = $('#fileName');
  name.replaceChildren(tr(fileTitle()));
  name.title = S.path || fileTitle();
  $('#fileInfo').title = S.path || '';
  if (isDirty()) name.append(h('span', { class: 'dirty-dot', title: 'Cambios sin guardar' }), h('span', { class: 'sr-only', text: ' (cambios sin guardar)' }));
  $('#fileInfo').textContent = S.doc ? (S.path ? S.path.replace(/[\\/][^\\/]*$/, '') : tr('Todavía sin guardar')) : tr('Sin archivo abierto');
  $('#statusRight').textContent = S.doc ? `${tr(S.kind === 'md' ? 'Markdown (.md)' : 'Texto (.txt)')} · ${S.encoding.toUpperCase()} · ${S.newLine}` : '';
}

function render() {
  const has = !!S.doc;
  document.body.classList.toggle('no-doc', !has);
  for (const id of ['#docHead', '#tabsBar']) $(id).hidden = !has;
  ['#btnSave', '#btnSaveAs', '#btnText', '#btnStats', '#btnImport', '#q'].forEach(s => { $(s).disabled = !has; });
  renderFileInfo();
  if (!has) { renderTtbStatus(); renderStart(); return; }
  if (S.sec >= sections().length) S.sec = Math.max(0, sections().length - 1);
  renderHead();
  renderTabs();
  $('#btnText').setAttribute('aria-pressed', String(!!S.textMode));
  $('#btnStats').setAttribute('aria-pressed', String(!!S.statsMode));
  if (S.statsMode) renderStats(); else if (S.textMode) renderText(); else renderList();
  renderDetail();
  const total = [...allEntries()].length;
  const done = sections().filter(isDone).reduce((n, s) => n + sectionCount(s), 0);
  if (!$('#statusLeft').textContent || $('#statusLeft').dataset.auto) {
    $('#statusLeft').textContent = tr(`${plural(total, 'juego', 'juegos')} · ${done} completados`);
    $('#statusLeft').dataset.auto = '1';
  }
  renderTtbStatus();
  queueCovers();
}

// Barra de estado de «Por jugar»: horas estimadas de los pendientes que tienen {ttb: …}
function pendingHours(s) {
  let hours = 0, withTtb = 0, count = 0;
  for (const b of s.blocks) for (const e of b.entries || []) { count++; const d = F.ttb(e); if (d) { hours += d.main; withTtb++; } }
  return { hours, withTtb, count };
}
function renderTtbStatus() {
  const el = $('#statusTtb');
  const s = S.doc && !S.textMode && !S.statsMode ? curSection() : null;
  const p = s && sectionKind(s) === 'backlog' ? pendingHours(s) : null;
  el.hidden = !p || !p.withTtb;
  el.textContent = p && p.withTtb ? tr(`⌛ ${fmtNum(p.hours)} h estimadas · ${p.withTtb} de ${p.count} con duración`) : '';
}

function renderHead() {
  const inp = $('#headerInput');
  const m = /\s*\(Actualizado el (\d{1,2}\/\d{1,2}\/\d{2,4})\)\s*/i.exec(S.doc.header || '');
  const title = m ? (S.doc.header.slice(0, m.index) + ' ' + S.doc.header.slice(m.index + m[0].length)).trim() : (S.doc.header || '');
  if (document.activeElement !== inp) inp.value = title;
  $('#docH1').textContent = title || 'Checkpoint';
  inp.dataset.updated = m ? m[1] : '';
  const meta = $('#docMeta');
  meta.replaceChildren(
    m ? h('span', { class: 'chip' }, icon('calendar', 'i i--sm'), `Actualizado el ${m[1]}`) : h('span', { class: 'chip chip--muted' }, 'Sin fecha de actualización'),
    S.settings.updateDate ? h('span', { class: 'chip chip--muted' }, icon('refresh-cw', 'i i--sm'), 'La fecha se pone al día al guardar') : null
  );
  const all = [...allEntries()];
  const total = all.length;
  const done = sections().filter(isDone).reduce((n, s) => n + sectionCount(s), 0);
  const hours = all.reduce((n, x) => n + (F.num(x.e, 'hours') || 0), 0);
  const pending = sections().filter(s => ['unfinished', 'backlog', 'playing'].includes(sectionKind(s))).reduce((n, s) => n + sectionCount(s), 0);
  $('#stats').replaceChildren(
    statEl('Completados', done, total, 'done', `${done} de ${total} juegos completados`),
    statEl('Pendientes', pending, total, 'pending', `${pending} de ${total} juegos pendientes`),
    hours ? statEl('Horas', fmtNum(hours), null, 'hours', `${fmtNum(hours)} horas anotadas`) : null
  );
}
// Tarjeta de estadística: etiqueta, porcentaje, número en monoespaciada y una barra fina con la proporción
function statEl(label, n, total, kind, sr) {
  const pct = total ? Math.round(n * 100 / total) : null;
  return h('div', { class: `stat stat--${kind}` },
    h('span', { class: 'sr-only', text: pct != null ? `${sr} (${pct} %)` : sr }),
    h('div', { class: 'stat__top', 'aria-hidden': 'true' }, h('span', { text: label }), pct != null ? h('span', { class: 'stat__pct', text: `${pct} %` }) : null),
    h('div', { class: 'stat__num', 'aria-hidden': 'true' }, h('b', { text: n }), h('small', { text: total != null ? `/${total}` : 'h' })),
    pct != null ? h('div', { class: 'stat__bar', 'aria-hidden': 'true' }, h('i', { style: `width:${pct}%` })) : null);
}

function renderTabs() {
  const tabs = $('#tabs');
  const q = S.query.trim();
  tabs.replaceChildren(...sections().map((s, i) => {
    const count = q ? s.blocks.reduce((n, b) => n + (b.entries || []).filter(e => matches(e, b, q)).length, 0) : sectionCount(s);
    const t = h('button', {
      class: 'tab', role: 'tab', id: `tab-${i}`, 'aria-selected': String(i === S.sec && !S.textMode && !S.statsMode), 'aria-controls': 'main',
      tabindex: i === S.sec ? '0' : '-1', onclick: () => selectSection(i),
      ondragover: ev => { if (dragId) { ev.preventDefault(); t.classList.add('drop-target'); } },
      ondragleave: () => t.classList.remove('drop-target'),
      ondrop: ev => { ev.preventDefault(); t.classList.remove('drop-target'); if (dragId) moveToSection(dragId, i); },
      oncontextmenu: ev => { ev.preventDefault(); tabMenu(i, ev); }
    }, icon(KIND_ICON[sectionKind(s)], 'i i--sm'), s.name || '(sin nombre)', h('span', { class: 'count', 'aria-label': plural(count, 'juego', 'juegos') }, String(count)),
      // raya de acento de la pestaña activa: al cambiar de pestaña se desliza a la nueva (View Transitions, app.css)
      i === S.sec && !S.textMode && !S.statsMode ? h('span', { class: 'tab__ind', 'aria-hidden': 'true' }) : null);
    return t;
  }));
}

function selectSection(i, focusTab = false) {
  if (i !== S.sec) document.documentElement.dataset.vtDir = i < S.sec ? 'back' : 'fwd';
  S.sec = i; S.textMode = false; S.statsMode = false; S.platform = '';
  $('#btnText').setAttribute('aria-pressed', 'false');
  const first = sections()[i].blocks.flatMap(b => b.entries || [])[0];
  if (!findEntryIn(sections()[i], S.selected)) S.selected = first?.id || null;
  transition('tab', () => { render(); if (focusTab) $(`#tab-${i}`)?.focus(); });
}
const findEntryIn = (s, id) => id && s.blocks.some(b => (b.entries || []).some(e => e.id === id));

function matches(e, b, q) {
  const n = norm(q);
  return [e.title, e.platforms, e.note, e.date, b.name, ...(e.fields || [])].some(x => norm(x).includes(n));
}

function renderList() {
  const main = $('#main');
  const s = curSection();
  const q = S.query.trim();
  const view = S.settings.view === 'list' ? 'list' : 'grid';
  main.setAttribute('role', 'tabpanel');
  main.setAttribute('aria-labelledby', `tab-${S.sec}`);
  const frag = document.createDocumentFragment();

  // Herramientas
  const plats = [...new Set(s.blocks.flatMap(b => (b.entries || []).flatMap(e => splitPlatforms(e.platforms))))].sort();
  const platSel = h('select', { class: 'select', id: 'platFilter', 'aria-label': 'Filtrar por plataforma', onchange: ev => { S.platform = ev.target.value; renderList(); } },
    h('option', { value: '' }, 'Todas las plataformas'), ...plats.map(p => h('option', { value: p, selected: p === S.platform }, p)));
  frag.append(h('div', { class: 'listtools' },
    h('button', { class: 'btn btn--secondary', onclick: () => addEntry(), title: 'Añadir juego (Insert)', 'aria-keyshortcuts': 'Insert' }, icon('plus'), 'Añadir juego'),
    h('button', { class: 'btn btn--ghost', onclick: () => addCategory() }, icon('folder-plus'), 'Nueva saga o género'),
    sectionKind(s) === 'backlog' && !q ? h('button', { class: 'btn btn--secondary', id: 'btnNext', onclick: openNext, title: '¿Qué juego ahora? (Ctrl+J)', 'aria-keyshortcuts': 'Control+J' }, icon('sparkles'), '¿Qué juego ahora?') : null,
    sectionKind(s) === 'backlog' && !q ? h('button', { class: 'btn btn--ghost', id: 'btnFillTtb', onclick: fillDurations, title: 'Busca en IGDB la duración de los juegos de esta sección que aún no la tienen' }, icon('clock'), 'Rellenar duraciones') : null,
    h('span', { class: 'grow' }),
    q ? h('span', { class: 'chip' }, icon('search', 'i i--sm'), `Buscando «${q}»`) : null,
    plats.length > 1 && !q ? platSel : null,
    h('div', { class: 'seg', role: 'radiogroup', 'aria-label': 'Vista' },
      h('button', { role: 'radio', 'aria-checked': String(view === 'grid'), title: 'Portadas', 'aria-label': 'Vista de portadas', onclick: () => setView('grid') }, icon('layout-grid', 'i i--sm')),
      h('button', { role: 'radio', 'aria-checked': String(view === 'list'), title: 'Lista', 'aria-label': 'Vista de lista compacta', onclick: () => setView('list') }, icon('list', 'i i--sm')))
  ));

  if (!S.settings.covers && !S.settings.coversAsked && !q) {
    frag.append(h('div', { class: 'covers-cta', role: 'note' }, icon('image'),
      h('span', null, S.settings.hasSgdbKey ? '¿Quieres ver las portadas? Se buscan los títulos en SteamGridDB, con tu clave, o en la tienda de Steam, y se guardan en este PC.' : '¿Quieres ver las portadas? Se buscan los títulos en la tienda de Steam, sin cuenta, y se guardan en este PC.'),
      h('button', { class: 'btn btn--primary btn--sm', onclick: async () => {
        S.settings = (await Host.call('settings.set', { covers: true, coversAsked: true })) || { ...S.settings, covers: true, coversAsked: true };
        render(); queueCovers(true); announce('Portadas activadas');
      } }, 'Activar portadas'),
      h('button', { class: 'btn btn--ghost btn--sm', onclick: () => { S.settings.coversAsked = true; Host.call('settings.set', { coversAsked: true }); renderList(); } }, 'Ahora no')));
  }

  let shown = 0;
  const sourceSecs = q ? sections().map((sec, i) => ({ sec, i })) : [{ sec: s, i: S.sec }];
  for (const { sec, i } of sourceSecs) {
    sec.blocks.forEach((b, bi) => {
      if (b.type === 'divider') {
        if (!q) frag.append(dividerEl(sec, b, bi));
        return;
      }
      let entries = b.entries || [];
      if (q) entries = entries.filter(e => matches(e, b, q));
      if (S.platform && !q) entries = entries.filter(e => splitPlatforms(e.platforms).includes(S.platform));
      if ((q || S.platform) && !entries.length) return;
      shown += entries.length;
      frag.append(groupEl(sec, i, b, bi, entries, view, !!q));
    });
  }

  if (!shown && !q && !S.platform && !s.blocks.some(b => b.type === 'category' && b.style !== 'none')) {
    frag.append(h('div', { class: 'empty' },
      icon(KIND_ICON[sectionKind(s)], 'i i--lg'),
      h('h2', { text: `«${s.name}» está vacía` }),
      h('p', { text: 'Añade un juego a mano, crea una saga o tráelos de Steam, Xbox o Epic.' }),
      h('div', { class: 'inline' },
        h('button', { class: 'btn btn--primary', onclick: () => addEntry() }, icon('plus'), 'Añadir juego'),
        h('button', { class: 'btn btn--secondary', onclick: openImport }, icon('download'), 'Importar'))));
  } else if (!shown && (q || S.platform)) {
    frag.append(h('div', { class: 'empty' }, icon('search', 'i i--lg'), h('h2', { text: 'Sin resultados' }), h('p', { text: q ? `Ningún juego coincide con «${q}».` : 'Ningún juego con esa plataforma.' })));
  }
  main.replaceChildren(frag);
  if (q) $('#statusLeft').textContent = tr(`${plural(shown, 'resultado', 'resultados')} para «${q}»`);
}

function setView(v) { S.settings.view = v; Host.call('settings.set', { view: v }); renderList(); restoreFocusToSelected(); }

function dividerEl(sec, b, bi) {
  return h('div', { class: 'divider-note' },
    icon('info', 'i i--sm'),
    h('span', { text: b.style === 'bracket' ? `[${b.name}]` : b.name }),
    h('button', { class: 'btn btn--ghost btn--icon btn--sm', 'aria-label': 'Editar nota separadora', title: 'Editar', onclick: () => editDivider(sec, b) }, icon('pencil', 'i i--sm')),
    h('button', { class: 'btn btn--ghost btn--icon btn--sm', 'aria-label': 'Eliminar nota separadora', title: 'Eliminar', onclick: () => mutate('eliminar nota', () => sec.blocks.splice(sec.blocks.indexOf(b), 1)) }, icon('trash-2', 'i i--sm')));
}

function groupEl(sec, si, b, bi, entries, view, searching) {
  const key = `${si}:${bi}:${b.name}`;
  const collapsed = S.collapsed.has(key) && !searching;
  const listId = `g-${si}-${bi}`;
  const title = searching ? `${sec.name} › ${blockLabel(b)}` : blockLabel(b);
  const head = h('div', { class: 'group__head', oncontextmenu: searching ? null : ev => { ev.preventDefault(); groupMenu(sec, b, key, ev); } },
    h('button', { class: 'group__toggle', 'aria-expanded': String(!collapsed), 'aria-controls': listId,
      onclick: () => { collapsed ? S.collapsed.delete(key) : S.collapsed.add(key); renderList(); $(`[aria-controls="${listId}"]`)?.focus(); } },
      icon('chevron-down'), h('h2', { text: title })),
    b.style === 'paren' ? h('span', { class: 'group__kind', title: 'Se escribe entre paréntesis, como (Indies)' }, 'género') : null,
    h('span', { class: 'group__count', 'aria-label': plural(entries.length, 'juego', 'juegos') }, String(entries.length)),
    searching ? null : h('div', { class: 'group__actions' },
      h('button', { class: 'btn btn--ghost btn--icon btn--sm', title: `Añadir juego a ${blockLabel(b)}`, 'aria-label': `Añadir juego a ${blockLabel(b)}`, onclick: () => addEntry(b) }, icon('plus', 'i i--sm')),
      b.style !== 'none' ? h('button', { class: 'btn btn--ghost btn--icon btn--sm', title: 'Renombrar', 'aria-label': `Renombrar ${blockLabel(b)}`, onclick: () => renameCategory(sec, b) }, icon('pencil', 'i i--sm')) : null,
      h('button', { class: 'btn btn--ghost btn--icon btn--sm', title: 'Subir', 'aria-label': `Subir ${blockLabel(b)}`, onclick: () => moveBlock(sec, b, -1) }, icon('arrow-up', 'i i--sm')),
      h('button', { class: 'btn btn--ghost btn--icon btn--sm', title: 'Bajar', 'aria-label': `Bajar ${blockLabel(b)}`, onclick: () => moveBlock(sec, b, 1) }, icon('arrow-down', 'i i--sm')),
      h('button', { class: 'btn btn--ghost btn--icon btn--sm btn--danger', title: 'Eliminar', 'aria-label': `Eliminar ${blockLabel(b)}`, onclick: () => deleteCategory(sec, b) }, icon('trash-2', 'i i--sm'))));
  const group = h('section', { class: 'group', 'aria-label': title,
    ondragover: ev => { if (dragId) { ev.preventDefault(); group.classList.add('drop-target'); } },
    ondragleave: ev => { if (!group.contains(ev.relatedTarget)) group.classList.remove('drop-target'); },
    ondrop: ev => { ev.preventDefault(); group.classList.remove('drop-target'); if (dragId) moveEntryTo(dragId, sec, b, null); } }, head);
  if (!collapsed) {
    const list = h('ul', { class: view === 'grid' ? `grid ${S.settings.covers ? '' : 'grid--nocovers'}` : 'rows', id: listId, role: 'list' });
    if (!entries.length) list.append(h('li', { class: 'muted', style: 'font-size:13px;padding:6px 2px' }, 'Sin juegos todavía. Arrastra uno aquí o pulsa +.'));
    for (const e of entries) list.append(h('li', { dataset: { id: e.id } }, itemEl(e, b, sec, view), moreBtn(e)));
    group.append(list);
  }
  return group;
}

function entryLabel(e, b, sec) {
  const parts = [e.title || tr('Sin título')];
  if (e.platforms) parts.push(e.platforms);
  if (e.date) parts.push(tr((isDone(sec) ? 'completado el ' : 'fecha ') + e.date));
  const r = F.num(e, 'rating'); if (r != null) parts.push(tr(`${fmtNum(r)} de 10`));
  const hrs = F.num(e, 'hours'); if (hrs != null) parts.push(tr(`${fmtNum(hrs)} horas`));
  const a = F.num(e, 'ach'); if (a != null) parts.push(tr(`logros ${fmtNum(a)}%`));
  const d = F.ttb(e); if (d) parts.push(tr(`dura unas ${fmtNum(d.main)} horas`));
  if (e.note) parts.push(`${tr('Nota')}: ${e.note}`);
  return parts.join(', ');
}
// Portada 2:3. cls 'cover' = tarjeta de la cuadrícula; 'detail__thumb' = miniatura de la cabecera del inspector.
function coverEl(e, cls = 'cover') {
  const url = S.settings.covers ? S.covers.get(coverKey(e)) : null;
  const card = cls === 'cover';
  const r = card ? F.num(e, 'rating') : null;
  const score = r != null ? h('span', { class: 'cover__score', 'aria-hidden': 'true' }, h('span', { class: 'cover__star', text: '★' }), fmtNum(r)) : null;
  if (url) {
    const box = h('div', { class: `${cls} m-zoom` });
    // Si Steam solo tiene la cabecera apaisada, se ve entera sobre una copia desenfocada en vez de recortarla
    box.append(h('img', { src: url, alt: '', loading: 'lazy', decoding: 'async', onerror: ev => ev.target.remove(),
      onload: ev => { const i = ev.target; if (i.naturalWidth > i.naturalHeight) { box.classList.add('cover--wide'); box.style.setProperty('--img', `url(${JSON.stringify(url)})`); } } }), score);
    return box;
  }
  const fam = mainFamily(e);
  if (!card) return h('div', { class: `${cls} cover--type cover--mini`, 'aria-hidden': 'true', dataset: { fam } }, h('b', { text: initials(e.title) }));
  // Sin portada: carátula tipográfica con el título centrado, degradado con el tono de la plataforma y retícula de puntos
  const plats = splitPlatforms(e.platforms);
  const ic = plats.map(platformIcon).find(Boolean);
  const title = e.title || '—';
  return h('div', { class: 'cover cover--type', 'aria-hidden': 'true', dataset: { fam, len: title.length > 34 ? 'xl' : title.length > 18 ? 'l' : 's' } },
    ic ? h('img', { class: `cover__brand brand ${ic.ink ? 'ic-ink' : ''}`, src: ic.src, alt: '' }) : null,
    score,
    h('span', { class: 'cover__rule' }),
    h('b', { class: 'cover__title', text: title }),
    plats.length ? h('span', { class: 'cover__plat', text: plats.join(' · ') }) : null);
}
function platformsEl(e) {
  return splitPlatforms(e.platforms).map(p => {
    const ic = platformIcon(p);
    return h('span', { class: 'pbadge', dataset: { fam: platformFamily(p) } }, ic ? h('img', { class: `brand ${ic.ink ? 'ic-ink' : ''}`, src: ic.src, alt: '' }) : null, p);
  });
}
function itemEl(e, b, sec, view) {
  const sel = e.id === S.selected;
  const r = F.num(e, 'rating'), hrs = F.num(e, 'hours'), a = F.num(e, 'ach');
  const common = {
    'aria-current': sel ? 'true' : null, tabindex: sel ? '0' : '-1', 'aria-label': entryLabel(e, b, sec), draggable: 'true',
    dataset: { id: e.id },
    onclick: () => select(e.id),
    ondblclick: () => focusDetailTitle(),
    oncontextmenu: ev => { ev.preventDefault(); select(e.id, false); gameMenu(e.id, ev); },
    onkeydown: ev => itemKey(ev, e.id),
    ondragstart: ev => { dragId = e.id; ev.dataTransfer.effectAllowed = 'move'; ev.dataTransfer.setData('text/plain', e.title); ev.currentTarget.classList.add('dragging'); },
    ondragend: ev => { dragId = null; ev.currentTarget.classList.remove('dragging'); document.querySelectorAll('.drop-before,.drop-target').forEach(x => x.classList.remove('drop-before', 'drop-target')); },
    ondragover: ev => { if (dragId && dragId !== e.id) { ev.preventDefault(); ev.stopPropagation(); ev.currentTarget.classList.add('drop-before'); } },
    ondragleave: ev => ev.currentTarget.classList.remove('drop-before'),
    ondrop: ev => { if (!dragId) return; ev.preventDefault(); ev.stopPropagation(); ev.currentTarget.classList.remove('drop-before'); moveEntryTo(dragId, sec, b, e.id); }
  };
  if (view === 'grid') {
    const cv = coverEl(e, 'cover');
    const typeTile = cv.classList.contains('cover--type');
    return h('button', { class: 'card', ...common }, cv,
      h('div', { class: 'card__body' },
        typeTile ? null : h('span', { class: 'card__title', text: e.title || 'Sin título' }),
        h('span', { class: 'card__meta' }, ...platformsEl(e)),
        (hrs != null || e.date) ? h('span', { class: 'card__meta card__nums' },
          e.date ? h('span', { class: isDone(sec) ? 'done-mark' : '' }, icon(isDone(sec) ? 'circle-check' : 'calendar', 'i i--sm'), e.date) : null,
          hrs != null ? h('span', null, icon('clock', 'i i--sm'), `${fmtNum(hrs)} h`) : null) : null,
        a != null ? h('div', { class: 'bar', title: `Logros ${fmtNum(a)}%`, 'aria-hidden': 'true' }, h('i', { style: `width:${Math.min(100, a)}%` })) : null,
        e.note ? h('span', { class: 'card__note', text: e.note, title: e.note }) : null));
  }
  const thumbUrl = S.settings.covers ? S.covers.get(coverKey(e)) : null;
  return h('button', { class: 'row', ...common },
    h('span', { class: 'thumb', 'aria-hidden': 'true' }, thumbUrl ? h('img', { src: thumbUrl, alt: '' }) : initials(e.title)),
    h('span', { style: 'min-width:0' },
      h('span', { class: 'row__title', style: 'display:block', text: e.title || 'Sin título' }),
      h('span', { class: 'row__sub' }, ...platformsEl(e), e.note ? h('span', { class: 'row__note', text: e.note }) : null, !e.platforms && !e.note ? '—' : null)),
    h('span', { class: 'row__side card__nums' },
      r != null ? h('span', { class: 'stars', 'aria-hidden': 'true' }, `★ ${fmtNum(r)}`) : null,
      hrs != null ? h('span', null, `${fmtNum(hrs)} h`) : null,
      a != null ? h('span', null, `🏅 ${fmtNum(a)}%`) : null,
      hrs == null && F.ttb(e) ? h('span', { title: 'Duración estimada' }, `⌛ ${fmtNum(F.ttb(e).main)} h`) : null,
      e.date ? h('span', { class: isDone(sec) ? 'done-mark' : '' }, icon(isDone(sec) ? 'circle-check' : 'calendar', 'i i--sm'), e.date) : null));
}
const narrow = () => window.innerWidth <= 820;
function closeDetail() { S.detailOpen = false; $('#workspace').classList.add('no-detail'); restoreFocusToSelected(); }
function select(id, focus = true) {
  S.selected = id;
  S.detailOpen = true;
  document.querySelectorAll('.card[aria-current], .row[aria-current]').forEach(x => { x.removeAttribute('aria-current'); x.tabIndex = -1; });
  const el = document.querySelector(`.card[data-id="${id}"], .row[data-id="${id}"]`);
  if (el) { el.setAttribute('aria-current', 'true'); el.tabIndex = 0; if (focus) el.focus({ preventScroll: false }); }
  $('#workspace').classList.remove('no-detail');
  transition('detail', renderDetail);
}
function restoreFocusToSelected() {
  const el = S.selected && document.querySelector(`.card[data-id="${S.selected}"], .row[data-id="${S.selected}"]`);
  if (el) el.focus(); else $('#main').focus();
}
function updateItem(id) {
  const x = findEntry(id); if (!x) return;
  renderTtbStatus();
  const li = document.querySelector(`li[data-id="${id}"]`);
  if (!li) return;
  const view = S.settings.view === 'list' ? 'list' : 'grid';
  li.replaceChildren(itemEl(x.e, x.b, x.s, view), moreBtn(x.e));
}

// Navegación por teclado dentro de la lista (roving tabindex)
function itemKey(ev, id) {
  const items = [...document.querySelectorAll('#main .card, #main .row')];
  const i = items.findIndex(x => x.dataset.id === id);
  const go = j => { if (j >= 0 && j < items.length) { ev.preventDefault(); select(items[j].dataset.id); } };
  if (ev.altKey && (ev.key === 'ArrowUp' || ev.key === 'ArrowDown')) { ev.preventDefault(); moveEntryStep(id, ev.key === 'ArrowUp' ? -1 : 1); return; }
  if (ev.key === 'ContextMenu' || (ev.shiftKey && ev.key === 'F10')) { ev.preventDefault(); gameMenu(id, ev.currentTarget); return; }
  if (ev.ctrlKey || ev.altKey || ev.metaKey) return;
  const vertical = (dir) => {
    const r = items[i].getBoundingClientRect();
    let best = -1, bestD = Infinity;
    items.forEach((x, j) => {
      const q = x.getBoundingClientRect();
      const dy = dir > 0 ? q.top - r.bottom : r.top - q.bottom;
      if (dy < -2) return;
      const d = dy * 4 + Math.abs(q.left - r.left);
      if (j !== i && d < bestD) { bestD = d; best = j; }
    });
    return best;
  };
  switch (ev.key) {
    case 'ArrowRight': go(i + 1); break;
    case 'ArrowLeft': go(i - 1); break;
    case 'ArrowDown': go(S.settings.view === 'list' ? i + 1 : vertical(1)); break;
    case 'ArrowUp': go(S.settings.view === 'list' ? i - 1 : vertical(-1)); break;
    case 'Home': go(0); break;
    case 'End': go(items.length - 1); break;
    case 'Enter': ev.preventDefault(); focusDetailTitle(); break;
  }
}

// ───────────────────────────── Panel de detalle ─────────────────────────────
let editBefore = null;
function beginEdit() { if (editBefore == null) editBefore = snapshot(); }
function endEdit(label) { if (editBefore != null) { commit(editBefore, label); editBefore = null; renderHeadAndTabs(); } }
function renderHeadAndTabs() { renderHead(); renderTabs(); }
function focusDetailTitle() { S.detailOpen = true; $('#workspace').classList.remove('no-detail'); const t = $('#dTitle'); if (t) { t.focus(); t.select(); } }

// Fondo del panel: la portada muy ampliada y desenfocada; sin portada, un brillo del color de la plataforma
function ambientEl(e) {
  const url = S.settings.covers ? S.covers.get(coverKey(e)) : null;
  const layer = h('i');
  if (url) layer.style.backgroundImage = `url(${JSON.stringify(url)})`;
  return h('div', { class: `detail__ambient${url ? '' : ' detail__ambient--glow'}`, 'aria-hidden': 'true', dataset: { fam: mainFamily(e) } }, layer);
}
function renderDetail() {
  const d = $('#detail');
  const x = S.selected && findEntry(S.selected);
  if (!S.doc || S.textMode || S.statsMode) { d.hidden = true; $('#workspace').classList.add('no-detail'); return; }
  d.hidden = false;
  if (!x) {
    $('#workspace').classList.add('no-detail');
    d.replaceChildren(h('div', { class: 'detail__empty' }, icon('gamepad-2', 'i i--lg'), h('p', { text: 'Elige un juego para ver y editar sus datos.' })));
    return;
  }
  // En ventanas estrechas el detalle es un panel inferior que solo se abre al elegir un juego.
  $('#workspace').classList.toggle('no-detail', narrow() && !S.detailOpen);
  const { e, b, s } = x;
  const done = isDone(s);
  const realStore = F.store(e);
  if (S.pendingStore && S.pendingStore.id !== e.id) S.pendingStore = null;
  const st = realStore || (S.pendingStore ? { store: S.pendingStore.store, id: '' } : null);
  const url = storeUrl(realStore);
  const live = (fn, label) => ({ onfocus: beginEdit, onblur: () => endEdit(label), oninput: ev => { beginEdit(); fn(ev.target.value, ev); dirtyChanged(); updateItem(e.id); } });
  const ttb = F.ttb(e);
  const setTtb = () => {
    const main = parseNum($('#dTtb')?.value ?? ''), full = parseNum($('#dTtbFull')?.value ?? '');
    F.set(e, 'ttb', main != null && main > 0 ? F.makeTtb(main, full != null && full > 0 ? full : null) : null);
  };

  // Cabecera de contexto (fija arriba): miniatura, saga y título, para saber siempre qué ficha se edita
  const ctx = h('header', { class: 'detail__ctx' },
    coverEl(e, 'detail__thumb'),
    h('div', { class: 'detail__id' },
      h('p', { class: 'detail__saga' }, h('span', { text: b.style === 'none' ? 'Sin saga' : blockLabel(b) }), h('span', { class: 'detail__sec', text: s.name })),
      h('h2', { class: 'detail__title', id: 'detailHeading', text: e.title || 'Sin título' })),
    h('button', { type: 'button', class: 'btn btn--ghost btn--icon btn--sm narrow-only', 'aria-label': 'Cerrar los detalles', onclick: closeDetail }, icon('x', 'i i--sm')));
  const quick = (!done || url) ? h('div', { class: 'detail__quick' },
    !done ? h('button', { type: 'button', class: 'btn btn--primary btn--sm', onclick: () => markPassed(e.id), title: 'Mover a Completados con la fecha de hoy (Ctrl+Intro)', 'aria-keyshortcuts': 'Control+Enter' }, icon('check', 'i i--sm'), 'Marcar como completado') : null,
    url ? h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => Host.call('open.url', { url }) }, icon('external-link', 'i i--sm'), 'Ver en la tienda') : null) : null;

  // Formulario, en tres bloques y un desplegable con lo técnico del archivo
  const plats = allPlatforms();
  const cur = splitPlatforms(e.platforms);
  const datalist = h('datalist', { id: 'platList' }, ...plats.map(p => h('option', { value: p })));
  const iso = dmyToIso(e.date);
  const customDate = e.date && !iso;
  const rating = F.num(e, 'rating');
  const others = F.others(e);
  const cats = categoriesOf(s);
  const block = (id, title, ic, ...kids) => h('section', { class: 'fblock', 'aria-labelledby': id }, h('h3', { class: 'fblock__h', id }, icon(ic, 'i i--sm'), title), ...kids);

  const form = h('form', { class: 'form', 'aria-labelledby': 'detailHeading', onsubmit: ev => ev.preventDefault() },
    block('fbId', 'Identidad y saga', 'tag',
      h('div', { class: 'field' },
        h('label', { for: 'dTitle' }, 'Título'),
        h('input', { id: 'dTitle', type: 'text', value: e.title, autocomplete: 'off', spellcheck: 'false', ...live(v => { e.title = v; $('#detailHeading').textContent = v || tr('Sin título'); suggestCategorySoon(e); }, 'editar título') }),
        h('div', { id: 'catSuggest' })),
      h('div', { class: 'field' },
        h('label', { for: 'dPlat' }, 'Plataformas'),
        h('input', { id: 'dPlat', type: 'text', value: e.platforms || '', list: 'platList', autocomplete: 'off', 'aria-describedby': 'dPlatHint',
          ...live(v => { e.platforms = v.trim() ? v : null; }, 'editar plataformas') }),
        datalist,
        h('span', { class: 'hint', id: 'dPlatHint' }, 'Separa varias con «/», por ejemplo PC/PS Vita.'),
        h('div', { class: 'suggest', role: 'group', 'aria-label': 'Añadir plataforma' },
          ...plats.filter(p => !cur.includes(p)).slice(0, 6).map(p => {
            const ic = platformIcon(p);
            return h('button', { type: 'button', dataset: { fam: platformFamily(p) }, onclick: () => mutate('añadir plataforma', () => { e.platforms = [...splitPlatforms(e.platforms), p].join('/'); }) },
              ic ? h('img', { class: `brand ${ic.ink ? 'ic-ink' : ''}`, src: ic.src, alt: '', style: 'width:14px;height:14px' }) : icon('plus', 'i i--sm'), p);
          }))),
      h('div', { class: 'row2' },
        h('div', { class: 'field' },
          h('label', { for: 'dSection' }, 'Sección'),
          h('select', { id: 'dSection', class: 'select', onchange: ev => moveToSection(e.id, +ev.target.value) },
            ...sections().map((sec, i) => h('option', { value: i, selected: sec === s }, sec.name)))),
        h('div', { class: 'field' },
          h('label', { for: 'dCat' }, 'Saga o género'),
          h('select', { id: 'dCat', class: 'select', onchange: ev => changeCategory(e.id, ev.target.value) },
            ...cats.map(c => h('option', { value: String(s.blocks.indexOf(c)), selected: c === b }, blockLabel(c))),
            !cats.some(c => c.style === 'none') ? h('option', { value: 'none' }, 'Sin categoría') : null,
            h('option', { value: 'new' }, 'Nueva saga o género…'))))),

    block('fbProg', 'Progreso y puntuación', 'trophy',
      h('div', { class: 'field' },
        h('label', { for: 'dDate' }, done ? 'Fecha en que lo pasaste' : 'Fecha'),
        h('div', { class: 'inline' },
          customDate
            ? h('input', { id: 'dDate', type: 'text', value: e.date, ...live(v => { e.date = v.trim() || null; }, 'editar fecha') })
            : h('input', { id: 'dDate', type: 'date', value: iso || '', onfocus: beginEdit, onblur: () => endEdit('editar fecha'),
              onchange: ev => { beginEdit(); e.date = isoToDmy(ev.target.value); dirtyChanged(); updateItem(e.id); } }),
          h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => mutate('poner fecha de hoy', () => { e.date = today(); }) }, 'Hoy'),
          e.date ? h('button', { type: 'button', class: 'btn btn--ghost btn--icon btn--sm', 'aria-label': 'Quitar la fecha', title: 'Quitar la fecha', onclick: () => mutate('quitar fecha', () => { e.date = null; }) }, icon('x', 'i i--sm')) : null)),
      h('fieldset', { class: 'field' },
        h('legend', null, 'Puntuación'),
        ratingEl(e, rating)),
      h('div', { class: 'row2' },
        h('div', { class: 'field' },
          h('label', { for: 'dHours' }, 'Horas jugadas'),
          h('input', { id: 'dHours', class: 'num', type: 'number', min: '0', step: '0.5', inputmode: 'decimal', value: F.num(e, 'hours') ?? '',
            ...live(v => F.set(e, 'hours', parseNum(v) != null ? F.make('hours', parseNum(v)) : null), 'editar horas') })),
        h('div', { class: 'field' },
          h('label', { for: 'dAch' }, 'Logros (%)'),
          h('input', { id: 'dAch', class: 'num', type: 'number', min: '0', max: '100', step: '1', inputmode: 'numeric', value: F.num(e, 'ach') ?? '',
            ...live(v => { const n = parseNum(v); F.set(e, 'ach', n != null ? F.make('ach', Math.max(0, Math.min(100, n))) : null); }, 'editar logros') }))),
      // Duración estimada → {ttb: 12h} o {ttb: 12h / 35h}. Sin historia principal no se guarda nada.
      h('div', { class: 'row2' },
        h('div', { class: 'field' },
          h('label', { for: 'dTtb' }, 'Duración estimada (h)'),
          h('input', { id: 'dTtb', class: 'num', type: 'number', min: '0', step: '0.5', inputmode: 'decimal', value: ttb?.main ?? '', placeholder: 'Historia', 'aria-describedby': 'dTtbHint',
            ...live(() => setTtb(), 'editar duración') })),
        h('div', { class: 'field' },
          h('label', { for: 'dTtbFull' }, 'Completista (h)'),
          h('input', { id: 'dTtbFull', class: 'num', type: 'number', min: '0', step: '0.5', inputmode: 'decimal', value: ttb?.full ?? '', placeholder: 'Opcional', 'aria-describedby': 'dTtbHint',
            ...live(() => setTtb(), 'editar duración') }))),
      h('div', { class: 'field' }, h('span', { class: 'hint', id: 'dTtbHint' }, 'Lo que se tarda en pasarlo: la historia principal y, si quieres, al 100 %. Sirve para «¿Qué juego ahora?».')),
      h('div', { class: 'ttb-actions' },
        h('button', { type: 'button', class: 'btn btn--secondary btn--sm', id: 'dTtbFind', onclick: () => findDuration(e) }, icon('search', 'i i--sm'), 'Buscar duración en IGDB'),
        h('button', { type: 'button', class: 'btn btn--ghost btn--sm', onclick: () => Host.call('open.url', { url: hltbUrl(e.title) }), 'aria-label': `Ver «${e.title || 'este juego'}» en HowLongToBeat (se abre en el navegador)` }, icon('external-link', 'i i--sm'), 'Ver en HowLongToBeat')),
      h('div', { id: 'dTtbResults', class: 'ttb-results', 'aria-live': 'polite' })),

    block('fbLinks', 'Enlaces y notas', 'link',
      h('div', { class: 'field' },
        h('label', { for: 'dNote' }, 'Nota'),
        h('input', { id: 'dNote', type: 'text', value: e.note || '', placeholder: 'Por ejemplo: emulación, lo pasé de pequeño…', ...live(v => { e.note = v.trim() ? v : null; }, 'editar nota') })),
      h('div', { class: 'field' },
        h('div', { class: st ? 'row2' : null },
          h('div', { class: 'field' },
            h('label', { for: 'dStore' }, 'Tienda enlazada'),
            h('select', { id: 'dStore', class: 'select', onchange: ev => {
              const v = ev.target.value;
              // Sin id todavía: la tienda queda pendiente hasta que se escriba (no se guarda {steam:} vacío).
              if (v && !(realStore && realStore.id)) { S.pendingStore = { id: e.id, store: v }; renderDetail(); $('#dStoreId')?.focus(); return; }
              S.pendingStore = null;
              mutate('cambiar tienda', () => F.set(e, 'store', v ? `${v}:${realStore.id}` : null));
            } },
              h('option', { value: '' }, 'Ninguna'), ...['steam', 'xbox', 'epic', 'gog', 'psn', 'nintendo', 'ea', 'ubisoft', 'ra'].map(k => h('option', { value: k, selected: st?.store === k }, { steam: 'Steam', xbox: 'Xbox', epic: 'Epic', gog: 'GOG', psn: 'PlayStation', nintendo: 'Nintendo', ea: 'EA', ubisoft: 'Ubisoft', ra: 'RetroAchievements' }[k])))),
          st ? h('div', { class: 'field' },
            h('label', { for: 'dStoreId' }, st.store === 'steam' ? 'Appid de Steam' : 'Id en la tienda'),
            h('input', { id: 'dStoreId', class: 'input num', type: 'text', value: st.id, placeholder: st.store === 'steam' ? 'p. ej. 2050650' : 'id', ...live(v => {
              const id = v.trim().replace(/\s+/g, '');
              F.set(e, 'store', id ? `${st.store}:${id}` : null);
              S.pendingStore = id ? null : { id: e.id, store: st.store };
            }, 'editar id de tienda') })) : null),
        Host.native ? h('div', null, h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => findSteamId(e) }, h('img', { class: 'brand ic-ink', src: 'icons/steam.svg', alt: '', style: 'width:14px;height:14px' }), 'Buscar en Steam')) : null,
        h('span', { class: 'hint' }, 'Se guarda como {steam:id} y sirve para las portadas, los enlaces del .md y «Actualizar datos».')),
      sgdbField(e)),

    // Lo técnico del formato plano, plegado (el estado se recuerda mientras la app está abierta)
    h('details', { class: 'adv', open: !!S.advOpen, ontoggle: ev => { S.advOpen = ev.target.open; } },
      h('summary', null, icon('settings', 'i i--sm'), h('span', null, 'Opciones avanzadas de archivo'),
        others.length ? h('span', { class: 'adv__count', 'aria-label': plural(others.length, 'campo', 'campos') }, String(others.length)) : null,
        icon('chevron-down', 'i i--sm adv__chev')),
      h('div', { class: 'adv__body' },
        e.platforms && e.note ? h('div', { class: 'field' },
          h('label', { for: 'dNotePos' }, 'En el archivo, la nota va'),
          h('select', { id: 'dNotePos', class: 'select', onchange: ev => mutate('mover nota', () => { e.notePos = ev.target.value; }) },
            h('option', { value: 'afterPlatform', selected: e.notePos !== 'beforePlatform' }, 'después de la plataforma'),
            h('option', { value: 'beforePlatform', selected: e.notePos === 'beforePlatform' }, 'antes de la plataforma'))) : null,
        h('div', { class: 'field' },
          h('label', { for: 'dField' }, 'Otros campos { }'),
          others.length ? h('div', { class: 'tags', role: 'list', 'aria-label': 'Campos del juego' }, ...others.map(f => h('span', { class: 'tag', role: 'listitem' }, f,
            h('button', { type: 'button', 'aria-label': `Quitar ${f}`, title: 'Quitar', onclick: () => mutate('quitar campo', () => e.fields.splice(e.fields.indexOf(f), 1)) }, icon('x', 'i i--sm'))))) : null,
          h('div', { class: 'inline' },
            h('input', { id: 'dField', class: 'input', type: 'text', placeholder: 'Dificultad: Difícil, coop, Platino…', onkeydown: ev => { if (ev.key === 'Enter') { ev.preventDefault(); addField(e, ev.target); } } }),
            h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => addField(e, $('#dField')) }, 'Añadir')),
          h('span', { class: 'hint' }, 'Se escriben al final de la línea, entre llaves.')))));

  // Barra de acciones fija abajo; Eliminar queda aparte, separado por una raya
  const actions = h('div', { class: 'detail__actions', role: 'group', 'aria-label': 'Acciones del juego' },
    h('button', { type: 'button', class: 'btn btn--ghost btn--sm', onclick: () => moveEntryStep(e.id, -1), title: 'Subir (Alt+↑)', 'aria-label': 'Subir' }, icon('arrow-up', 'i i--sm'), h('span', { class: 'label-act' }, 'Subir')),
    h('button', { type: 'button', class: 'btn btn--ghost btn--sm', onclick: () => moveEntryStep(e.id, 1), title: 'Bajar (Alt+↓)', 'aria-label': 'Bajar' }, icon('arrow-down', 'i i--sm'), h('span', { class: 'label-act' }, 'Bajar')),
    h('button', { type: 'button', class: 'btn btn--ghost btn--sm', onclick: () => duplicateEntry(e.id), 'aria-label': 'Duplicar' }, icon('copy', 'i i--sm'), h('span', { class: 'label-act' }, 'Duplicar')),
    h('span', { class: 'grow' }),
    h('button', { type: 'button', class: 'btn btn--sm btn--destroy', onclick: () => deleteEntry(e.id), title: 'Eliminar (Supr)' }, icon('trash-2', 'i i--sm'), 'Eliminar'));

  // Al volver a pintar la misma ficha (tras puntuar, añadir un campo…) se conservan el desplazamiento y el foco
  const same = d.dataset.eid === e.id;
  const scrollTop = same ? d.querySelector('.detail__scroll')?.scrollTop || 0 : 0;
  const focusId = same && d.contains(document.activeElement) ? document.activeElement.id : '';
  d.dataset.eid = e.id;
  const scroller = h('div', { class: 'detail__scroll' }, ctx, quick, form);
  d.replaceChildren(ambientEl(e), scroller, actions);
  scroller.scrollTop = scrollTop;
  if (focusId) document.getElementById(focusId)?.focus({ preventScroll: true });
  renderCatSuggestion(e);
}

// Portada elegida a mano: id de juego de SteamGridDB ({sgdb:id}). Acepta el número o el enlace del juego.
function sgdbField(e) {
  const id = F.sgdb(e);
  const parse = v => { const m = /steamgriddb\.com\/game\/(\d+)/i.exec(v) || /^\s*(\d+)\s*$/.exec(v); return m ? m[1] : null; };
  const open = gid => Host.call('open.url', { url: `https://www.steamgriddb.com/game/${gid}` });
  return h('div', { class: 'field' },
    h('label', { for: 'dSgdb' }, 'Portada de SteamGridDB (id del juego)'),
    h('div', { class: 'inline' },
      h('input', { id: 'dSgdb', class: 'input num', type: 'text', inputmode: 'numeric', value: id || '', placeholder: 'p. ej. 5245268 o el enlace del juego', autocomplete: 'off', 'aria-describedby': 'dSgdbHint',
        onfocus: beginEdit, onblur: () => endEdit('editar id de SteamGridDB'),
        oninput: ev => { beginEdit(); const v = ev.target.value; const gid = parse(v); if (gid || !v.trim()) { F.set(e, 'sgdb', gid ? `sgdb:${gid}` : null); dirtyChanged(); } },
        // al confirmar (Intro o salir del campo) se busca la portada nueva y se deja solo el número
        onchange: ev => { const gid = parse(ev.target.value); ev.target.value = gid || ''; if (!gid) F.set(e, 'sgdb', null); updateItem(e.id); queueCovers(); renderDetailCoverSoon(e); } }),
      id ? h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => open(id) }, icon('external-link', 'i i--sm'), 'Ver') : null),
    h('span', { class: 'hint', id: 'dSgdbHint' }, S.settings.hasSgdbKey
      ? 'Abre el juego en steamgriddb.com y copia el número del enlace (…/game/5245268). Se guarda como {sgdb:id} y manda sobre la búsqueda automática.'
      : 'Necesita la clave de SteamGridDB (Ajustes › Cuentas). Se guarda como {sgdb:id}.'));
}
// Tras cambiar el id, la miniatura y el fondo del inspector se actualizan cuando llega la portada (evento «cover»)
function renderDetailCoverSoon(e) {
  const th = document.querySelector('.detail__thumb');
  if (th) th.replaceWith(coverEl(e, 'detail__thumb'));
  document.querySelector('.detail__ambient')?.replaceWith(ambientEl(e));
}

// Puntuación: 5 estrellas con medias estrellas. Son 10 botones de radio (1–10, cada media estrella es un punto),
// así el teclado y los lectores de pantalla funcionan igual que antes y el archivo sigue guardando {★ n/10}.
const STAR_D = 'M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.12 2.12 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.12 2.12 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.12 2.12 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.12 2.12 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.12 2.12 0 0 0 1.597-1.16z';
function starSvg() {
  const ns = 'http://www.w3.org/2000/svg';
  const s = document.createElementNS(ns, 'svg'); s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true');
  const p = document.createElementNS(ns, 'path'); p.setAttribute('d', STAR_D); s.append(p);
  return s;
}
function ratingEl(e, rating) {
  const starsTxt = n => tr(`${String(n / 2).replace('.', I18N.code === 'en' ? '.' : ',')} ${n === 2 ? 'estrella' : 'estrellas'}`);
  const badge = h('output', { class: 'rating__badge', 'aria-hidden': 'true' });
  const show = (v, preview) => {
    wrap.querySelectorAll('.star').forEach((st, k) => st.style.setProperty('--f', `${Math.max(0, Math.min(1, ((v || 0) - 2 * k) / 2)) * 100}%`));
    badge.replaceChildren(h('b', { text: v != null ? fmtNum(v) : '—' }), '/10');
    badge.classList.toggle('is-set', v != null); badge.classList.toggle('is-preview', !!preview);
  };
  const wrap = h('div', { class: 'rating', role: 'radiogroup', 'aria-label': 'Puntuación de media estrella a 5 estrellas (de 1 a 10)', onpointerleave: () => show(rating) },
    ...[0, 1, 2, 3, 4].map(k => h('span', { class: 'star' }, starSvg(), h('span', { class: 'star__fill' }, starSvg()),
      ...[2 * k + 1, 2 * k + 2].flatMap(n => [
        h('input', { type: 'radio', name: 'rating', id: `r${n}`, value: n, checked: rating != null && Math.round(rating) === n,
          onchange: () => mutate('puntuar', () => F.set(e, 'rating', F.make('rating', n))) }),
        h('label', { for: `r${n}`, class: `star__half star__half--${n % 2 ? 'l' : 'r'}`, title: `${starsTxt(n)} · ${n}/10`, onpointerenter: () => show(n, true) },
          h('span', { class: 'sr-only' }, `${starsTxt(n)} (${n} de 10)`))]))));
  show(rating);
  return h('div', { class: 'rating-row' }, wrap, badge,
    rating != null ? h('button', { type: 'button', class: 'btn btn--ghost btn--sm', onclick: () => mutate('quitar puntuación', () => F.set(e, 'rating', null)) }, 'Quitar') : null);
}

function addField(e, input) {
  const v = input.value.trim().replace(/^\{|\}$/g, '');
  if (!v) return;
  mutate('añadir campo', () => { e.fields ||= []; const si = e.fields.findIndex(f => F.kind(f) === 'store'); si >= 0 ? e.fields.splice(si, 0, v) : e.fields.push(v); });
  $('#dField')?.focus();
}

let sugTimer;
function suggestCategorySoon(e) {
  clearTimeout(sugTimer);
  sugTimer = setTimeout(async () => {
    const r = await Host.call('suggest.category', { doc: S.doc, title: e.title });
    S.suggestion = r && r.name ? { id: e.id, ...r } : null;
    renderCatSuggestion(e);
  }, 450);
}
function renderCatSuggestion(e) {
  const box = $('#catSuggest'); if (!box) return;
  const x = findEntry(e.id);
  const sg = S.suggestion;
  if (!sg || sg.id !== e.id || !x || (norm(x.b.name) === norm(sg.name) && x.b.style !== 'none')) { box.replaceChildren(); return; }
  const label = sg.paren ? `(${sg.name})` : sg.name;
  box.replaceChildren(h('div', { class: 'suggest' },
    h('button', { type: 'button', onclick: () => { changeCategory(e.id, { name: sg.name, paren: sg.paren }); S.suggestion = null; } },
      icon('sparkles', 'i i--sm'), `Mover a la saga sugerida: ${label}`)));
}

async function findSteamId(e) {
  announce('Buscando en la tienda de Steam…');
  try {
    const r = await Host.call('steam.find', { title: e.title });
    if (!r) { toast(`No se ha encontrado «${e.title}» en Steam.`); return; }
    mutate('enlazar con Steam', () => F.set(e, 'store', `steam:${r.appId}`));
    toast(`Enlazado con «${r.name}» en Steam.`);
    queueCovers(true);
  } catch (err) { toast(err.message); }
}

// ───────────────────────────── Acciones sobre juegos ─────────────────────────────
function targetBlockForNew(s) {
  const x = S.selected && findEntry(S.selected);
  if (x && x.s === s) return x.b;
  const cats = categoriesOf(s);
  if (cats.length) return cats[cats.length - 1];
  const b = { type: 'category', name: '', style: 'none', entries: [] };
  s.blocks.unshift(b);
  return b;
}
function addEntry(block) {
  const s = curSection();
  const e = { id: uid(), title: '', fields: [] };
  mutate('añadir juego', () => {
    const b = block || targetBlockForNew(s);
    const x = S.selected && findEntry(S.selected);
    const at = x && x.b === b ? b.entries.indexOf(x.e) + 1 : b.entries.length;
    b.entries.splice(at, 0, e);
    if (isDone(s)) e.date = today();
    S.selected = e.id;
  });
  focusDetailTitle();
  announce('Juego nuevo añadido. Escribe el título.');
}
function deleteEntry(id) {
  const x = findEntry(id); if (!x) return;
  const items = [...document.querySelectorAll('#main .card, #main .row')];
  const i = items.findIndex(el => el.dataset.id === id);
  const next = items[i + 1] || items[i - 1];
  mutate('eliminar juego', () => { x.b.entries.splice(x.b.entries.indexOf(x.e), 1); S.selected = next?.dataset.id || null; });
  restoreFocusToSelected();
  toast(`Eliminado «${x.e.title || 'Sin título'}»`, { label: 'Deshacer', fn: undo });
}
function duplicateEntry(id) {
  const x = findEntry(id); if (!x) return;
  const c = { ...JSON.parse(JSON.stringify(x.e)), id: uid(), raw: undefined };
  mutate('duplicar juego', () => { x.b.entries.splice(x.b.entries.indexOf(x.e) + 1, 0, c); S.selected = c.id; });
  focusDetailTitle();
}
function moveEntryStep(id, dir) {
  const x = findEntry(id); if (!x) return;
  const cats = categoriesOf(x.s);
  const i = x.b.entries.indexOf(x.e);
  mutate('mover juego', () => {
    if (i + dir >= 0 && i + dir < x.b.entries.length) {
      x.b.entries.splice(i, 1); x.b.entries.splice(i + dir, 0, x.e);
    } else {
      const ci = cats.indexOf(x.b) + dir;
      if (ci < 0 || ci >= cats.length) return;
      x.b.entries.splice(i, 1);
      dir < 0 ? cats[ci].entries.push(x.e) : cats[ci].entries.unshift(x.e);
    }
  });
  restoreFocusToSelected();
  const y = findEntry(id);
  announce(`«${x.e.title}» en ${blockLabel(y.b)}, posición ${y.b.entries.indexOf(y.e) + 1} de ${y.b.entries.length}`);
}
/** Bloque de categoría con ese nombre y estilo en la sección (lo crea antes de las notas finales). */
function ensureCategory(s, name, style) {
  if (style === 'none' || !name) {
    let b = s.blocks.find(b => b.type === 'category' && b.style === 'none');
    if (!b) { b = { type: 'category', name: '', style: 'none', entries: [] }; s.blocks.unshift(b); }
    return b;
  }
  let b = s.blocks.find(b => b.type === 'category' && b.style !== 'none' && norm(b.name) === norm(name));
  if (b) return b;
  b = { type: 'category', name, style, entries: [] };
  let at = s.blocks.length;
  while (at > 0 && s.blocks[at - 1].type === 'divider') at--;
  s.blocks.splice(at, 0, b);
  return b;
}
function moveToSection(id, si) {
  const x = findEntry(id); const target = sections()[si];
  if (!x || !target || x.s === target) return;
  mutate(`mover a ${target.name}`, () => {
    x.b.entries.splice(x.b.entries.indexOf(x.e), 1);
    ensureCategory(target, x.b.name, x.b.style).entries.push(x.e);
    if (isDone(target) && !x.e.date) x.e.date = today();
  });
  toast(`«${x.e.title}» movido a ${target.name}`, { label: 'Deshacer', fn: undo });
}
function markPassed(id) {
  const x = findEntry(id); if (!x) return;
  const si = sections().findIndex(s => sectionKind(s) === 'completed');
  if (si < 0) return;
  moveToSection(id, si);
}
function moveEntryTo(id, sec, block, beforeId) {
  const x = findEntry(id); if (!x) return;
  if (beforeId === id) return;
  mutate('mover juego', () => {
    x.b.entries.splice(x.b.entries.indexOf(x.e), 1);
    const at = beforeId ? block.entries.findIndex(e => e.id === beforeId) : -1;
    at >= 0 ? block.entries.splice(at, 0, x.e) : block.entries.push(x.e);
    if (sec !== x.s && isDone(sec) && !x.e.date) x.e.date = today();
  });
  announce(`«${x.e.title}» movido a ${blockLabel(block)}`);
}
async function changeCategory(id, value) {
  const x = findEntry(id); if (!x) return;
  let target;
  if (value === 'new') {
    const r = await promptCategory();
    if (!r) { renderDetail(); return; }
    target = { name: r.name, style: r.paren ? 'paren' : 'colon' };
  } else if (value === 'none') target = { name: '', style: 'none' };
  else if (typeof value === 'object') target = { name: value.name, style: value.paren ? 'paren' : 'colon' };
  else { const b = x.s.blocks[+value]; target = { block: b }; }
  mutate('cambiar saga', () => {
    x.b.entries.splice(x.b.entries.indexOf(x.e), 1);
    (target.block || ensureCategory(x.s, target.name, target.style)).entries.push(x.e);
  });
  const y = findEntry(id);
  announce(`«${x.e.title}» ahora está en ${blockLabel(y.b)}`);
}

// ───────────────────────────── Categorías, separadores y secciones ─────────────────────────────
async function promptCategory(initial = '', paren = false, title = 'Nueva saga o género') {
  const r = await prompt({
    title, label: 'Nombre', value: initial, list: S.sagas,
    hint: 'Una saga se escribe «Nombre:»; un género, entre paréntesis como «(Indies)».',
    check: { label: 'Es un género (se escribe entre paréntesis)', checked: paren }
  });
  return r && r.value.trim() ? { name: r.value.trim().replace(/^\((.*)\)$/, '$1').replace(/:$/, ''), paren: r.checked || /^\(.*\)$/.test(r.value.trim()) } : null;
}
async function addCategory() {
  const r = await promptCategory();
  if (!r) return;
  const s = curSection();
  mutate('nueva saga', () => ensureCategory(s, r.name, r.paren ? 'paren' : 'colon'));
  announce(`Saga «${r.name}» creada`);
}
async function renameCategory(s, b) {
  const r = await promptCategory(b.name, b.style === 'paren', 'Renombrar');
  if (!r) return;
  mutate('renombrar saga', () => { b.name = r.name; b.style = r.paren ? 'paren' : 'colon'; });
}
function moveBlock(s, b, dir) {
  const i = s.blocks.indexOf(b), j = i + dir;
  if (j < 0 || j >= s.blocks.length) return;
  mutate('mover saga', () => { s.blocks.splice(i, 1); s.blocks.splice(j, 0, b); });
}
async function deleteCategory(s, b) {
  const n = b.entries.length;
  if (n) {
    const v = await confirmDlg(`Eliminar «${blockLabel(b)}»`, `Tiene ${plural(n, 'juego', 'juegos')}. ¿Qué quieres hacer con ellos?`,
      [{ label: 'Cancelar', value: 'cancel' }, { label: 'Dejarlos sin categoría', value: 'keep' }, { label: 'Eliminarlos también', value: 'all', danger: true }]);
    if (v === 'cancel' || !v) return;
    mutate('eliminar saga', () => {
      const i = s.blocks.indexOf(b); s.blocks.splice(i, 1);
      if (v === 'keep') ensureCategory(s, '', 'none').entries.push(...b.entries);
    });
  } else mutate('eliminar saga', () => s.blocks.splice(s.blocks.indexOf(b), 1));
  toast(`«${blockLabel(b)}» eliminada`, { label: 'Deshacer', fn: undo });
}
async function editDivider(s, b) {
  const r = await prompt({ title: 'Nota separadora', label: 'Texto', value: b.name, hint: 'En el .txt se escribe entre corchetes: [texto].' });
  if (!r) return;
  mutate('editar nota', () => { b.name = r.value.trim(); b.style = 'bracket'; });
}
async function sectionAction(act) {
  const s = curSection();
  if (act === 'add') {
    const free = SUGGESTED_SECTIONS.map(tr).filter(n => !sections().some(x => norm(x.name) === norm(n) || norm(tr(x.name)) === norm(n)));
    const r = await prompt({ title: 'Nueva sección', label: 'Nombre', value: free[0] || '', list: free, hint: 'En el .txt se escribe como <Nombre>.', chips: free });
    if (!r || !r.value.trim()) return;
    mutate('nueva sección', () => { sections().push({ name: r.value.trim(), blocks: [] }); S.sec = sections().length - 1; });
  } else if (act === 'rename') {
    const r = await prompt({ title: 'Renombrar sección', label: 'Nombre', value: s.name,
      hint: s.implicit ? 'Es la primera sección: en el .txt no lleva línea propia, así que el nombre solo se ve en la app y en el .md.' : 'En el .txt se escribe como <Nombre>.' });
    if (!r || !r.value.trim()) return;
    mutate('renombrar sección', () => { s.name = r.value.trim(); });
  } else if (act === 'left' || act === 'right') {
    const j = S.sec + (act === 'left' ? -1 : 1);
    if (j < 0 || j >= sections().length) return;
    if (s.implicit || sections()[j].implicit) { toast('La primera sección del .txt no lleva nombre, así que tiene que ir la primera.'); return; }
    mutate('mover sección', () => { sections().splice(S.sec, 1); sections().splice(j, 0, s); S.sec = j; });
  } else if (act === 'divider') {
    const r = await prompt({ title: 'Nota separadora', label: 'Texto', value: '', hint: 'Aparece al final de esta sección. En el .txt: [texto].' });
    if (!r || !r.value.trim()) return;
    mutate('añadir nota', () => s.blocks.push({ type: 'divider', name: r.value.trim(), style: 'bracket', entries: [] }));
  } else if (act === 'delete') {
    if (s.implicit) { toast('La primera sección no se puede eliminar, pero puedes vaciarla.'); return; }
    const n = sectionCount(s);
    const v = await confirmDlg(`Eliminar «${s.name}»`, n ? `Se eliminarán también sus ${plural(n, 'juego', 'juegos')}. Puedes deshacerlo con Ctrl+Z.` : 'La sección está vacía.',
      [{ label: 'Cancelar', value: 'cancel' }, { label: 'Eliminar', value: 'ok', danger: true }]);
    if (v !== 'ok') return;
    mutate('eliminar sección', () => { sections().splice(S.sec, 1); S.sec = Math.max(0, S.sec - 1); });
    toast(`Sección «${s.name}» eliminada`, { label: 'Deshacer', fn: undo });
  }
}

// ───────────────────────────── Vista de texto ─────────────────────────────
async function renderText() {
  const main = $('#main');
  main.removeAttribute('role');
  const ta = h('textarea', { id: 'rawText', 'aria-label': `Contenido del archivo en formato ${S.textKind === 'md' ? 'Markdown' : 'texto'}`, spellcheck: 'false', readonly: !S.textEditing });
  const bar = h('div', { class: 'listtools' },
    h('div', { class: 'seg', role: 'radiogroup', 'aria-label': 'Formato' },
      ...['txt', 'md'].map(k => h('button', { role: 'radio', 'aria-checked': String(S.textKind === k), style: 'width:auto;padding:0 12px', disabled: S.textEditing,
        onclick: () => { S.textKind = k; renderText(); } }, k === 'txt' ? '.txt' : '.md'))),
    h('span', { class: 'muted', style: 'font-size:13px' }, S.textEditing ? 'Editando el texto: al aplicar se vuelve a leer la lista.' : 'Vista previa de lo que se guardará.'),
    h('span', { class: 'grow' }),
    S.textEditing
      ? [h('button', { class: 'btn btn--secondary', onclick: () => { S.textEditing = false; renderText(); } }, 'Cancelar'),
         h('button', { class: 'btn btn--primary', onclick: applyText }, icon('check'), 'Aplicar cambios')]
      : [h('button', { class: 'btn btn--ghost', onclick: () => { navigator.clipboard?.writeText(ta.value); toast('Texto copiado'); } }, icon('copy'), 'Copiar'),
         h('button', { class: 'btn btn--secondary', onclick: () => { S.textEditing = true; renderText(); } }, icon('pencil'), 'Editar el texto')]);
  const wrap = h('div', { class: 'textview' }, bar, ta);
  main.replaceChildren(wrap);
  const r = await Host.call('file.serialize', { doc: S.doc, kind: S.textKind });
  ta.value = r?.text ?? '';
  if (S.textEditing) ta.focus();
}
async function applyText() {
  const text = $('#rawText').value;
  try {
    const r = await Host.call('file.parse', { text, kind: S.textKind });
    if (!r) return;
    const before = snapshot();
    S.doc.header = r.doc.header; S.doc.hasHeader = r.doc.hasHeader; S.doc.sections = r.doc.sections;
    if (S.textKind === 'md' && r.doc.markdown) S.doc.markdown = r.doc.markdown;
    commit(before, 'editar el texto');
    S.textEditing = false;
    render();
    toast('Texto aplicado');
  } catch (err) { toast(`No se pudo leer el texto: ${err.message}`); }
}
function toggleText() {
  if (!S.doc) return;
  S.textMode = !S.textMode; S.textEditing = false; S.statsMode = false;
  if (S.textMode) S.textKind = S.kind;
  $('#btnText').setAttribute('aria-pressed', String(S.textMode));
  render();
}

// ───────────────────────────── Pantalla de inicio ─────────────────────────────
function renderStart() {
  $('#detail').hidden = true;
  $('#workspace').classList.add('no-detail');
  const main = $('#main');
  main.removeAttribute('role');
  main.className = 'listpane start dropzone';
  main.replaceChildren(h('div', { class: 'start__inner' },
    h('h1', null, 'Tus juegos, en tu propio archivo.'),
    h('p', { class: 'lead' }, 'Abre tu lista en .txt o .md o empieza una nueva. Lo que guardes seguirá siendo texto que puedes leer en cualquier sitio.'),
    h('div', { class: 'start__actions' },
      h('button', { class: 'bigcard', onclick: openFile }, h('span', { class: 'appname__mark' }, icon('folder-open', 'i i--lg')), h('b', null, 'Abrir una lista'), h('span', null, 'Un .txt o .md que ya tengas. También puedes arrastrarlo aquí.')),
      h('button', { class: 'bigcard', onclick: newFile }, h('span', { class: 'appname__mark' }, icon('file-plus', 'i i--lg')), h('b', null, 'Crear una lista nueva'), h('span', null, 'Con las secciones Completados, Inconclusos y Por jugar.'))),
    S.recent.length ? h('div', null,
      h('h2', { style: 'font-size:18px;margin-bottom:8px' }, 'Abiertos hace poco'),
      h('ul', { class: 'recent' }, ...S.recent.map(r => h('li', null, h('button', { onclick: () => openFile(r.path) },
        h('img', { class: 'brand', src: /\.md$/i.test(r.path) ? 'icons/markdown.svg' : 'icons/text.svg', alt: '' }),
        h('span', null, h('b', { style: 'font-weight:500' }, r.path.split(/[\\/]/).pop()), h('span', { class: 'path' }, r.path))))))) : null));
}

// ───────────────────────────── Archivos ─────────────────────────────
function loadFile(f) {
  S.doc = f.doc; S.path = f.path || null; S.kind = f.kind || 'txt'; S.encoding = f.encoding || 'utf-8'; S.newLine = f.newLine || 'CRLF';
  S.sec = 0; S.undo = []; S.redo = []; S.saved = snapshot(); S.textMode = false; S.statsMode = false; S.detailOpen = false; S.query = ''; $('#q').value = ''; S.platform = '';
  S.selected = sections()[0]?.blocks.flatMap(b => b.entries || [])[0]?.id || null;
  $('#main').className = 'listpane';
  $('#extBar').hidden = true;
  lastDirty = null; dirtyChanged();
  if (f.recent) S.recent = f.recent;
  render();
  enter();
  announce(S.path ? `Abierto ${fileTitle()}: ${plural([...allEntries()].length, 'juego', 'juegos')}` : 'Lista nueva');
}
// Entrada al abrir: una sola vez por archivo (app.css, .is-entering). Con «reducir movimiento» no se anima nada.
let enterTimer;
function enter() {
  const app = $('#app');
  app.classList.remove('is-entering');
  void app.offsetWidth;
  app.classList.add('is-entering');
  clearTimeout(enterTimer);
  enterTimer = setTimeout(() => app.classList.remove('is-entering'), 1500);
}
async function confirmDiscard() {
  if (!isDirty()) return true;
  const v = await confirmDlg('¿Guardar los cambios?', `Hay cambios sin guardar en «${fileTitle()}».`,
    [{ label: 'Cancelar', value: 'cancel' }, { label: 'No guardar', value: 'discard' }, { label: 'Guardar', value: 'save', primary: true }]);
  if (v === 'save') return await save();
  return v === 'discard';
}
async function newFile() {
  if (!(await confirmDiscard())) return;
  const r = await Host.call('file.new');
  if (r) loadFile({ doc: r.doc, path: null, kind: 'txt' });
}
async function openFile(path) {
  if (typeof path !== 'string') path = undefined;
  if (!(await confirmDiscard())) return;
  try {
    const r = await Host.call('file.open', { path });
    if (r) loadFile(r);
  } catch (err) { toast(`No se pudo abrir: ${err.message}`); }
}
async function save(as) {
  if (!S.doc) return false;
  try {
    if (editBefore != null) endEdit('editar');
    const r = await Host.call('file.save', { doc: S.doc, path: as ? null : S.path, as: as || null });
    if (!r) return false;
    const before = snapshot();
    S.doc.header = r.doc.header; S.doc.layout = r.doc.layout;
    S.path = r.path; S.kind = r.kind; S.encoding = r.encoding; S.newLine = r.newLine || S.newLine;
    if (before !== snapshot()) S.undo.push({ doc: before, sel: S.selected, sec: S.sec, label: 'fecha de actualización' });
    S.saved = snapshot();
    if (r.recent) S.recent = r.recent;
    $('#extBar').hidden = true;
    lastDirty = null; dirtyChanged(); render();
    toast(r.upgraded ? `Guardado en ${fileTitle()} (ahora en UTF-8 para admitir ★ y emojis)` : `Guardado en ${fileTitle()}`);
    return true;
  } catch (err) { toast(`No se pudo guardar: ${err.message}`); return false; }
}

// ───────────────────────────── Portadas ─────────────────────────────
function coverKey(e) {
  const sg = F.sgdb(e);
  if (sg) return `sgdb:${sg}`;
  const st = F.store(e);
  if (st && (st.store === 'steam' || st.store === 'xbox') && st.id !== '?') return `${st.store}:${st.id}`;
  return 't:' + norm(e.title).replace(/\s+/g, ' ').trim();
}
let coverTimer;
function queueCovers(force, refresh) {
  if (!S.settings.covers || !S.doc) return;
  clearTimeout(coverTimer);
  coverTimer = setTimeout(() => {
    const items = [];
    for (const { e } of allEntries()) {
      const k = coverKey(e);
      // refresh: volver a pedir también las que ya tienen portada (al poner la clave de SteamGridDB)
      if (!e.title || (S.covers.has(k) && !refresh) || (S.coverReq.has(k) && !force)) continue;
      S.coverReq.add(k);
      const st = F.store(e);
      items.push({ key: k, title: e.title, steam: st?.store === 'steam' ? st.id : null, xbox: st?.store === 'xbox' ? st.id : null, sgdb: F.sgdb(e) });
    }
    if (items.length) Host.call('covers.resolve', { items });
  }, 250);
}
Host.on('cover', d => {
  if (!d.url) return;
  S.covers.set(d.key, d.url);
  for (const { e } of allEntries()) if (coverKey(e) === d.key) updateItem(e.id);
  const x = S.selected && findEntry(S.selected);
  if (x && coverKey(x.e) === d.key) {
    const old = document.querySelector('.detail__thumb');
    if (old) old.replaceWith(coverEl(x.e, 'detail__thumb'));
    document.querySelector('.detail__ambient')?.replaceWith(ambientEl(x.e));
  }
});

// ───────────────────────────── Diálogos ─────────────────────────────
function prompt({ title, label, value = '', hint, list, check, chips }) {
  const dlg = $('#dlgPrompt');
  $('#promptTitle').textContent = tr(title);
  const dl = list?.length ? h('datalist', { id: 'promptList' }, ...list.map(v => h('option', { value: v }))) : null;
  const inp = h('input', { id: 'promptInput', type: 'text', value, autocomplete: 'off', list: dl ? 'promptList' : null, required: true, 'aria-describedby': hint ? 'promptHint' : null });
  const cb = check ? h('input', { type: 'checkbox', id: 'promptCheck', class: 'm-check', checked: check.checked }) : null;
  $('#promptBody').replaceChildren(
    h('div', { class: 'field' }, h('label', { for: 'promptInput' }, label), inp, dl, hint ? h('span', { class: 'hint', id: 'promptHint' }, hint) : null),
    chips?.length ? h('div', { class: 'suggest', role: 'group', 'aria-label': 'Sugerencias' }, ...chips.map(c => h('button', { type: 'button', onclick: () => { inp.value = c; inp.focus(); } }, c))) : null,
    cb ? h('label', { class: 'inline', style: 'cursor:pointer' }, cb, check.label) : null);
  return new Promise(res => {
    dlg.returnValue = '';
    dlg.onclose = () => res(dlg.returnValue === 'ok' ? { value: inp.value, checked: cb?.checked || false } : null);
    dlg.showModal();
    inp.focus(); inp.select();
  });
}
function confirmDlg(title, text, buttons) {
  const dlg = $('#dlgConfirm');
  $('#confirmTitle').textContent = tr(title);
  $('#confirmText').textContent = tr(text);
  $('#confirmFoot').replaceChildren(...buttons.map(b => h('button', { class: `btn ${b.primary ? 'btn--primary' : b.danger ? 'btn--secondary btn--danger' : 'btn--secondary'}`, value: b.value }, b.label)));
  return new Promise(res => {
    dlg.returnValue = '';
    dlg.onclose = () => res(dlg.returnValue || 'cancel');
    dlg.showModal();
    (dlg.querySelector('.btn--primary') || dlg.querySelector('.btn--danger') || dlg.querySelector('button'))?.focus();
  });
}
function closeOnBackdrop(dlg) { dlg.addEventListener('click', ev => { if (ev.target === dlg) dlg.close('cancel'); }); }

// Campos de las cuentas (Ajustes › Cuentas y paso 3 de la bienvenida). reopen vuelve a pintar el diálogo tras guardar.
function accountFields(st, reopen) {
  const keyStatus = (has) => h('span', { class: has ? 'done-mark' : 'muted', style: 'font-size:12.5px' }, has ? [icon('circle-check', 'i i--sm'), ' Guardada y cifrada en este PC'] : 'Sin configurar');
  return [
    h('div', { class: 'field' },
      h('div', { class: 'inline' }, h('span', { class: 'option', style: 'padding:0;border:0;background:none;display:inline' }, h('span', { class: 'brand-text' }, 'SG')), h('b', { style: 'font-weight:500' }, 'SteamGridDB (portadas)'), h('span', { class: 'grow' }), keyStatus(st.hasSgdbKey)),
      h('label', { for: 'sSgdbKey' }, 'Clave de la API de SteamGridDB'),
      h('input', { id: 'sSgdbKey', type: 'password', autocomplete: 'off', placeholder: st.hasSgdbKey ? '•••••••• (guardada)' : '32 caracteres' }),
      h('span', { class: 'hint' }, 'Carátulas 2:3 hechas por la comunidad, también de juegos de Nintendo y PlayStation. Inicia sesión en ',
        h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); Host.call('open.url', { url: 'https://www.steamgriddb.com/profile/preferences/api' }); } }, 'steamgriddb.com › Preferencias › API'),
        ' y genera la clave gratuita. Se envía el título de cada juego a SteamGridDB.')),
    h('div', { class: 'field' },
      h('div', { class: 'inline' }, h('img', { class: 'brand ic-ink', src: 'icons/steam.svg', alt: '' }), h('b', { style: 'font-weight:500' }, 'Steam'), h('span', { class: 'grow' }), keyStatus(st.hasSteamKey)),
      h('div', { class: 'row2' },
        h('div', { class: 'field' }, h('label', { for: 'sSteamKey' }, 'Clave de la Steam Web API'), h('input', { id: 'sSteamKey', type: 'password', autocomplete: 'off', placeholder: st.hasSteamKey ? '•••••••• (guardada)' : '32 caracteres' })),
        h('div', { class: 'field' }, h('label', { for: 'sSteamId' }, 'SteamID64 o enlace del perfil'), h('input', { id: 'sSteamId', type: 'text', autocomplete: 'off', value: st.steamId || '', placeholder: 'https://steamcommunity.com/id/…' }))),
      h('span', { class: 'hint' }, 'Consigue la clave en ', h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); Host.call('open.url', { url: 'https://steamcommunity.com/dev/apikey' }); } }, 'steamcommunity.com/dev/apikey'),
        '. Tu perfil debe tener los «Detalles del juego» en público.')),
    h('div', { class: 'field' },
      h('div', { class: 'inline' }, h('span', { class: 'option', style: 'padding:0;border:0;background:none;display:inline' }, h('span', { class: 'brand-text' }, 'XB')), h('b', { style: 'font-weight:500' }, 'Xbox (OpenXBL)'), h('span', { class: 'grow' }), keyStatus(st.hasXblKey)),
      h('label', { for: 'sXblKey' }, 'Clave de OpenXBL'),
      h('input', { id: 'sXblKey', type: 'password', autocomplete: 'off', placeholder: st.hasXblKey ? '•••••••• (guardada)' : 'Tu clave de xbl.io' }),
      h('span', { class: 'hint' }, 'OpenXBL es un servicio de terceros (no de Microsoft) con clave gratuita en ',
        h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); Host.call('open.url', { url: 'https://xbl.io' }); } }, 'xbl.io'), '. Los juegos instalados en este PC se detectan sin clave.')),
    h('div', { class: 'field' },
      h('div', { class: 'inline' }, h('span', { class: 'option', style: 'padding:0;border:0;background:none;display:inline' }, h('span', { class: 'brand-text' }, 'RA')), h('b', { style: 'font-weight:500' }, 'RetroAchievements'), h('span', { class: 'grow' }), keyStatus(st.hasRaKey && !!st.raUser)),
      h('div', { class: 'row2' },
        h('div', { class: 'field' }, h('label', { for: 'sRaUser' }, 'Usuario de RetroAchievements'), h('input', { id: 'sRaUser', type: 'text', autocomplete: 'off', spellcheck: 'false', value: st.raUser || '', placeholder: 'Tu nombre de usuario' })),
        h('div', { class: 'field' }, h('label', { for: 'sRaKey' }, 'Web API Key'), h('input', { id: 'sRaKey', type: 'password', autocomplete: 'off', placeholder: st.hasRaKey ? '•••••••• (guardada)' : '32 caracteres' }))),
      h('span', { class: 'hint' }, 'Juegos retro jugados con logros, incluido el modo Hardcore. La clave está en ',
        h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); Host.call('open.url', { url: 'https://retroachievements.org/settings' }); } }, 'retroachievements.org › Ajustes › Claves'),
        '. Los emuladores de este PC (RetroArch, Dolphin, PCSX2, DuckStation) se detectan sin cuenta.')),
    h('div', { class: 'field' },
      h('div', { class: 'inline' }, h('span', { class: 'option', style: 'padding:0;border:0;background:none;display:inline' }, h('span', { class: 'brand-text' }, 'DB')), h('b', { style: 'font-weight:500' }, 'IGDB (duración de los juegos)'), h('span', { class: 'grow' }), keyStatus(st.hasIgdbKey)),
      h('div', { class: 'row2' },
        h('div', { class: 'field' }, h('label', { for: 'sIgdbId' }, 'Client ID de Twitch'), h('input', { id: 'sIgdbId', type: 'text', autocomplete: 'off', spellcheck: 'false', value: st.igdbClientId || '', placeholder: '30 caracteres' })),
        h('div', { class: 'field' }, h('label', { for: 'sIgdbSecret' }, 'Secreto del cliente'), h('input', { id: 'sIgdbSecret', type: 'password', autocomplete: 'off', placeholder: st.hasIgdbKey ? '•••••••• (guardado)' : '30 caracteres' }))),
      h('span', { class: 'hint' }, 'Rellena la duración estimada con los tiempos que envían los jugadores a IGDB (la base de datos de Twitch). Crea una aplicación gratuita en ',
        h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); Host.call('open.url', { url: 'https://dev.twitch.tv/console/apps' }); } }, 'dev.twitch.tv › Consola › Aplicaciones'),
        ' (URL de redirección: http://localhost, tipo: confidencial) y copia aquí su Client ID y su secreto. Solo se envía el título del juego, y solo al pedir la duración. HowLongToBeat no permite consultas automáticas: desde el inspector se abre su búsqueda en el navegador.')),
    h('div', { class: 'inline' },
      h('button', { class: 'btn btn--primary', onclick: async () => {
        const sgdbKey = $('#sSgdbKey').value.trim() || null;
        const r = await Host.call('secrets.set', { steamKey: $('#sSteamKey').value || null, steamId: $('#sSteamId').value, xblKey: $('#sXblKey').value || null, sgdbKey,
          raUser: $('#sRaUser').value, raKey: $('#sRaKey').value.trim() || null,
          igdbClientId: $('#sIgdbId').value, igdbSecret: $('#sIgdbSecret').value.trim() || null });
        if (r) S.settings = r;
        if ($('#sIgdbSecret').value.trim() && S.settings.hasIgdbKey) {
          // Se comprueban el Client ID y el secreto al momento con una búsqueda de prueba
          const c = await Host.call('igdb.check');
          if (c && !c.ok) { toast(c.error || 'IGDB ha rechazado el acceso.'); reopen(); return; }
          toast('IGDB conectado: ya puedes buscar duraciones desde el inspector.');
          reopen(); return;
        }
        if (sgdbKey) {
          // Se comprueba la clave al momento y, si vale, se vuelven a buscar las portadas en SteamGridDB
          const c = await Host.call('sgdb.check');
          if (c && !c.ok) { toast(c.error || 'SteamGridDB ha rechazado la clave.'); reopen(); return; }
          if (S.settings.covers) queueCovers(true, true);
          toast(S.settings.covers ? 'Clave de SteamGridDB guardada: buscando portadas…' : 'Clave de SteamGridDB guardada. Activa «Mostrar portadas» para usarla.');
        } else toast('Cuentas guardadas');
        reopen();
      } }, icon('save'), 'Guardar cuentas'),
      h('button', { class: 'btn btn--ghost btn--danger', onclick: async () => { const r = await Host.call('secrets.set', { clear: true }); if (r) S.settings = r; toast('Claves borradas'); reopen(); } }, 'Borrar las claves'))
  ];
}

// ── Ajustes ──
function openSettings() {
  $('#dlgWelcome').replaceChildren();   // la bienvenida usa los mismos id en los campos de cuentas
  const dlg = $('#dlgSettings');
  const st = S.settings;
  const md = st.markdown || {};
  const sw = (id, on, onchange) => h('button', { type: 'button', role: 'switch', class: 'm-switch', id, 'aria-checked': String(!!on),
    onclick: ev => { const v = ev.currentTarget.getAttribute('aria-checked') !== 'true'; ev.currentTarget.setAttribute('aria-checked', String(v)); onchange(v); } });
  const setting = (id, title, desc, on, fn) => h('div', { class: 'setting' }, h('div', null, h('label', { for: id, style: 'font-weight:500' }, title), desc ? h('small', { id: id + 'D' }, desc) : null), sw(id, on, fn));
  const setS = async patch => { Object.assign(st, patch); S.settings = await Host.call('settings.set', patch) || st; applyTheme(); render(); };
  const mdOpt = (k, title, desc) => setting('md_' + k, title, desc, md[k], v => { md[k] = v; S.doc && (S.doc.markdown = { ...S.doc.markdown, [k]: v }); setS({ markdown: md }); });
  const keyStatus = (has) => h('span', { class: has ? 'done-mark' : 'muted', style: 'font-size:12.5px' }, has ? [icon('circle-check', 'i i--sm'), ' Guardada y cifrada en este PC'] : 'Sin configurar');

  dlg.replaceChildren(h('div', { class: 'dlg__head' }, h('h2', { id: 'settingsTitle' }, 'Ajustes'),
    h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar', onclick: () => dlg.close() }, icon('x'))),
  h('div', { class: 'dlg__body' },
    h('h3', { class: 'sub' }, 'Apariencia'),
    h('div', { class: 'setting' }, h('div', null, h('label', { for: 'sTheme', style: 'font-weight:500' }, 'Tema'), h('small', null, 'Sistema sigue el modo claro u oscuro de Windows.')),
      h('select', { id: 'sTheme', class: 'select', onchange: ev => setS({ theme: ev.target.value }) },
        ...[['system', 'Sistema'], ['dark', 'Oscuro'], ['light', 'Claro']].map(([v, t]) => h('option', { value: v, selected: st.theme === v }, t)))),
    h('div', { class: 'setting' }, h('div', null, h('label', { for: 'sLang', style: 'font-weight:500' }, 'Idioma'), h('small', null, 'Sistema usa el idioma de Windows.')),
      h('select', { id: 'sLang', class: 'select', onchange: async ev => { await setS({ language: ev.target.value }); setLanguage(ev.target.value); openSettings(); $('#sLang')?.focus(); } },
        h('option', { value: 'system', selected: (st.language || 'system') === 'system' }, `${tr('Sistema')} (${I18N.systemName})`),
        ...I18N.LANGS.map(l => h('option', { value: l.code, selected: st.language === l.code, lang: l.code, 'data-notr': '' }, document.createTextNode(l.name))))),
    setting('sCovers', 'Mostrar portadas', st.hasSgdbKey
      ? 'Busca cada título en SteamGridDB (con tu clave) y, si no la encuentra, en la tienda de Steam. Las imágenes se guardan en este PC.'
      : 'Busca cada título en la tienda de Steam (sin cuenta) y guarda las imágenes en este PC. Con una clave de SteamGridDB (abajo, en Cuentas) también salen las de Nintendo y PlayStation.', st.covers,
      v => { setS({ covers: v }); if (v) queueCovers(true); }),

    h('h3', { class: 'sub' }, 'Al abrir la app'),
    h('div', { class: 'setting' }, h('div', null, h('label', { for: 'sResume', style: 'font-weight:500' }, 'Última lista abierta'), h('small', null, 'Qué hacer al abrir Checkpoint sin indicar un archivo.')),
      h('select', { id: 'sResume', class: 'select', onchange: ev => setS({ resume: ev.target.value }) },
        ...[['ask', 'Preguntar si continúo'], ['always', 'Abrirla directamente'], ['never', 'Empezar en la pantalla de inicio']].map(([v, l]) => h('option', { value: v, selected: (st.resume || 'ask') === v }, l)))),
    h('div', { class: 'setting' }, h('div', null, h('span', { style: 'font-weight:500' }, 'Pantalla de bienvenida'), h('small', null, 'Vuelve a ver los pasos de la primera vez: tema, cuentas y lista.')),
      h('button', { class: 'btn btn--secondary btn--sm', onclick: () => { dlg.close(); openWelcome(0); } }, 'Abrir la bienvenida')),

    h('h3', { class: 'sub' }, 'Al guardar'),
    setting('sDate', 'Poner al día «(Actualizado el …)»', 'Cambia la fecha de la primera línea a la de hoy cada vez que guardas.', st.updateDate, v => setS({ updateDate: v })),
    setting('sIds', 'Guardar el id de la tienda al importar', 'Añade {steam:123} o {xbox:…} a los juegos importados para las portadas, los enlaces y «Actualizar datos».', st.writeStoreIds, v => setS({ writeStoreIds: v })),
    setting('sBak', 'Copia de seguridad .bak', 'La primera vez que guardas en cada sesión se copia el archivo anterior como .bak.', st.backup, v => setS({ backup: v })),

    h('h3', { class: 'sub' }, 'Markdown (.md)'),
    mdOpt('frontMatter', 'Cabecera YAML', 'Bloque --- con el título, la fecha y estas opciones.'),
    mdOpt('emojis', 'Emojis en los títulos', '🏆 Completados, ⏸️ Inconclusos, 📋 Por jugar…'),
    mdOpt('counts', 'Número de juegos en cada sección', '## 🏆 Completados (16)'),
    mdOpt('summaryTable', 'Tabla resumen', 'Una tabla con las secciones y sus juegos al principio.'),
    mdOpt('taskList', 'Casillas de tarea', '- [x] para los completados y - [ ] para el resto.'),
    mdOpt('storeLinks', 'Enlaces a la tienda', 'El título enlaza a Steam o Microsoft Store cuando el juego tiene id.'),
    mdOpt('covers', 'Portadas en el .md', 'Miniaturas de Steam junto a cada juego enlazado.'),
    mdOpt('collapsible', 'Sagas plegables', 'Cada saga dentro de <details>, que se pliega en GitHub y Obsidian.'),
    mdOpt('rules', 'Líneas entre secciones', '--- entre una sección y la siguiente.'),

    h('h3', { class: 'sub' }, 'Cuentas'),
    h('p', { class: 'lead', style: 'font-size:13.5px' }, 'Las claves se guardan cifradas con tu usuario de Windows (DPAPI). La de SteamGridDB se usa al buscar portadas; las demás, solo cuando pulsas Importar o Actualizar datos.'),
    ...accountFields(st, openSettings)),
  h('div', { class: 'dlg__foot' }, h('span', { class: 'grow muted', style: 'font-size:12.5px' }, 'Los cambios se aplican al momento.'), h('button', { class: 'btn btn--secondary', onclick: () => dlg.close() }, 'Cerrar')));
  if (!dlg.open) dlg.showModal();
}

// ── Ayuda ──
function openHelp() {
  const dlg = $('#dlgHelp');
  const keys = [
    ['Ctrl+N', 'Lista nueva'], ['Ctrl+O', 'Abrir'], ['Ctrl+S', 'Guardar'], ['Ctrl+Mayús+S', 'Guardar como (.txt o .md)'],
    ['Ctrl+Z / Ctrl+Y', 'Deshacer / rehacer'], ['Ctrl+F', 'Buscar en todas las secciones'], ['Ctrl+Tab', 'Sección siguiente'],
    ['Insert', 'Añadir juego'], ['Intro o F2', 'Editar el juego seleccionado'], ['Supr', 'Eliminar el juego seleccionado'],
    ['Alt+↑ / Alt+↓', 'Subir o bajar el juego'], ['Ctrl+Intro', 'Marcar como completado'], ['Ctrl+J', '¿Qué juego ahora?'], ['Ctrl+E', 'Estadísticas y resumen del año'], ['Flechas', 'Moverse por los juegos'], ['Clic derecho o Mayús+F10', 'Menú rápido del juego'],
    ['Ctrl++ / Ctrl+- / Ctrl+0', 'Zoom'], ['F11', 'Pantalla completa sin bordes (Esc para salir)'], ['Esc', 'Cerrar menús y diálogos'], ['F1', 'Esta ayuda']];
  dlg.replaceChildren(h('div', { class: 'dlg__head' }, h('h2', { id: 'helpTitle' }, 'Ayuda'), h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar', onclick: () => dlg.close() }, icon('x'))),
    h('div', { class: 'dlg__body' },
      h('h3', { class: 'sub' }, 'Atajos de teclado'),
      h('table', { class: 'keys' }, h('tbody', null, ...keys.map(([k, d]) => h('tr', null, h('td', null, ...k.split(' / ').flatMap((x, i) => [i ? ' / ' : null, h('kbd', null, x)])), h('td', null, d))))),
      h('h3', { class: 'sub' }, 'Mando'),
      h('table', { class: 'keys' }, h('tbody', null, ...[
        ['Cruceta o stick izquierdo', 'Moverse por los juegos y por los campos del inspector'], ['LB / RB', 'Sección anterior o siguiente'],
        ['A', 'Abrir el juego o confirmar la edición'], ['B', 'Cerrar el inspector o un diálogo, o borrar la búsqueda'],
        ['Y', 'Marcar como completado hoy']]
        .map(([k, d]) => h('tr', null, h('td', null, ...k.split(' / ').flatMap((x, i) => [i ? ' / ' : null, h('kbd', null, x)])), h('td', null, d))))),
      h('h3', { class: 'sub' }, 'Formato del .txt'),
      h('table', { class: 'keys' }, h('tbody', null,
        ...[['Primera línea', 'Título. «(Actualizado el dd/mm/aaaa)» se pone al día al guardar.'],
          ['<Nombre>', 'Empieza una sección. Lo que va antes de la primera son los Completados.'],
          ['Nombre:', 'Saga.'], ['(Nombre)', 'Género, como (Indies).'], ['[texto]', 'Nota separadora.'],
          ['-Título - Plataforma', 'Un juego. Puede llevar [nota], #dd/mm/aaaa# y campos {…}.'],
          ['{★ 9/10} {42 h} {Logros 100%}', 'Puntuación, horas y logros.'], ['{steam:123} {xbox:9PK…} {ra:1234}', 'Tienda enlazada (también RetroAchievements).'], ['{Hardcore: 85%}', 'Logros de RetroAchievements en modo Hardcore.'], ['{ttb: 12h} {ttb: 12h / 35h}', 'Duración estimada: historia principal y, opcional, al 100 %.'], ['{clave: valor} {etiqueta}', 'Cualquier otro dato.']]
          .map(([k, d]) => h('tr', null, h('td', null, h('code', null, k)), h('td', null, d))))),
      h('p', { class: 'lead', style: 'font-size:13.5px' }, 'Las líneas que no se tocan se guardan exactamente igual que estaban. En .md se usan títulos #, casillas de tarea, enlaces, tabla resumen y plegables (ver Ajustes).')),
    h('div', { class: 'dlg__foot' }, h('button', { class: 'btn btn--secondary', onclick: () => dlg.close() }, 'Cerrar')));
  dlg.showModal();
}

// ───────────────────────────── Cambios hechos fuera de la app ─────────────────────────────
// La vigilancia está en ListaJuegos.Core.Sync.FileChangeWatcher: solo avisa si el contenido en disco cambia de verdad
// (no por los guardados propios, los .bak ni los temporales).
Host.on('fileChanged', d => {
  if (!S.doc || !S.path || norm(d.path) !== norm(S.path)) return;
  if (isDirty() || editBefore != null) showExtBar(); else reloadFromDisk(true);
});
function showExtBar() {
  const msg = `«${fileTitle()}» ha cambiado fuera de Checkpoint y tienes cambios sin guardar.`;
  $('#extText').textContent = tr(msg);
  $('#extBar').hidden = false;
  // Aviso no bloqueante: se anuncia, pero el foco se queda donde estaba
  announce(`${tr(msg)} ${tr('Elige «Recargar desde disco» o «Mantener mis cambios» en el aviso de arriba.')}`);
}
function keepMine() {
  $('#extBar').hidden = true;
  toast(S.settings.backup ? 'Se mantienen tus cambios. Al guardar, la versión del disco quedará en una copia .bak.' : 'Se mantienen tus cambios. Al guardar se sustituirá la versión del disco.');
  restoreFocusToSelected();
}
// Recarga conservando la pestaña, la búsqueda, el filtro de plataforma, el juego elegido y el desplazamiento.
// Queda en el historial: Ctrl+Z devuelve la versión de antes de recargar.
async function reloadFromDisk(auto) {
  $('#extBar').hidden = true;
  if (!S.path) return;
  const main = $('#main');
  const active = document.activeElement;
  const focusId = active && active !== document.body ? active.id : null;
  const inMain = main.contains(active);
  const secName = curSection()?.name;
  const selX = S.selected && findEntry(S.selected);
  const sel = selX ? { title: norm(selX.e.title), plats: selX.e.platforms || '' } : null;
  const scroll = main.scrollTop;
  let r;
  try { r = await Host.call('file.open', { path: S.path }); } catch (err) { toast(`No se pudo recargar: ${err.message}`); return; }
  if (!r) return;
  editBefore = null;
  const before = snapshot();
  S.doc = r.doc; S.kind = r.kind || S.kind; S.encoding = r.encoding || S.encoding; S.newLine = r.newLine || S.newLine;
  if (r.recent) S.recent = r.recent;
  S.undo.push({ doc: before, sel: null, sec: S.sec, label: 'recargar desde disco' });
  S.redo = [];
  S.saved = snapshot();
  const si = sections().findIndex(s => s.name === secName);
  S.sec = si >= 0 ? si : Math.min(S.sec, Math.max(0, sections().length - 1));
  const same = e => sel && norm(e.title) === sel.title;
  const inSec = (curSection()?.blocks || []).flatMap(b => b.entries || []);
  S.selected = (inSec.find(e => same(e) && (e.platforms || '') === sel.plats) || inSec.find(same) || inSec[0])?.id || null;
  lastDirty = null; dirtyChanged();
  render();
  main.scrollTop = scroll;
  const f = focusId && document.getElementById(focusId);
  if (f && !main.contains(f)) f.focus({ preventScroll: true }); else if (inMain) restoreFocusToSelected();
  if (auto) toast(`«${fileTitle()}» ha cambiado en el disco y se ha vuelto a cargar.`, { label: 'Deshacer', fn: undo });
  else announce('Recargado desde disco.');
}

// ───────────────────────────── Estadísticas ─────────────────────────────
// Los cálculos están en ListaJuegos.Core.Stats.LibraryStats; aquí solo se dibujan (SVG sin librerías).
let statsSeq = 0, statsResize;
const SUM = { year: new Date().getFullYear(), canvas: null, data: null, seq: 0 };
const SVGNS = 'http://www.w3.org/2000/svg';
function sv(tag, attrs = {}, ...kids) {
  const el = document.createElementNS(SVGNS, tag);
  for (const [k, v] of Object.entries(attrs || {})) if (v != null && v !== false) el.setAttribute(k, v);
  for (const c of kids) if (c != null) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}
function toggleStats() {
  if (!S.doc) return;
  if (editBefore != null) endEdit('editar');
  S.statsMode = !S.statsMode; S.textMode = false; S.textEditing = false;
  render();
  $('#main').focus({ preventScroll: true });
  announce(S.statsMode ? 'Estadísticas' : 'Lista de juegos');
}
window.addEventListener('resize', () => { if (!S.statsMode) return; clearTimeout(statsResize); statsResize = setTimeout(renderStats, 200); });

async function renderStats() {
  const main = $('#main');
  main.removeAttribute('role'); main.removeAttribute('aria-labelledby');
  const seq = ++statsSeq;
  let st;
  try { st = await Host.call('stats.compute', { doc: S.doc }); }
  catch (err) { main.replaceChildren(h('div', { class: 'msg msg--warn' }, icon('triangle-alert'), h('span', { text: err.message }))); return; }
  if (seq !== statsSeq || !S.statsMode || !st) return;
  const scroll = main.scrollTop;
  const yearNow = new Date().getFullYear();
  const inner = Math.max(280, Math.min(main.clientWidth - 48, 1180) - 34);
  const wide = inner >= 860;
  const half = wide ? Math.floor((inner - 16) / 2) : inner;
  const thisYear = st.years.find(y => y.year === yearNow)?.games || 0;
  main.replaceChildren(h('div', { class: 'statsview' },
    h('div', { class: 'statsview__head' },
      h('h2', { id: 'statsTitle' }, icon('chart-column'), 'Estadísticas'),
      h('p', { class: 'muted', text: 'Calculadas al momento con la lista abierta. Nada sale de este PC.' })),
    h('div', { class: 'kpis', role: 'list', 'aria-label': 'Cifras principales' },
      kpi('Completados', String(st.completed), `de ${plural(st.total, 'juego', 'juegos')}`),
      kpi('Horas jugadas', `${fmtNum(st.hours)} h`, st.withHours ? `anotadas en ${plural(st.withHours, 'juego', 'juegos')}` : 'Ningún juego tiene horas anotadas'),
      kpi('Nota media', st.rating != null ? `${fmtNum(st.rating)}/10` : '—', st.rated ? `de ${plural(st.rated, 'juego puntuado', 'juegos puntuados')}` : 'Ningún juego puntuado'),
      kpi(`Completados en ${yearNow}`, String(thisYear), st.undated ? `${plural(st.undated, 'completado sin fecha', 'completados sin fecha')}` : 'Todos los completados tienen fecha')),
    yearsCard(st, inner),
    h('div', { class: `statsview__pair${wide ? ' is-wide' : ''}` }, platformsCard(st, half), sagasCard(st, half)),
    summaryCard(st)));
  main.scrollTop = scroll;
  buildSummary();
}
function kpi(label, value, sub) {
  return h('div', { class: 'kpi', role: 'listitem' },
    h('span', { class: 'kpi__label', text: label }), h('b', { class: 'kpi__num', text: value }), sub ? h('span', { class: 'kpi__sub', text: sub }) : null);
}
// Tarjeta de un gráfico: título, nota, el SVG y los mismos datos en una tabla (para teclado y lectores de pantalla)
function chartCard(id, title, note, chart, cols, rows) {
  return h('section', { class: 'chart', 'aria-labelledby': id },
    h('h3', { id }, title),
    note ? h('p', { class: 'chart__note', text: note }) : null,
    chart,
    rows.length ? h('details', { class: 'chart__table' },
      h('summary', null, icon('list', 'i i--sm'), 'Ver los datos en una tabla'),
      h('table', null,
        h('thead', null, h('tr', null, ...cols.map(c => h('th', { scope: 'col' }, c)))),
        h('tbody', null, ...rows.map(r => h('tr', null, ...r.map((c, i) => i ? h('td', null, c) : h('th', { scope: 'row' }, c))))))) : null);
}
const pct = (n, total) => total ? `${Math.round(n * 100 / total)} %` : '';
function clipText(s, px, charPx = 7.3) { const n = Math.max(4, Math.floor(px / charPx)); return s.length > n ? s.slice(0, n - 1).trimEnd() + '…' : s; }
function roundTop(x, y, w, hh, r) { r = Math.min(r, w / 2, hh); return `M${x},${y + hh}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + hh}Z`; }
function roundRight(x, y, w, hh, r) { r = Math.min(r, w / 2, hh / 2); return `M${x},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + hh - r}Q${x + w},${y + hh} ${x + w - r},${y + hh}H${x}Z`; }

// Barras verticales (una serie: un solo color y sin leyenda; el título la nombra)
function barsV(items, aria, W) {
  const H = 230, top = 24, bottom = 30, n = items.length, ph = H - top - bottom;
  const max = Math.max(1, ...items.map(i => i.value));
  const slot = W / n, bw = Math.max(6, Math.min(48, slot * 0.62));
  const svg = sv('svg', { width: W, height: H, viewBox: `0 0 ${W} ${H}`, class: 'chart__svg', role: 'img', 'aria-label': aria });
  svg.append(sv('line', { x1: 0, x2: W, y1: H - bottom + 0.5, y2: H - bottom + 0.5, class: 'chart__base' }));
  const every = Math.ceil(n / Math.max(1, Math.floor(W / 52)));
  items.forEach((it, i) => {
    const x = i * slot + (slot - bw) / 2, bh = it.value ? Math.max(3, ph * it.value / max) : 0, y = H - bottom - bh;
    const g = sv('g', { class: 'chart__col' }, sv('title', null, it.title));
    g.append(sv('rect', { x: i * slot, y: 0, width: slot, height: H, class: 'chart__hit' }));
    if (bh) g.append(sv('path', { d: roundTop(x, y, bw, bh, 4), class: 'chart__bar' }));
    if (it.value && slot >= 22) g.append(sv('text', { x: x + bw / 2, y: y - 7, class: 'chart__val', 'text-anchor': 'middle' }, String(it.value)));
    if (i % every === 0 || i === n - 1) g.append(sv('text', { x: x + bw / 2, y: H - 10, class: 'chart__lbl', 'text-anchor': 'middle' }, it.label));
    svg.append(g);
  });
  return svg;
}
// Barras horizontales: nombre a la izquierda, valor al final de la barra
function barsH(items, aria, W) {
  const rowH = 32, labelW = Math.round(Math.min(220, W * 0.4)), n = items.length, H = n * rowH;
  const max = Math.max(1, ...items.map(i => i.value));
  const room = Math.max(40, W - labelW - 48);
  const svg = sv('svg', { width: W, height: H, viewBox: `0 0 ${W} ${H}`, class: 'chart__svg', role: 'img', 'aria-label': aria });
  items.forEach((it, i) => {
    const y = i * rowH, bw = Math.max(3, room * it.value / max);
    const g = sv('g', { class: 'chart__row' }, sv('title', null, it.title));
    g.append(sv('rect', { x: 0, y, width: W, height: rowH, class: 'chart__hit' }));
    g.append(sv('text', { x: 0, y: y + rowH / 2, class: 'chart__name', 'dominant-baseline': 'central' }, clipText(it.label, labelW - 12)));
    g.append(sv('path', { d: roundRight(labelW, y + 8, bw, rowH - 16, 4), class: 'chart__bar', style: it.color ? `fill:${it.color}` : null }));
    g.append(sv('text', { x: labelW + bw + 8, y: y + rowH / 2, class: 'chart__val', 'dominant-baseline': 'central' }, String(it.value)));
    svg.append(g);
  });
  return svg;
}
// Más de n filas: las últimas se juntan en «Otras» (no se inventan colores nuevos)
function foldRows(list, n) {
  if (list.length <= n) return list;
  const rest = list.slice(n - 1);
  return [...list.slice(0, n - 1), { name: tr('Otras'), games: rest.reduce((a, r) => a + r.games, 0), other: true }];
}

function yearsCard(st, W) {
  const rows = st.years;
  const note = st.undated === 1 ? tr('1 juego completado no tiene fecha y no cuenta aquí. Añade #dd/mm/aaaa# para situarlo en su año.')
    : st.undated ? tr(`${st.undated} juegos completados no tienen fecha y no cuentan aquí. Añade #dd/mm/aaaa# para situarlos en su año.`) : null;
  const chart = rows.length
    ? barsV(rows.map(r => ({ label: String(r.year), value: r.games, title: tr(`${r.year}: ${plural(r.games, 'juego', 'juegos')}`) + (r.hours ? ` · ${fmtNum(r.hours)} h` : '') + (r.rating != null ? ` · ★ ${fmtNum(r.rating)}` : '') })),
      tr(`Juegos completados por año, de ${rows[0].year} a ${rows.at(-1).year}. Máximo: ${Math.max(...rows.map(r => r.games))}.`), W)
    : h('p', { class: 'chart__empty', text: 'Ningún juego completado tiene fecha todavía.' });
  return chartCard('chYears', 'Completados por año', note, chart,
    ['Año', 'Juegos', 'Horas', 'Nota media'],
    rows.map(r => [String(r.year), String(r.games), r.hours ? `${fmtNum(r.hours)} h` : '—', r.rating != null ? `${fmtNum(r.rating)}/10` : '—']));
}
// En el tema claro se oscurecen (--fam-k) para que las barras lleguen a 3:1 sobre el fondo blanco
const famFill = f => `color-mix(in srgb, var(--fam-${f}) var(--fam-k, 100%), black)`;
const FAM_FILL = { nintendo: famFill('nintendo'), playstation: famFill('playstation'), xbox: famFill('xbox'), pc: famFill('pc'), mobile: famFill('mobile') };
function platformsCard(st, W) {
  const total = st.platforms.reduce((n, p) => n + p.games, 0);
  const rows = foldRows(st.platforms, 8);
  const chart = rows.length
    ? barsH(rows.map(p => ({ label: p.name, value: p.games, title: tr(`${p.name}: ${plural(p.games, 'juego', 'juegos')}`) + ` (${pct(p.games, total)})`,
        color: p.other ? 'var(--chart-other)' : FAM_FILL[platformFamily(p.name)] || 'var(--chart)' })),
      tr(`Plataformas de los juegos completados. La primera es ${rows[0].name} con ${plural(rows[0].games, 'juego', 'juegos')}.`), W)
    : h('p', { class: 'chart__empty', text: 'Ningún juego completado tiene plataforma.' });
  return chartCard('chPlats', 'Plataformas', 'De los juegos completados (la primera plataforma de cada uno). El color es el de su familia, como en la lista.', chart,
    ['Plataforma', 'Juegos', '%'], st.platforms.map(p => [p.name, String(p.games), pct(p.games, total)]));
}
function sagasCard(st, W) {
  const rows = st.sagas.slice(0, 8);
  const chart = rows.length
    ? barsH(rows.map(s => ({ label: s.name, value: s.games, title: tr(`${s.name}: ${plural(s.games, 'juego completado', 'juegos completados')}`) })),
      tr(`Sagas con más juegos completados. La primera es ${rows[0].name} con ${plural(rows[0].games, 'juego', 'juegos')}.`), W)
    : h('p', { class: 'chart__empty', text: 'Ningún juego completado pertenece a una saga.' });
  return chartCard('chSagas', 'Sagas con más juegos completados', 'Según la saga de tu lista o, si el juego va suelto, el diccionario de sagas.', chart,
    ['Saga', 'Juegos'], st.sagas.map(s => [s.name, String(s.games)]));
}

// ── Resumen del año (PNG) ── Se dibuja en un <canvas> en este PC: sin red. Las portadas son las ya descargadas.
function summaryCard(st) {
  const yearNow = new Date().getFullYear();
  const years = [...new Set([yearNow, SUM.year, ...st.years.filter(y => y.games).map(y => y.year)])].sort((a, b) => b - a);
  return h('section', { class: 'chart yearcard', 'aria-labelledby': 'sumTitle' },
    h('h3', { id: 'sumTitle' }, 'Resumen del año'),
    h('p', { class: 'chart__note', text: 'Una imagen con los juegos que completaste ese año, sus portadas ya descargadas, las horas y tus mejores notas. Se crea en este PC, sin conexión.' }),
    h('div', { class: 'yearcard__tools' },
      h('div', { class: 'field' }, h('label', { for: 'sumYear' }, 'Año'),
        h('select', { class: 'select', id: 'sumYear', onchange: ev => { SUM.year = +ev.target.value; buildSummary(); } },
          ...years.map(y => h('option', { value: String(y), selected: y === SUM.year }, String(y))))),
      h('button', { class: 'btn btn--secondary', id: 'sumSave', disabled: true, onclick: saveSummary }, icon('download'), 'Guardar PNG…')),
    h('p', { class: 'yearcard__status', id: 'sumStatus', role: 'status' }),
    h('div', { class: 'yearcard__preview', id: 'sumPreview' }));
}
function summaryText(d) {
  if (!d.games) return tr(`En ${d.year} no hay juegos completados con fecha.`);
  return tr(`Resumen de ${d.year}: ${plural(d.games, 'juego completado', 'juegos completados')}`) +
    (d.hours ? ` · ${fmtNum(d.hours)} h` : '') + (d.rating != null ? ` · ${tr('nota media')} ${fmtNum(d.rating)}/10` : '') +
    (d.top?.length ? ` · ${tr('mejor nota')}: ${d.top[0].title} (${fmtNum(d.top[0].rating)}/10)` : '') + '.';
}
async function buildSummary() {
  const seq = ++SUM.seq;
  const status = $('#sumStatus'), pv = $('#sumPreview'), btn = $('#sumSave');
  if (!pv) return;
  btn.disabled = true;
  status.textContent = tr('Creando la imagen…');
  let d;
  try { d = await Host.call('stats.year', { doc: S.doc, year: SUM.year }); } catch (err) { status.textContent = err.message; return; }
  if (seq !== SUM.seq || !d) return;
  const canvas = await drawYearCard(d);
  if (seq !== SUM.seq || !$('#sumPreview')) return;
  canvas.className = 'yearcard__canvas';
  canvas.setAttribute('role', 'img');
  canvas.setAttribute('aria-label', summaryText(d));
  $('#sumPreview').replaceChildren(canvas);
  SUM.canvas = canvas; SUM.data = d;
  $('#sumSave').disabled = false;
  $('#sumStatus').textContent = summaryText(d);
}
async function saveSummary() {
  if (!SUM.canvas || !SUM.data) return;
  try {
    const r = await Host.call('export.png', { data: SUM.canvas.toDataURL('image/png'), name: `${tr('Resumen')} ${SUM.data.year}.png` });
    if (r?.path) toast(`Imagen guardada en ${r.path.split(/[\\/]/).pop()}`);
  } catch (err) { toast(`No se pudo guardar la imagen: ${err.message}`); }
}
// Portada ya descargada de un juego, leída desde el mismo origen (app.lj/cache/covers) para no «contaminar» el canvas.
function localCover(id) {
  const x = findEntry(id);
  const u = x && S.covers.get(coverKey(x.e));
  if (!u) return null;
  if (u.startsWith(`https://covers.lj/`)) return 'cache/covers/' + u.slice('https://covers.lj/'.length);
  return /^mock\//.test(u) ? u : null;   // nunca una dirección de Internet
}
function loadImg(src) {
  return new Promise(res => {
    if (!src) { res(null); return; }
    const img = new Image();
    const t = setTimeout(() => res(null), 4000);
    img.onload = () => { clearTimeout(t); res(img); };
    img.onerror = () => { clearTimeout(t); res(null); };
    img.src = src;
  });
}
function fitLines(g, text, maxW, maxLines) {
  const cut = s => { if (g.measureText(s).width <= maxW) return s; while (s.length > 1 && g.measureText(s + '…').width > maxW) s = s.slice(0, -1); return s.trimEnd() + '…'; };
  const words = String(text).split(/\s+/).filter(Boolean), lines = [];
  let cur = '';
  for (const w of words) {
    const t = cur ? `${cur} ${w}` : w;
    if (!cur || g.measureText(t).width <= maxW) cur = t; else { lines.push(cur); cur = w; }
  }
  if (cur) lines.push(cur);
  if (lines.length > maxLines) { lines.length = maxLines; lines[maxLines - 1] += '…'; }
  return lines.map(cut);
}
function rrect(g, x, y, w, hh, r) { g.beginPath(); g.roundRect(x, y, w, hh, r); }
// Rejilla de carátulas 2:3: el mayor tamaño que deja ver todos los juegos (o los que quepan y «+N»)
function posterGrid(n, W, H, gap) {
  for (let cols = 2; cols <= 10; cols++) {
    const w = (W - gap * (cols - 1)) / cols, ph = w * 1.5;
    const rows = Math.floor((H + gap) / (ph + gap));
    if (rows >= 1 && rows * cols >= n) return { cols, rows: Math.ceil(n / cols), w, h: ph, cap: n };
  }
  const cols = 10, w = (W - gap * 9) / cols, ph = w * 1.5, rows = Math.max(1, Math.floor((H + gap) / (ph + gap)));
  return { cols, rows, w, h: ph, cap: cols * rows };
}
const CARD_FAM = { nintendo: '#ff5a66', playstation: '#6d8bff', xbox: '#34d399', pc: '#38bdf8', mobile: '#fbbf24', other: '#8b93a7' };
async function drawYearCard(d) {
  try { await Promise.all(['700 64px Outfit', '600 32px Outfit', '400 24px Epilogue', '600 24px Epilogue', '500 24px "JetBrains Mono"'].map(f => document.fonts.load(f))); } catch { /* sin fuentes: las del sistema */ }
  const W = 1080, H = 1350, P = 72;
  const C = { bg: '#0c0d10', surface: '#13151b', card: '#1a1d26', ink: '#eceef3', muted: '#9ba2b3', line: 'rgba(255,255,255,.09)', accent: '#ff6b4f', star: '#fbbf5a' };
  const mono = (s, px, w = 500) => `${w} ${px}px "JetBrains Mono", Consolas, monospace`;
  const disp = (px, w = 700) => `${w} ${px}px Outfit, "Segoe UI", sans-serif`;
  const text = (px, w = 400) => `${w} ${px}px Epilogue, "Segoe UI", sans-serif`;
  const c = document.createElement('canvas'); c.width = W; c.height = H;
  const g = c.getContext('2d');
  g.textBaseline = 'alphabetic';

  // Fondo y marco
  g.fillStyle = C.bg; g.fillRect(0, 0, W, H);
  const lg = g.createLinearGradient(0, 0, 0, H); lg.addColorStop(0, 'rgba(255,255,255,.035)'); lg.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = lg; g.fillRect(0, 0, W, H);
  g.strokeStyle = C.line; g.lineWidth = 2; rrect(g, 24, 24, W - 48, H - 48, 28); g.stroke();

  // Cabecera
  g.fillStyle = C.muted; g.font = mono('', 22); g.fillText(`CHECKPOINT · ${tr('Resumen del año').toUpperCase()}`, P, 104);
  g.fillStyle = C.ink; g.font = disp(184); g.fillText(String(d.year), P - 6, 290);
  g.fillStyle = C.accent; rrect(g, P, 318, 72, 6, 3); g.fill();
  g.fillStyle = C.muted; g.font = text(30); g.fillText(tr('Los juegos que me he pasado'), P, 382);

  // Cifras
  const stats = [[String(d.games), tr('Juegos')], [d.hours ? fmtNum(d.hours) : '—', tr('Horas')], [d.rating != null ? fmtNum(d.rating) : '—', tr('Nota media')]];
  const colW = (W - 2 * P) / 3;
  stats.forEach(([n, l], i) => {
    const x = P + i * colW;
    if (i) { g.fillStyle = C.line; g.fillRect(x - 24, 432, 2, 92); }
    g.fillStyle = C.ink; g.font = disp(72, 600); g.fillText(n, x, 496);
    g.fillStyle = C.muted; g.font = mono('', 18); g.fillText(l.toUpperCase(), x, 526);
  });

  // Carátulas
  const top3 = (d.top || []).slice(0, 3);
  const gy = 562, gh = (top3.length ? 1120 : 1240) - gy, gap = 14;
  if (!d.games) {
    g.fillStyle = C.card; rrect(g, P, gy, W - 2 * P, 220, 20); g.fill();
    g.fillStyle = C.muted; g.font = text(30);
    fitLines(g, tr(`En ${d.year} no hay juegos completados con fecha.`), W - 2 * P - 80, 2).forEach((l, i) => g.fillText(l, P + 40, gy + 100 + i * 40));
  } else {
    const L = posterGrid(d.list.length, W - 2 * P, gh, gap);
    const shown = d.list.length > L.cap ? d.list.slice(0, L.cap - 1) : d.list;
    const extra = d.list.length - shown.length;
    const imgs = await Promise.all(shown.map(x => loadImg(localCover(x.entryId))));
    const usedW = Math.min(L.cols, shown.length + (extra ? 1 : 0)) * (L.w + gap) - gap;
    const x0 = P + (W - 2 * P - usedW) / 2;
    const cell = i => [x0 + (i % L.cols) * (L.w + gap), gy + Math.floor(i / L.cols) * (L.h + gap)];
    shown.forEach((game, i) => { const [x, y] = cell(i); drawPoster(g, x, y, L.w, L.h, game, imgs[i], C, { mono, disp, text }); });
    if (extra) {
      const [x, y] = cell(shown.length);
      g.fillStyle = C.card; rrect(g, x, y, L.w, L.h, 12); g.fill();
      g.fillStyle = C.ink; g.font = disp(Math.max(22, L.w * 0.26), 600); g.textAlign = 'center';
      g.fillText(`+${extra}`, x + L.w / 2, y + L.h / 2 + L.w * 0.09); g.textAlign = 'left';
    }
  }

  // Mejores puntuaciones
  if (top3.length) {
    g.fillStyle = C.muted; g.font = mono('', 18); g.fillText(tr('Mejores puntuaciones').toUpperCase(), P, 1144);
    top3.forEach((t, i) => {
      const y = 1182 + i * 36;
      g.fillStyle = C.star; g.font = mono('', 24, 600); g.fillText(`★ ${fmtNum(t.rating)}`, P, y);
      g.fillStyle = C.ink; g.font = text(26, 600);
      g.fillText(fitLines(g, t.title, W - 2 * P - 260, 1)[0] || '', P + 120, y);
      if (t.platforms) { g.fillStyle = C.muted; g.font = mono('', 18); g.textAlign = 'right'; g.fillText(fitLines(g, t.platforms, 150, 1)[0], W - P, y); g.textAlign = 'left'; }
    });
  }

  // Pie
  g.fillStyle = C.muted; g.font = mono('', 16);
  g.fillText(tr('Hecho con Checkpoint'), P, H - 46);
  g.textAlign = 'right'; g.fillText(today(), W - P, H - 46); g.textAlign = 'left';
  return c;
}
function drawPoster(g, x, y, w, ph, game, img, C, F2) {
  const r = Math.max(8, w * 0.07);
  g.save();
  rrect(g, x, y, w, ph, r); g.clip();
  if (img) {
    const s = Math.max(w / img.naturalWidth, ph / img.naturalHeight);
    const iw = img.naturalWidth * s, ih = img.naturalHeight * s;
    g.drawImage(img, x + (w - iw) / 2, y + (ph - ih) / 2, iw, ih);
  } else {
    // Carátula tipográfica: color de la familia de la plataforma, iniciales y título
    const fam = CARD_FAM[platformFamily(splitPlatforms(game.platforms)[0] || '')] || CARD_FAM.other;
    const bg = g.createLinearGradient(x, y, x + w, y + ph);
    bg.addColorStop(0, fam + '55'); bg.addColorStop(1, fam + '14');
    g.fillStyle = C.card; g.fillRect(x, y, w, ph);
    g.fillStyle = bg; g.fillRect(x, y, w, ph);
    g.fillStyle = 'rgba(236,238,243,.92)'; g.font = F2.disp(Math.round(w * 0.3)); g.textAlign = 'center';
    g.fillText(initials(game.title), x + w / 2, y + ph * 0.42);
    g.textAlign = 'left';
    const fs = Math.max(12, Math.round(w * 0.085));
    g.font = F2.text(fs, 600); g.fillStyle = C.ink;
    const lines = fitLines(g, game.title, w - fs * 1.4, 3);
    lines.forEach((l, i) => g.fillText(l, x + fs * 0.7, y + ph - fs * 0.9 - (lines.length - 1 - i) * fs * 1.25));
  }
  // Nota en la esquina
  if (game.rating != null && w >= 70) {
    const fs = Math.max(12, Math.round(w * 0.075));
    g.font = F2.mono('', fs, 600);
    const label = `★ ${fmtNum(game.rating)}`, tw = g.measureText(label).width;
    g.fillStyle = 'rgba(12,13,16,.82)'; rrect(g, x + w - tw - fs * 1.4, y + fs * 0.6, tw + fs * 0.9, fs * 1.6, fs * 0.8); g.fill();
    g.fillStyle = C.star; g.fillText(label, x + w - tw - fs * 0.95, y + fs * 1.75);
  }
  g.restore();
  g.strokeStyle = 'rgba(255,255,255,.10)'; g.lineWidth = 1.5; rrect(g, x + 0.75, y + 0.75, w - 1.5, ph - 1.5, r); g.stroke();
}

// ── Duración desde IGDB ── (conector en ListaJuegos.Core.Connectors.IgdbClient; HowLongToBeat solo como enlace)
const hltbUrl = t => `https://howlongtobeat.com/?q=${encodeURIComponent((t || '').replace(/\([^)]*\)|\[[^\]]*\]/g, ' ').replace(/\s+/g, ' ').trim())}`;
function needIgdb() {
  if (S.settings.hasIgdbKey) return false;
  toast('Para buscar duraciones añade el Client ID y el secreto de IGDB en Ajustes › Cuentas.', { label: 'Abrir Ajustes', fn: openSettings });
  return true;
}
function ttbText(r) {
  return [r.year ? `${r.name} (${r.year})` : r.name, `${fmtNum(r.main)} h historia`, r.full && r.full > r.main ? `${fmtNum(r.full)} h al 100 %` : null,
    plural(r.count, 'tiempo enviado', 'tiempos enviados')].filter(Boolean).map(x => tr(x)).join(' · ');
}
async function findDuration(e) {
  if (needIgdb() || !e.title?.trim()) return;
  const box = $('#dTtbResults'), btn = $('#dTtbFind');
  btn.disabled = true;
  box.replaceChildren(h('p', { class: 'hint' }, icon('loader-circle', 'i i--sm spin'), ' ', 'Buscando en IGDB…'));
  let r;
  try { r = await Host.call('ttb.lookup', { title: e.title, platforms: e.platforms || null }); }
  catch (err) { box.replaceChildren(h('p', { class: 'msg msg--warn' }, icon('triangle-alert', 'i i--sm'), h('span', { text: err.message }))); btn.disabled = false; return; }
  btn.disabled = false;
  const list = r?.results || [];
  if (!list.length) { box.replaceChildren(h('p', { class: 'hint', text: 'IGDB no tiene tiempos para este juego. Prueba en HowLongToBeat o escribe la duración a mano.' })); return; }
  const apply = x => {
    mutate('duración de IGDB', () => F.set(e, 'ttb', x.field));
    announce(`Duración de «${e.title}»: ${fmtNum(x.main)} h`);
    $('#dTtb')?.focus();
  };
  box.replaceChildren(
    h('p', { class: 'hint', text: list[0].exact ? 'Resultado de IGDB:' : 'IGDB no tiene ese título exacto. ¿Es alguno de estos?' }),
    h('ul', { class: 'ttb-list' }, ...list.slice(0, 3).map(x => h('li', null,
      h('span', { class: 'ttb-list__txt' }, ttbText(x)),
      h('button', { type: 'button', class: 'btn btn--secondary btn--sm', onclick: () => apply(x), 'aria-label': `Usar la duración de ${x.name}${x.year ? ' (' + x.year + ')' : ''}` }, icon('check', 'i i--sm'), 'Usar')))));
  box.querySelector('button')?.focus();
}
async function fillDurations() {
  if (needIgdb()) return;
  const s = curSection(); if (!s) return;
  const btn = $('#btnFillTtb');
  if (btn) { btn.disabled = true; btn.lastChild.textContent = tr('Buscando…'); }
  try {
    const r = await Host.call('ttb.fill', { doc: S.doc, section: S.sec });
    if (!r) return;
    const changes = (r.changes || []).filter(c => findEntry(c.entryId));
    if (changes.length) mutate('duraciones de IGDB', () => { for (const c of changes) F.set(findEntry(c.entryId).e, 'ttb', c.field); });
    const rest = [r.unsure?.length ? tr(`${r.unsure.length} dudosos`) : null, r.notFound?.length ? tr(`${r.notFound.length} sin datos`) : null].filter(Boolean).join(' · ');
    const msg = changes.length ? tr(`Duración añadida a ${plural(changes.length, 'juego', 'juegos')}`) : tr('No se ha añadido ninguna duración');
    toast(rest ? `${msg}. ${rest}: búscalos uno a uno desde el inspector.` : `${msg}.`, changes.length ? { label: 'Deshacer', fn: undo } : null);
  } catch (err) { toast(`No se pudo buscar en IGDB: ${err.message}`); }
  finally { $('#statusLeft').textContent = ''; render(); }
}

// ───────────────────────────── Bienvenida (primera vez) ─────────────────────────────
// Asistente de 4 pasos: idioma, apariencia y guardado, cuentas (opcional) y la lista. Todo se puede cambiar después en
// Ajustes. Al cerrarlo, de cualquier forma, no vuelve a salir (se puede abrir otra vez desde Ajustes).
const WELCOME_STEPS = ['Bienvenida', 'Apariencia y guardado', 'Cuentas y servicios', 'Tu lista'];
let welcomeStep = 0;
function openWelcome(step = 0) {
  $('#dlgSettings').replaceChildren();   // los campos de cuentas usan los mismos id que Ajustes
  const dlg = $('#dlgWelcome');
  const st = S.settings;
  welcomeStep = Math.max(0, Math.min(WELCOME_STEPS.length - 1, step));
  const setS = async patch => { Object.assign(st, patch); S.settings = await Host.call('settings.set', patch) || st; applyTheme(); };
  const choice = (name, value, current, label, desc, onpick) => h('label', { class: 'option choice' },
    h('input', { type: 'radio', class: 'm-radio', name, value, checked: current === value, onchange: () => onpick(value) }),
    h('span', null, label, desc ? h('small', null, desc) : null));
  const check = (id, label, desc, on, fn) => h('label', { class: 'option choice', for: id },
    h('input', { type: 'checkbox', class: 'm-check', id, checked: !!on, onchange: ev => fn(ev.target.checked) }),
    h('span', null, label, desc ? h('small', null, desc) : null));

  const bodies = [
    () => [
      h('p', { class: 'welcome__hero' }, h('span', { class: 'appname__mark' }, icon('gamepad-2', 'i i--lg'))),
      h('p', { class: 'lead', id: 'welcomeLead' }, 'Checkpoint guarda tu lista de juegos en un .txt o un .md que es tuyo y se puede leer en cualquier sitio. En unos pasos eliges cómo se ve y, si quieres, conectas tus cuentas. Todo se puede cambiar luego en Ajustes.'),
      h('div', { class: 'field' }, h('label', { for: 'wLang' }, 'Idioma'),
        h('select', { id: 'wLang', class: 'select', onchange: async ev => { await setS({ language: ev.target.value }); setLanguage(ev.target.value); openWelcome(0); $('#wLang')?.focus(); } },
          h('option', { value: 'system', selected: (st.language || 'system') === 'system' }, `${tr('Sistema')} (${I18N.systemName})`),
          ...I18N.LANGS.map(l => h('option', { value: l.code, selected: st.language === l.code, lang: l.code, 'data-notr': '' }, document.createTextNode(l.name))))),
      h('p', { class: 'hint welcome__privacy' }, icon('info', 'i i--sm'), ' ', 'Sin telemetría: la app solo se conecta a Internet cuando importas, buscas portadas o duraciones.')
    ],
    () => [
      h('fieldset', { class: 'next-filter' }, h('legend', null, 'Tema'),
        h('div', { class: 'options options--choice' },
          choice('wTheme', 'system', st.theme || 'system', 'Sistema', 'Sigue el modo de Windows', v => setS({ theme: v })),
          choice('wTheme', 'dark', st.theme, 'Oscuro', null, v => setS({ theme: v })),
          choice('wTheme', 'light', st.theme, 'Claro', null, v => setS({ theme: v })))),
      h('fieldset', { class: 'next-filter' }, h('legend', null, 'Vista de la lista'),
        h('div', { class: 'options options--choice' },
          choice('wView', 'grid', st.view === 'list' ? 'list' : 'grid', 'Portadas', 'Tarjetas con carátula', v => setS({ view: v })),
          choice('wView', 'list', st.view === 'list' ? 'list' : 'grid', 'Lista compacta', 'Una fila por juego', v => setS({ view: v })))),
      h('div', { class: 'options' },
        check('wCovers', 'Mostrar portadas', 'Busca la carátula de cada juego en Steam (o SteamGridDB con clave) y la guarda en este PC.', st.covers, v => { setS({ covers: v, coversAsked: true }); if (v) queueCovers(true); }),
        check('wDate', 'Poner al día «(Actualizado el …)» al guardar', null, st.updateDate, v => setS({ updateDate: v })),
        check('wBak', 'Copia de seguridad .bak', 'La primera vez que guardas en cada sesión se copia el archivo anterior.', st.backup, v => setS({ backup: v })))
    ],
    () => [
      h('p', { class: 'lead', style: 'font-size:13.5px' }, 'Opcional. Las claves se guardan cifradas con tu usuario de Windows y solo se usan cuando importas, buscas portadas o duraciones. Puedes saltarte este paso.'),
      ...accountFields(st, () => openWelcome(2))
    ],
    () => [
      h('p', { class: 'lead' }, '¿Con qué lista empiezas?'),
      h('div', { class: 'start__actions' },
        h('button', { class: 'bigcard', onclick: () => { finishWelcome(); openFile(); } }, h('span', { class: 'appname__mark' }, icon('folder-open', 'i i--lg')), h('b', null, 'Abrir una lista'), h('span', null, 'Un .txt o .md que ya tengas.')),
        h('button', { class: 'bigcard', onclick: () => { finishWelcome(); newFile(); } }, h('span', { class: 'appname__mark' }, icon('file-plus', 'i i--lg')), h('b', null, 'Crear una lista nueva'), h('span', null, 'Con Completados, Inconclusos y Por jugar.'))),
      h('fieldset', { class: 'next-filter' }, h('legend', null, 'Al volver a abrir la app'),
        h('div', { class: 'options options--choice' },
          choice('wResume', 'ask', st.resume || 'ask', 'Preguntar si continúo con la última lista', null, v => setS({ resume: v })),
          choice('wResume', 'always', st.resume, 'Abrir la última lista directamente', null, v => setS({ resume: v })),
          choice('wResume', 'never', st.resume, 'Empezar en la pantalla de inicio', null, v => setS({ resume: v }))))
    ]
  ];

  const last = welcomeStep === WELCOME_STEPS.length - 1;
  const go = n => { openWelcome(n); dlg.querySelector('.dlg__body')?.querySelector('input, select, button')?.focus(); };
  dlg.replaceChildren(
    h('div', { class: 'dlg__head' },
      h('div', null,
        h('p', { class: 'welcome__step', id: 'welcomeStep' }, tr(`Paso ${welcomeStep + 1} de ${WELCOME_STEPS.length}`)),
        h('h2', { id: 'welcomeTitle' }, welcomeStep === 0 ? 'Te damos la bienvenida a Checkpoint' : WELCOME_STEPS[welcomeStep])),
      h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar la bienvenida', onclick: () => dlg.close() }, icon('x'))),
    h('ol', { class: 'welcome__dots', 'aria-label': 'Pasos' }, ...WELCOME_STEPS.map((s, i) =>
      h('li', { class: i === welcomeStep ? 'is-current' : i < welcomeStep ? 'is-done' : '', 'aria-current': i === welcomeStep ? 'step' : null }, h('span', { class: 'sr-only', text: s })))),
    h('div', { class: 'dlg__body' }, ...bodies[welcomeStep]()),
    h('div', { class: 'dlg__foot' },
      welcomeStep > 0 ? h('button', { class: 'btn btn--ghost', onclick: () => go(welcomeStep - 1) }, icon('arrow-left'), 'Atrás') : h('button', { class: 'btn btn--ghost', onclick: () => dlg.close() }, 'Saltar la configuración'),
      h('span', { class: 'grow' }),
      last ? h('button', { class: 'btn btn--secondary', onclick: () => dlg.close() }, 'Más tarde')
           : h('button', { class: 'btn btn--secondary', id: 'welcomeNext', onclick: () => go(welcomeStep + 1) }, 'Siguiente', icon('arrow-right'))));
  if (!dlg.open) dlg.showModal();
  if (welcomeStep === 0) $('#welcomeNext')?.focus();
}
function finishWelcome() {
  const dlg = $('#dlgWelcome');
  if (dlg.open) dlg.close();
}
// Al cerrar (Esc, «Saltar», «Más tarde», o al abrir o crear una lista) queda marcada como vista.
function onWelcomeClosed() {
  if (!S.settings.onboarded) { S.settings.onboarded = true; Host.call('settings.set', { onboarded: true }); }
  render();
  if (!S.doc) $('#main').focus();
}

// ───────────────────────────── Continuar con la última lista ─────────────────────────────
function askResume(path) {
  const dlg = $('#dlgResume');
  const name = path.split(/[\\/]/).pop();
  const cb = h('input', { type: 'checkbox', id: 'resumeNever', class: 'm-check' });
  dlg.replaceChildren(h('form', { method: 'dialog' },
    h('div', { class: 'dlg__head' }, h('h2', { id: 'resumeTitle' }, '¿Continuar donde lo dejaste?')),
    h('div', { class: 'dlg__body' },
      h('p', { class: 'lead', id: 'resumeText' }, tr(`La última lista que abriste es «${name}».`)),
      h('p', { class: 'resume__path', title: path }, h('img', { class: 'brand', src: /\.md$/i.test(path) ? 'icons/markdown.svg' : 'icons/text.svg', alt: '' }), h('span', { text: path })),
      h('label', { class: 'inline', for: 'resumeNever', style: 'cursor:pointer' }, cb, 'No volver a preguntar')),
    h('div', { class: 'dlg__foot' },
      h('button', { class: 'btn btn--secondary', value: 'no' }, 'Ahora no'),
      h('button', { class: 'btn btn--primary', value: 'yes', id: 'resumeYes' }, icon('folder-open'), 'Continuar'))));
  dlg.returnValue = '';
  dlg.onclose = async () => {
    const yes = dlg.returnValue === 'yes';
    if (cb.checked) {
      // Con la casilla, la respuesta queda para las próximas veces: abrir siempre la última o empezar siempre en el inicio
      const resume = yes ? 'always' : 'never';
      S.settings.resume = resume;
      S.settings = await Host.call('settings.set', { resume }) || S.settings;
      toast(yes ? 'Se abrirá siempre la última lista. Puedes cambiarlo en Ajustes.' : 'No se volverá a preguntar. Puedes cambiarlo en Ajustes.');
    }
    if (yes) openFile(path); else $('#main').focus();
  };
  dlg.showModal();
  $('#resumeYes').focus();
}

// ── ¿Qué juego ahora? ── (la lógica está en ListaJuegos.Core.Suggest.NextGame)
const NEXT = { length: 'any', sagaOnly: false, platform: '' };
const LENGTHS = [
  { id: 'any', label: 'Me da igual', desc: 'Cualquier duración' },
  { id: 'short', label: 'Corto', desc: 'Menos de 8 h' },
  { id: 'medium', label: 'Medio', desc: 'Entre 8 y 20 h' },
  { id: 'long', label: 'Largo', desc: 'Más de 20 h' }
];
const NEXT_MAX = 30;
function openNext() {
  if (!S.doc) return;
  const dlg = $('#dlgNext');
  const backlog = sections().filter(s => sectionKind(s) === 'backlog');
  const plats = [...new Set(backlog.flatMap(s => s.blocks.flatMap(b => (b.entries || []).flatMap(e => splitPlatforms(e.platforms)))))]
    .sort((a, b) => a.localeCompare(b, I18N.locale));
  if (NEXT.platform && !plats.includes(NEXT.platform)) NEXT.platform = '';
  const list = h('ul', { class: 'next-list', id: 'nextList', 'aria-labelledby': 'nextResTitle' });
  const count = h('span', { class: 'muted', id: 'nextCount', role: 'status', 'aria-live': 'polite' });
  let seq = 0;
  const refresh = async () => {
    const my = ++seq;
    let r = null;
    try { r = await Host.call('next.suggest', { doc: S.doc, length: NEXT.length, sagaOnly: NEXT.sagaOnly, platform: NEXT.platform || null }); }
    catch (err) { list.replaceChildren(h('li', { class: 'msg msg--warn' }, icon('triangle-alert'), h('span', { text: err.message }))); return; }
    if (my !== seq) return;                               // llegó una respuesta más nueva
    const picks = r?.picks || [];
    const playing = sections().findIndex(s => sectionKind(s) === 'playing');
    list.replaceChildren(...picks.slice(0, NEXT_MAX).map(p => nextItem(p, playing, dlg)));
    if (!picks.length) list.append(h('li', { class: 'next next--empty' }, icon('search', 'i'), h('span', { text: 'Ningún juego pendiente cumple esos filtros. Prueba con otra duración o plataforma.' })));
    count.textContent = tr(picks.length > NEXT_MAX ? `${plural(picks.length, 'sugerencia', 'sugerencias')} · se muestran ${NEXT_MAX}` : plural(picks.length, 'sugerencia', 'sugerencias'));
  };
  const body = backlog.length ? [
    h('p', { class: 'lead', id: 'nextLead' }, 'Te propone juegos de «Por jugar» según el tiempo que tengas, las sagas que ya has empezado y la plataforma.'),
    h('fieldset', { class: 'next-filter' }, h('legend', null, 'Tiempo disponible'),
      h('div', { class: 'options options--choice' }, ...LENGTHS.map(l => h('label', { class: 'option choice' },
        h('input', { type: 'radio', class: 'm-radio', name: 'nextLen', value: l.id, checked: NEXT.length === l.id, onchange: () => { NEXT.length = l.id; refresh(); } }),
        h('span', null, l.label, h('small', null, l.desc)))))),
    h('div', { class: 'row2 next-row' },
      h('div', { class: 'field' },
        h('label', { for: 'nextPlat' }, 'Plataforma'),
        h('select', { id: 'nextPlat', class: 'select', onchange: ev => { NEXT.platform = ev.target.value; refresh(); } },
          h('option', { value: '' }, 'Todas las plataformas'), ...plats.map(p => h('option', { value: p, selected: p === NEXT.platform }, p)))),
      h('label', { class: 'option choice next-saga' },
        h('input', { type: 'checkbox', class: 'm-check', checked: NEXT.sagaOnly, onchange: ev => { NEXT.sagaOnly = ev.target.checked; refresh(); } }),
        h('span', null, 'Solo sagas que ya he empezado', h('small', null, 'Con entregas en Completados o Inconclusos')))),
    h('div', { class: 'imp-tools' }, h('h3', { class: 'sub', id: 'nextResTitle' }, 'Sugerencias'), h('span', { class: 'grow' }), count),
    list
  ] : [h('p', { class: 'lead', id: 'nextLead' }, 'Esta lista no tiene una sección «Por jugar». Crea una (o renombra una existente) para recibir sugerencias.')];
  dlg.replaceChildren(
    h('div', { class: 'dlg__head' }, h('h2', { id: 'nextTitle', style: 'display:flex;align-items:center;gap:10px' }, icon('sparkles'), '¿Qué juego ahora?'), h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar', onclick: () => dlg.close() }, icon('x'))),
    h('div', { class: 'dlg__body' }, ...body),
    h('div', { class: 'dlg__foot' }, h('button', { class: 'btn btn--secondary', onclick: () => dlg.close() }, 'Cerrar')));
  if (!dlg.open) dlg.showModal();
  dlg.querySelector('input[name="nextLen"]:checked')?.focus();
  if (backlog.length) refresh();
}
function nextItem(p, playing, dlg) {
  const dur = p.hours != null
    ? (p.fullHours ? `⌛ ${fmtNum(p.hours)} h · ${fmtNum(p.fullHours)} h al 100 %` : `⌛ ${fmtNum(p.hours)} h`)
    : 'Sin duración anotada';
  const saga = p.saga
    ? `Continúa ${p.saga}: ${[p.sagaDone ? tr(plural(p.sagaDone, 'completado', 'completados')) : null, p.sagaStarted ? tr(plural(p.sagaStarted, 'empezado', 'empezados')) : null].filter(Boolean).join(', ')}`
    : null;
  const goTo = () => {
    const x = findEntry(p.entryId); if (!x) return;
    dlg.close();
    S.query = ''; $('#q').value = ''; S.platform = ''; S.textMode = false; S.statsMode = false;
    S.sec = sections().indexOf(x.s);
    render();
    select(p.entryId);
  };
  return h('li', { class: 'next' },
    h('div', { style: 'min-width:0' },
      h('div', { class: 'imp__title', text: p.title || 'Sin título' }),
      h('div', { class: 'imp__meta' },
        p.platforms ? h('span', { text: p.platforms }) : null,
        h('span', { text: dur }),
        saga ? h('span', { class: 'next__saga', title: (p.sagaTitles || []).join(' · ') }, icon('trophy', 'i i--sm'), tr(saga)) : null)),
    h('div', { class: 'next__actions' },
      h('button', { class: 'btn btn--ghost btn--sm', 'aria-label': `Ver ${p.title} en la lista`, onclick: goTo }, icon('eye'), 'Ver'),
      playing >= 0 ? h('button', { class: 'btn btn--secondary btn--sm', 'aria-label': `Empezar ${p.title} ahora`, onclick: () => { dlg.close(); moveToSection(p.entryId, playing); } }, icon('gamepad-2'), 'Empezar ahora') : null));
}

// ── Importar ──
const SOURCES = [
  { id: 'steamOnline', label: 'Steam (tu cuenta)', desc: 'Toda tu biblioteca, horas y logros', icon: 'icons/steam.svg', needs: 'hasSteamKey' },
  { id: 'steamLocal', label: 'Steam (este PC)', desc: 'Juegos instalados y horas', icon: 'icons/steam.svg' },
  { id: 'xboxLocal', label: 'Xbox (este PC)', desc: 'Juegos de la app Xbox / Game Pass', text: 'XB' },
  { id: 'xboxOnline', label: 'Xbox (tu cuenta)', desc: 'Historial y logros vía OpenXBL', text: 'XB', needs: 'hasXblKey' },
  { id: 'epicLocal', label: 'Epic Games (este PC)', desc: 'Juegos instalados con el lanzador', icon: 'icons/epic-games.svg' },
  { id: 'gogLocal', label: 'GOG Galaxy (este PC)', desc: 'Juegos instalados', icon: 'icons/gog.svg' },
  { id: 'eaLocal', label: 'EA app (este PC)', desc: 'Juegos instalados', icon: 'icons/ea.svg' },
  { id: 'ubisoftLocal', label: 'Ubisoft Connect (este PC)', desc: 'Juegos instalados', icon: 'icons/ubisoft.svg' },
  { id: 'raOnline', label: 'RetroAchievements (tu cuenta)', desc: 'Juegos retro, logros y modo Hardcore', text: 'RA', needs: 'hasRaKey' },
  { id: 'emuLocal', label: 'Emuladores (este PC)', desc: 'RetroArch, Dolphin, PCSX2 y DuckStation', lucide: 'gamepad-2' }
];
let IMP = { chosen: new Set(['steamLocal', 'xboxLocal', 'epicLocal']), achievements: false, candidates: [], messages: [], hideDup: true };
function openImport() {
  if (!S.doc) return;
  if (S.settings.hasSteamKey) IMP.chosen.add('steamOnline');
  if (S.settings.hasXblKey) IMP.chosen.add('xboxOnline');
  if (S.settings.hasRaKey && S.settings.raUser) IMP.chosen.add('raOnline');
  importStep1();
  const dlg = $('#dlgImport');
  if (!dlg.open) dlg.showModal();
}
function importStep1() {
  const dlg = $('#dlgImport');
  dlg.replaceChildren(
    h('div', { class: 'dlg__head' }, h('h2', { id: 'importTitle' }, 'Importar juegos'), h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar', onclick: () => dlg.close() }, icon('x'))),
    h('div', { class: 'dlg__body' },
      h('p', { class: 'lead' }, 'Elige de dónde traerlos. Antes de añadir nada verás la lista, con la sección y la saga que se proponen para cada juego.'),
      h('fieldset', null, h('legend', { class: 'sr-only' }, 'Fuentes'),
        h('div', { class: 'options' }, ...SOURCES.map(src => {
          const blocked = src.needs && (!S.settings[src.needs] || (src.id === 'raOnline' && !S.settings.raUser));
          return h('label', { class: 'option', style: blocked ? 'opacity:.6' : null },
            h('input', { type: 'checkbox', class: 'm-check', checked: IMP.chosen.has(src.id) && !blocked, disabled: blocked,
              onchange: ev => ev.target.checked ? IMP.chosen.add(src.id) : IMP.chosen.delete(src.id) }),
            src.icon ? h('img', { class: 'brand ic-ink', src: src.icon, alt: '' }) : src.lucide ? icon(src.lucide, 'i brand') : h('span', { class: 'brand-text', 'aria-hidden': 'true' }, src.text),
            h('span', null, src.label, h('small', null, blocked ? (src.id === 'raOnline' ? 'Configura el usuario y la clave en Ajustes' : 'Configura la clave en Ajustes') : src.desc)));
        }))),
      h('label', { class: 'inline', style: 'cursor:pointer' }, h('input', { type: 'checkbox', class: 'm-check', checked: IMP.achievements, onchange: ev => (IMP.achievements = ev.target.checked) }),
        'Pedir también los logros de Steam (más lento)'),
      (!S.settings.hasSteamKey || !S.settings.hasXblKey || !S.settings.hasRaKey) ? h('div', { class: 'msg' }, icon('info'), h('span', null, 'Para traer tu biblioteca completa de Steam, tu historial de Xbox o tus juegos de RetroAchievements añade tus claves en ',
        h('a', { href: '#', class: 'link', onclick: ev => { ev.preventDefault(); dlg.close(); openSettings(); } }, 'Ajustes › Cuentas'), '. Nintendo y PlayStation no ofrecen una forma de leer tu biblioteca: añádelos a mano.')) : null),
    h('div', { class: 'dlg__foot' },
      h('button', { class: 'btn btn--secondary', onclick: refreshLinked }, icon('refresh-cw'), 'Actualizar horas y logros de los enlazados'),
      h('span', { class: 'grow' }),
      h('button', { class: 'btn btn--secondary', onclick: () => dlg.close() }, 'Cancelar'),
      h('button', { class: 'btn btn--primary', onclick: runScan }, icon('search'), 'Buscar juegos')));
}
Host.on('progress', d => { const p = $('#impProgress'); if (p) p.textContent = tr(d.text); announce(d.text); });
async function runScan() {
  const dlg = $('#dlgImport');
  const body = dlg.querySelector('.dlg__body');
  body.replaceChildren(h('div', { class: 'progress', role: 'status' }, icon('loader-circle', 'i spin'), h('span', { id: 'impProgress' }, 'Buscando juegos…')));
  try {
    const r = await Host.call('import.scan', { doc: S.doc, sources: [...IMP.chosen], achievements: IMP.achievements });
    IMP.candidates = r?.candidates || [];
    IMP.messages = r?.messages || [];
    importStep2();
  } catch (err) {
    body.replaceChildren(h('div', { class: 'msg msg--warn' }, icon('triangle-alert'), h('span', { text: err.message })));
  }
}
function importStep2() {
  const dlg = $('#dlgImport');
  const secNames = [...new Set([...sections().map(s => s.name), ...IMP.candidates.map(c => c.suggestedSection).filter(Boolean)])];
  const catNames = [...new Set([...allEntries()].map(x => x.b).filter(b => b.style !== 'none').map(blockLabel).concat(S.sagas))];
  const list = h('ul', { class: 'imp-list', id: 'impList' });
  const countEl = h('span', { class: 'muted', id: 'impCount' });
  const addBtn = h('button', { class: 'btn btn--primary', onclick: applyImport }, icon('plus'), h('span', { id: 'impAddLabel' }, 'Añadir'));
  const refresh = () => {
    const visible = IMP.candidates.filter(c => !(IMP.hideDup && c.duplicateOf));
    list.replaceChildren(...visible.map(c => h('li', { class: `imp ${c.duplicateOf ? 'is-dup' : ''}` },
      h('input', { type: 'checkbox', class: 'm-check', checked: c.selected, 'aria-label': `Añadir ${c.title}`, onchange: ev => { c.selected = ev.target.checked; upd(); } }),
      h('span', { class: 'thumb', 'aria-hidden': 'true' }, c.thumb && (S.settings.covers || c.thumb.startsWith('https://covers.lj')) ? h('img', { src: c.thumb, alt: '', loading: 'lazy' }) : null),
      h('div', { style: 'min-width:0' },
        h('div', { class: 'imp__title', text: c.title }),
        h('div', { class: 'imp__meta' },
          ...[c.platform, c.note ? `[${c.note}]` : null, c.hours ? `${fmtNum(c.hours)} h` : null, c.achievements != null ? `Logros ${fmtNum(c.achievements)}%` : null,
            c.hardcore ? `Hardcore ${fmtNum(c.hardcore)}%` : null, c.beaten ? 'superado' : null,
            c.installed ? 'instalado' : null, c.lastPlayed ? `jugado el ${new Date(c.lastPlayed).toLocaleDateString(I18N.locale)}` : null]
            .filter(Boolean).map(t => h('span', { text: t })),
          c.duplicateOf ? h('span', { class: 'badge-dup' }, `ya está en ${c.duplicateOf}`) : null)),
      h('select', { 'aria-label': `Sección para ${c.title}`, onchange: ev => (c.suggestedSection = ev.target.value) }, ...secNames.map(n => h('option', { value: n, selected: n === c.suggestedSection }, n))),
      h('input', { type: 'text', list: 'impCats', 'aria-label': `Saga o género para ${c.title}`, placeholder: 'Sin categoría', value: c.suggestedCategory ? (c.suggestedParen ? `(${c.suggestedCategory})` : c.suggestedCategory) : '',
        onchange: ev => { const v = ev.target.value.trim(); c.suggestedParen = /^\(.*\)$/.test(v); c.suggestedCategory = v.replace(/^\((.*)\)$/, '$1') || null; } }))));
    if (!visible.length) list.append(h('li', { class: 'muted' }, IMP.candidates.length ? 'Todos los juegos encontrados ya están en tu lista.' : 'No se ha encontrado ningún juego.'));
    upd();
  };
  const upd = () => {
    const n = IMP.candidates.filter(c => c.selected).length;
    countEl.textContent = tr(`${plural(IMP.candidates.length, 'encontrado', 'encontrados')} · ${n} para añadir`);
    $('#impAddLabel') && ($('#impAddLabel').textContent = tr(n ? `Añadir ${plural(n, 'juego', 'juegos')}` : 'Añadir'));
    addBtn.disabled = !n;
  };
  dlg.replaceChildren(
    h('div', { class: 'dlg__head' }, h('h2', { id: 'importTitle' }, 'Revisa antes de añadir'), h('button', { class: 'btn btn--ghost btn--icon', 'aria-label': 'Cerrar', onclick: () => dlg.close() }, icon('x'))),
    h('div', { class: 'dlg__body' },
      ...IMP.messages.filter(m => m.message).map(m => h('div', { class: `msg ${m.ok ? '' : 'msg--warn'}` }, icon(m.ok ? 'info' : 'triangle-alert'), h('span', { text: m.message }))),
      h('div', { class: 'imp-tools' },
        h('button', { class: 'btn btn--ghost btn--sm', onclick: () => { IMP.candidates.forEach(c => (c.selected = !(IMP.hideDup && c.duplicateOf))); refresh(); } }, 'Seleccionar todos'),
        h('button', { class: 'btn btn--ghost btn--sm', onclick: () => { IMP.candidates.forEach(c => (c.selected = false)); refresh(); } }, 'Ninguno'),
        h('label', { class: 'inline', style: 'cursor:pointer;font-size:13.5px' }, h('input', { type: 'checkbox', class: 'm-check', checked: IMP.hideDup, onchange: ev => { IMP.hideDup = ev.target.checked; refresh(); } }), 'Ocultar los que ya están'),
        h('span', { class: 'grow' }), countEl),
      h('datalist', { id: 'impCats' }, ...catNames.map(n => h('option', { value: n }))),
      list),
    h('div', { class: 'dlg__foot' },
      h('button', { class: 'btn btn--secondary', onclick: importStep1 }, 'Atrás'),
      h('span', { class: 'grow' }),
      addBtn));
  refresh();
}
async function applyImport() {
  const items = IMP.candidates.filter(c => c.selected);
  if (!items.length) return;
  try {
    const r = await Host.call('import.apply', { doc: S.doc, items });
    const before = snapshot();
    S.doc.sections = r.doc.sections;
    commit(before, 'importar juegos');
    $('#dlgImport').close();
    render();
    toast(`${plural(items.length, 'juego añadido', 'juegos añadidos')}`, { label: 'Deshacer', fn: undo });
    queueCovers();
  } catch (err) { toast(err.message); }
}
async function refreshLinked() {
  const dlg = $('#dlgImport');
  const body = dlg.querySelector('.dlg__body');
  body.replaceChildren(h('div', { class: 'progress', role: 'status' }, icon('loader-circle', 'i spin'), h('span', { id: 'impProgress' }, 'Actualizando…')));
  try {
    const r = await Host.call('import.refresh', { doc: S.doc, sources: [...IMP.chosen] });
    const before = snapshot();
    S.doc.sections = r.doc.sections;
    commit(before, 'actualizar datos');
    dlg.close(); render();
    toast(r.changed ? `${plural(r.changed, 'juego actualizado', 'juegos actualizados')}` : 'Todo estaba al día', r.changed ? { label: 'Deshacer', fn: undo } : null);
  } catch (err) { body.replaceChildren(h('div', { class: 'msg msg--warn' }, icon('triangle-alert'), h('span', { text: err.message }))); }
}

// ───────────────────────────── Menús ─────────────────────────────
function wireMenu(btnSel, listSel, onOpen) {
  const btn = $(btnSel), list = $(listSel);
  const close = (focus) => { list.hidden = true; btn.setAttribute('aria-expanded', 'false'); if (focus) btn.focus(); };
  btn.addEventListener('click', () => {
    const open = list.hidden;
    document.querySelectorAll('.menu__list').forEach(l => { l.hidden = true; });
    document.querySelectorAll('[aria-haspopup="menu"]').forEach(b => b.setAttribute('aria-expanded', 'false'));
    if (open) { onOpen?.(list); list.hidden = false; btn.setAttribute('aria-expanded', 'true'); list.querySelector('button')?.focus(); }
  });
  list.addEventListener('keydown', ev => {
    const items = [...list.querySelectorAll('button')];
    const i = items.indexOf(document.activeElement);
    if (ev.key === 'ArrowDown') { ev.preventDefault(); items[(i + 1) % items.length].focus(); }
    if (ev.key === 'ArrowUp') { ev.preventDefault(); items[(i - 1 + items.length) % items.length].focus(); }
    if (ev.key === 'Escape') { ev.preventDefault(); close(true); }
    if (ev.key === 'Tab') close(false);
  });
  list.addEventListener('click', ev => { if (ev.target.closest('button')) close(false); });
  document.addEventListener('click', ev => { if (!list.hidden && !ev.target.closest('.menu')) close(false); });
}

// ───────────────────────────── Menú rápido (clic derecho, Mayús+F10 o botón «⋯») ─────────────────────────────
// items: { label, icon, kbd, fn, danger, disabled, sub: () => items } o '-' para una raya.
const CM = { stack: [], opener: null };
function closeMenus(focusBack) {
  CM.stack.forEach(m => m.remove());
  CM.stack = [];
  if (focusBack && CM.opener?.isConnected) CM.opener.focus();
  CM.opener = null;
}
function placeMenu(m, x, y, alt) {
  const r = m.getBoundingClientRect(), vw = innerWidth, vh = innerHeight;
  if (x + r.width > vw - 8) x = alt != null ? alt - r.width : vw - r.width - 8;
  if (y + r.height > vh - 8) y = Math.max(8, vh - r.height - 8);
  m.style.left = `${Math.max(8, x)}px`; m.style.top = `${Math.max(8, y)}px`;
}
function openMenu(items, at, level = 0, label) {
  CM.stack.splice(level).forEach(m => m.remove());
  const m = h('div', { class: 'cmenu', role: 'menu', 'aria-label': label || null, tabindex: '-1' });
  const btns = [];
  for (const it of items.filter(Boolean)) {
    if (it === '-') { if (m.lastChild && m.lastChild.tagName !== 'HR') m.append(h('hr')); continue; }
    const b = h('button', { type: 'button', role: 'menuitem', class: it.danger ? 'is-danger' : null, disabled: it.disabled || null,
      'aria-haspopup': it.sub ? 'menu' : null, 'aria-expanded': it.sub ? 'false' : null },
      it.icon ? icon(it.icon, 'i i--sm') : h('span', { class: 'cmenu__gap' }),
      h('span', { class: 'cmenu__label', text: it.label }),
      it.kbd ? h('kbd', { class: 'cmenu__kbd', text: it.kbd }) : null,
      it.sub ? icon('chevron-right', 'i i--sm cmenu__chev') : null);
    const openSub = (focus) => {
      const r = b.getBoundingClientRect();
      b.setAttribute('aria-expanded', 'true');
      const sm = openMenu(it.sub(), { x: r.right - 2, y: r.top - 6, alt: r.left + 2 }, level + 1, it.label);
      if (focus) sm.querySelector('button:not([disabled])')?.focus();
    };
    b.addEventListener('pointerenter', () => {
      m.querySelectorAll('[aria-expanded="true"]').forEach(x => x.setAttribute('aria-expanded', 'false'));
      if (it.sub) { clearTimeout(CM.t); CM.t = setTimeout(() => openSub(false), 120); }
      else { clearTimeout(CM.t); CM.stack.splice(level + 1).forEach(x => x.remove()); }
      b.focus({ preventScroll: true });
    });
    b.addEventListener('click', ev => {
      ev.stopPropagation();
      if (it.sub) { openSub(true); return; }
      closeMenus(false);
      it.fn?.();
    });
    b._sub = it.sub ? openSub : null;
    btns.push(b); m.append(b);
  }
  if (m.lastChild?.tagName === 'HR') m.lastChild.remove();
  m.addEventListener('keydown', ev => {
    const list = btns.filter(x => !x.disabled);
    const i = list.indexOf(document.activeElement);
    const go = j => { ev.preventDefault(); list[(j + list.length) % list.length]?.focus(); };
    switch (ev.key) {
      case 'ArrowDown': go(i + 1); break;
      case 'ArrowUp': go(i - 1); break;
      case 'Home': go(0); break;
      case 'End': go(list.length - 1); break;
      case 'ArrowRight': if (list[i]?._sub) { ev.preventDefault(); list[i]._sub(true); } break;
      case 'ArrowLeft': case 'Escape':
        ev.preventDefault(); ev.stopPropagation();
        if (level > 0) { const parent = CM.stack[level - 1]; CM.stack.splice(level).forEach(x => x.remove()); const pb = parent.querySelector('[aria-expanded="true"]'); pb?.setAttribute('aria-expanded', 'false'); pb?.focus(); }
        else if (ev.key === 'Escape') closeMenus(true);
        break;
      case 'Tab': ev.preventDefault(); closeMenus(true); break;
      default:
        // Escribir una letra salta a la primera opción que empieza por ella
        if (ev.key.length === 1 && !ev.ctrlKey && !ev.altKey) {
          const k = norm(ev.key), from = i + 1;
          const j = [...list.slice(from), ...list.slice(0, from)].find(x => norm(x.textContent).startsWith(k));
          if (j) { ev.preventDefault(); j.focus(); }
        }
    }
  });
  document.body.append(m);
  placeMenu(m, at.x, at.y, at.alt);
  CM.stack.push(m);
  return m;
}
/** Abre el menú en el puntero (clic derecho) o junto al elemento (teclado / botón ⋯). */
function contextMenu(items, from, label) {
  closeMenus(false);
  let at;
  if (from && 'clientX' in from && (from.clientX || from.clientY)) at = { x: from.clientX, y: from.clientY };
  else { const el = from?.currentTarget || from; const r = (el?.getBoundingClientRect ? el : document.activeElement).getBoundingClientRect(); at = { x: r.left + 8, y: r.bottom - 4 }; }
  CM.opener = document.activeElement;
  const m = openMenu(items, at, 0, label);
  m.querySelector('button:not([disabled])')?.focus({ preventScroll: true });
}
document.addEventListener('pointerdown', ev => { if (CM.stack.length && !ev.target.closest('.cmenu')) closeMenus(false); }, true);
addEventListener('resize', () => closeMenus(false));
addEventListener('blur', () => closeMenus(false));
document.addEventListener('scroll', ev => { if (CM.stack.length && !ev.target.closest?.('.cmenu')) closeMenus(false); }, true);

// Botón «⋯» sobre cada juego: el mismo menú con clic izquierdo
function moreBtn(e) {
  return h('button', { type: 'button', class: 'more-btn', tabindex: '-1', 'aria-label': `Más acciones de ${e.title || tr('Sin título')}`, title: 'Más acciones',
    onclick: ev => { ev.stopPropagation(); select(e.id, false); gameMenu(e.id, ev.currentTarget); } }, icon('ellipsis', 'i i--sm'));
}
async function renameEntry(id) {
  const x = findEntry(id); if (!x) return;
  const r = await prompt({ title: 'Renombrar juego', label: 'Título', value: x.e.title || '' });
  if (!r || !r.value.trim() || r.value.trim() === x.e.title) return;
  mutate('renombrar juego', () => { x.e.title = r.value.trim(); });
  restoreFocusToSelected();
}
function gameMenu(id, from) {
  const x = findEntry(id); if (!x) return;
  const { e, b, s } = x;
  const done = isDone(s);
  const url = storeUrl(F.store(e));
  const rating = F.num(e, 'rating');
  const stars = n => '★'.repeat(Math.floor(n / 2)) + (n % 2 ? '½' : '');
  const si = sections().indexOf(s);
  contextMenu([
    { label: 'Editar los detalles', icon: 'pencil', kbd: tr('Intro o F2').split(' ')[0], fn: () => { select(id, false); focusDetailTitle(); } },
    { label: 'Renombrar…', icon: 'type', fn: () => renameEntry(id) },
    '-',
    !done && sections().some(z => sectionKind(z) === 'completed') ? { label: 'Marcar como completado', icon: 'circle-check', kbd: tr('Ctrl+Intro'), fn: () => markPassed(id) } : null,
    { label: 'Mover a la sección', icon: 'folder-input', sub: () => sections().map((z, i) => ({
      label: i === si ? `${tr(z.name)} ${tr('(actual)')}` : z.name || '(sin nombre)', icon: KIND_ICON[sectionKind(z)], disabled: i === si, fn: () => moveToSection(id, i) })) },
    { label: 'Cambiar de saga', icon: 'tag', sub: () => [
      ...categoriesOf(s).filter(c => c.style !== 'none').map(c => ({ label: blockLabel(c), icon: c === b ? 'check' : null, disabled: c === b, fn: () => changeCategory(id, String(s.blocks.indexOf(c))) })),
      b.style !== 'none' ? { label: 'Sin categoría', fn: () => changeCategory(id, 'none') } : null,
      '-',
      { label: 'Nueva saga o género…', icon: 'folder-plus', fn: () => changeCategory(id, 'new') }] },
    { label: 'Puntuar', icon: 'star', sub: () => [
      ...[10, 9, 8, 7, 6, 5, 4, 3, 2, 1].map(n => ({ label: `${stars(n)}  ${n}/10`, icon: rating != null && Math.round(rating) === n ? 'check' : null,
        fn: () => mutate('puntuar', () => F.set(e, 'rating', F.make('rating', n))) })),
      rating != null ? '-' : null,
      rating != null ? { label: 'Quitar puntuación', icon: 'x', fn: () => mutate('quitar puntuación', () => F.set(e, 'rating', null)) } : null] },
    e.date !== today() ? { label: 'Poner la fecha de hoy', icon: 'calendar', fn: () => mutate('poner fecha de hoy', () => { e.date = today(); }) } : null,
    '-',
    url ? { label: 'Ver en la tienda', icon: 'external-link', fn: () => Host.call('open.url', { url }) } : null,
    !F.store(e) && Host.native && e.title ? { label: 'Buscar en Steam', icon: 'search', fn: () => findSteamId(e) } : null,
    S.settings.covers && e.title ? { label: 'Buscar la portada otra vez', icon: 'image', fn: () => {
      const k = coverKey(e); S.covers.delete(k); S.coverReq.delete(k); updateItem(id); queueCovers(true); announce('Buscando portada…'); } } : null,
    { label: 'Copiar el título', icon: 'copy', disabled: !e.title, fn: () => { navigator.clipboard?.writeText(e.title); toast('Título copiado'); } },
    { label: 'Duplicar', icon: 'copy-plus', fn: () => duplicateEntry(id) },
    { label: 'Subir', icon: 'arrow-up', kbd: 'Alt+↑', fn: () => moveEntryStep(id, -1) },
    { label: 'Bajar', icon: 'arrow-down', kbd: 'Alt+↓', fn: () => moveEntryStep(id, 1) },
    '-',
    { label: 'Eliminar', icon: 'trash-2', kbd: tr('Supr'), danger: true, fn: () => deleteEntry(id) }
  ], from, tr(`Menú de ${e.title || tr('Sin título')}`));
}
function groupMenu(sec, b, key, from) {
  const collapsed = S.collapsed.has(key);
  contextMenu([
    { label: 'Añadir un juego aquí', icon: 'plus', fn: () => addEntry(b) },
    b.style !== 'none' ? { label: 'Renombrar…', icon: 'pencil', fn: () => renameCategory(sec, b) } : null,
    { label: collapsed ? 'Desplegar' : 'Plegar', icon: 'chevron-down', fn: () => { collapsed ? S.collapsed.delete(key) : S.collapsed.add(key); renderList(); } },
    { label: 'Subir', icon: 'arrow-up', fn: () => moveBlock(sec, b, -1) },
    { label: 'Bajar', icon: 'arrow-down', fn: () => moveBlock(sec, b, 1) },
    '-',
    { label: 'Eliminar…', icon: 'trash-2', danger: true, fn: () => deleteCategory(sec, b) }
  ], from, tr(`Menú de ${blockLabel(b)}`));
}
function tabMenu(i, from) {
  const go = act => () => { if (S.sec !== i) { S.sec = i; S.textMode = false; S.statsMode = false; render(); } sectionAction(act); };
  const n = sections().length;
  contextMenu([
    { label: 'Renombrar esta sección…', icon: 'pencil', fn: go('rename') },
    { label: 'Mover a la izquierda', icon: 'arrow-left', disabled: i === 0, fn: go('left') },
    { label: 'Mover a la derecha', icon: 'arrow-right', disabled: i === n - 1, fn: go('right') },
    { label: 'Añadir nota separadora…', icon: 'info', fn: go('divider') },
    { label: 'Nueva sección…', icon: 'folder-plus', fn: go('add') },
    '-',
    { label: 'Eliminar esta sección…', icon: 'trash-2', danger: true, disabled: !!sections()[i]?.implicit, fn: go('delete') }
  ], from, tr(`Menú de ${sections()[i]?.name || ''}`));
}

// Cambiar de idioma sin reiniciar: textos fijos de index.html + todo lo que pinta app.js
function setLanguage(code) {
  I18N.set(code);
  I18N.translateTree(document.body);
  lastDirty = null;
  render(); dirtyChanged();
}

// ───────────────────────────── Tema ─────────────────────────────
function applyTheme() {
  const t = S.settings.theme;
  if (t === 'dark' || t === 'light') document.documentElement.dataset.theme = t; else delete document.documentElement.dataset.theme;
}

// ───────────────────────────── Teclado global ─────────────────────────────
let dragId = null;
document.addEventListener('keydown', ev => {
  if (ev.target.closest?.('.cmenu')) return;
  const inField = ev.target.closest('input, textarea, select, [contenteditable]');
  const ctrl = ev.ctrlKey || ev.metaKey;
  const k = ev.key.toLowerCase();
  if (ev.key === 'F11') { ev.preventDefault(); toggleFullScreen(); return; }
  if (document.querySelector('dialog[open]') && !(ctrl && k === 's')) return;
  if (ctrl && k === 's') { ev.preventDefault(); ev.shiftKey ? saveAsMenu() : save(); return; }
  if (ctrl && k === 'o') { ev.preventDefault(); openFile(); return; }
  if (ctrl && k === 'n') { ev.preventDefault(); newFile(); return; }
  if (ctrl && k === 'f') { ev.preventDefault(); $('#q').focus(); $('#q').select(); return; }
  if (ctrl && (k === '+' || k === '=' || k === '-' || k === '0')) { ev.preventDefault(); Host.call('zoom', { dir: k === '0' ? 0 : k === '-' ? -1 : 1 }); return; }
  if (ev.key === 'F1') { ev.preventDefault(); openHelp(); return; }
  if (!S.doc) return;
  if (ctrl && ev.key === 'Tab') { ev.preventDefault(); const n = sections().length; selectSection((S.sec + (ev.shiftKey ? n - 1 : 1)) % n, true); return; }
  if (ctrl && k === 'j') { ev.preventDefault(); openNext(); return; }
  if (ctrl && k === 'e') { ev.preventDefault(); toggleStats(); return; }
  if (ctrl && ev.key === 'Enter' && S.selected) { ev.preventDefault(); if (editBefore != null) ev.target.blur?.(); markPassed(S.selected); return; }
  if (!inField && ctrl && k === 'z') { ev.preventDefault(); ev.shiftKey ? redo() : undo(); return; }
  if (!inField && ctrl && k === 'y') { ev.preventDefault(); redo(); return; }
  if (inField) { if (ev.key === 'Escape' && ev.target.id === 'q') { ev.target.value = ''; S.query = ''; render(); } return; }
  if (ev.key === 'Insert') { ev.preventDefault(); addEntry(); return; }
  if (ev.key === 'Delete' && S.selected) { ev.preventDefault(); deleteEntry(S.selected); return; }
  if (ev.key === 'F2' && S.selected) { ev.preventDefault(); focusDetailTitle(); return; }
  if (ev.key === 'Escape' && narrow() && S.detailOpen) { closeDetail(); return; }
  if (ev.key === 'Escape' && S.fullScreen) toggleFullScreen(false);   // Esc también sale de la pantalla completa
});
// Pantalla completa sin bordes (MainWindow.SetFullScreen)
async function toggleFullScreen(on) {
  const r = await Host.call('window.fullscreen', on == null ? {} : { on });
  if (!Host.native) setFullScreen(on ?? !S.fullScreen);   // en el navegador de prueba solo cambia el botón
  else if (r) setFullScreen(r.on);
}
function setFullScreen(on) {
  if (S.fullScreen === on) return;
  S.fullScreen = on;
  const b = $('#btnFull');
  b.setAttribute('aria-pressed', String(on));
  b.setAttribute('aria-label', tr(on ? 'Salir de la pantalla completa' : 'Pantalla completa'));
  b.title = tr(on ? 'Salir de la pantalla completa (F11 o Esc)' : 'Pantalla completa (F11)');
  b.querySelector('use').setAttribute('href', `icons/sprite.svg#${on ? 'minimize' : 'maximize'}`);
  announce(on ? 'Pantalla completa. F11 o Esc para salir.' : 'Pantalla completa desactivada');
}
Host.on('fullScreen', d => setFullScreen(!!d.on));
function saveAsMenu() { $('#btnSaveAs').click(); }

// Pestañas: flechas izquierda/derecha
$('#tabs').addEventListener('keydown', ev => {
  const n = sections().length;
  if (ev.key === 'ArrowRight') { ev.preventDefault(); selectSection((S.sec + 1) % n, true); }
  if (ev.key === 'ArrowLeft') { ev.preventDefault(); selectSection((S.sec - 1 + n) % n, true); }
  if (ev.key === 'Home') { ev.preventDefault(); selectSection(0, true); }
  if (ev.key === 'End') { ev.preventDefault(); selectSection(n - 1, true); }
  if (ev.key === 'F2') { ev.preventDefault(); sectionAction('rename'); }
});

// Arrastrar archivos a la ventana
document.addEventListener('dragover', ev => { if ([...(ev.dataTransfer?.types || [])].includes('Files')) { ev.preventDefault(); $('#main').classList.add('over'); } });
document.addEventListener('dragleave', ev => { if (!ev.relatedTarget) $('#main').classList.remove('over'); });
document.addEventListener('drop', ev => {
  if (!ev.dataTransfer?.files?.length) return;
  ev.preventDefault(); $('#main').classList.remove('over');
  Host.dropFiles(ev.dataTransfer.files);
});
Host.on('fileDropped', d => openFile(d.path));
Host.on('saveAndClose', async () => { if (await save()) Host.call('app.quit'); });
Host.on('zoomChanged', d => announce(`Zoom ${Math.round(d.factor * 100)}%`));

// ───────────────────────────── Mando (Gamepad API) ─────────────────────────────
// Convive con el teclado: el mando hace lo mismo que las teclas (flechas, Intro, Esc…) sobre el elemento con el foco,
// sin cambiar cómo funciona el teclado. Mapa «standard» de Chromium (Xbox: A/B/X/Y, LB/RB; PlayStation: ✕/○/□/△, L1/R1).
const PAD_BUTTONS = [['a', 0], ['b', 1], ['x', 2], ['y', 3], ['lb', 4], ['rb', 5], ['back', 8], ['start', 9]];
// Lectura pura del mando: pulsaciones (solo al bajar el botón) y direcciones con repetición suave
// (primera repetición a los 400 ms y luego cada 130 ms). Zona muerta del stick con histéresis.
class PadReader {
  constructor(o = {}) {
    this.delay = o.delay ?? 400; this.rate = o.rate ?? 130; this.dead = o.dead ?? 0.55; this.release = o.release ?? 0.35;
    this.prev = []; this.dir = null; this.next = 0;
  }
  update(snap, t) {
    const out = [], b = snap.buttons || [];
    for (const [name, i] of PAD_BUTTONS) if (b[i] && !this.prev[i]) out.push(name);
    this.prev = b.slice();
    let d = b[12] ? 'up' : b[13] ? 'down' : b[14] ? 'left' : b[15] ? 'right' : null;
    if (!d) {
      const x = snap.axes?.[0] || 0, y = snap.axes?.[1] || 0, ax = Math.abs(x), ay = Math.abs(y);
      if (Math.max(ax, ay) >= (this.dir ? this.release : this.dead)) d = ax > ay ? (x > 0 ? 'right' : 'left') : (y > 0 ? 'down' : 'up');
    }
    if (d !== this.dir) { this.dir = d; if (d) { out.push(d); this.next = t + this.delay; } }
    else if (d && t >= this.next) { out.push(d); this.next = t + this.rate; }
    return out;
  }
  reset() { this.prev = []; this.dir = null; }
}

const Pad = {
  reader: new PadReader(), raf: 0, shown: false,
  // Se lee unas 60 veces por segundo con un temporizador (no con requestAnimationFrame: WebView2 lo detiene si la
  // ventana queda tapada aunque siga activa). Solo mientras haya un mando conectado.
  start() { if (!this.raf && navigator.getGamepads) this.raf = setTimeout(() => this.tick(performance.now()), 16); },
  tick(t) {
    this.raf = 0;
    const pads = [...(navigator.getGamepads() || [])].filter(p => p && p.connected);
    if (!pads.length) { this.show(false); this.reader.reset(); return; }   // sin mando no se sigue leyendo
    const p = pads.find(x => x.buttons.some(b => b.pressed) || x.axes.some(a => Math.abs(a) > 0.5)) || pads[0];
    const acts = this.reader.update({ buttons: p.buttons.map(b => b.pressed || b.value > 0.5), axes: [p.axes[0], p.axes[1]] }, t);
    // Solo con la ventana activa: el mando es de todo el sistema y no debe tocar la lista mientras se usa otra app
    if (acts.length && document.hasFocus()) {
      this.show(true);
      document.documentElement.classList.add('pad-mode');
      for (const a of acts) { try { padAction(a); } catch (err) { console.error(err); } }
    }
    this.raf = setTimeout(() => this.tick(performance.now()), 16);
  },
  show(on) {
    if (on === this.shown) return;
    this.shown = on;
    $('#statusPad').hidden = !on;
    if (!on) document.documentElement.classList.remove('pad-mode');
  }
};
window.addEventListener('gamepadconnected', ev => {
  Pad.show(true); Pad.start();
  announce(`Mando conectado: ${padName(ev.gamepad)}. A abre, B cierra, Y marca como completado y LB o RB cambian de sección.`);
});
window.addEventListener('gamepaddisconnected', () => {
  if (![...(navigator.getGamepads?.() || [])].some(p => p && p.connected)) { Pad.show(false); announce('Mando desconectado'); }
});
// Teclado o ratón de verdad: se quita el realce de foco propio del mando (los eventos que lanza el mando no son «isTrusted»)
for (const ev of ['keydown', 'pointerdown']) document.addEventListener(ev, e => { if (e.isTrusted) document.documentElement.classList.remove('pad-mode'); }, true);
function padName(g) { return (g?.id || '').replace(/\s*\(.*$/, '').trim() || tr('mando'); }

const reducedMotion = () => document.documentElement.dataset.motion === 'off' || matchMedia('(prefers-reduced-motion: reduce)').matches;
const PAD_KEYS = { up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight' };
function padKey(el, key) { (el && el !== document.body ? el : document).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })); }
function padFocusables(root) {
  return [...root.querySelectorAll('button, a[href], input, select, textarea, summary, [tabindex]:not([tabindex="-1"])')]
    .filter(el => !el.disabled && el.getAttribute('tabindex') !== '-1' && !el.closest('[hidden], [inert]') && el.getClientRects().length > 0);
}
// Mueve el foco al control anterior o siguiente de una zona (inspector, diálogo, estadísticas), en el orden de tabulación
function padMoveIn(root, dir) {
  const list = padFocusables(root);
  if (!list.length) return;
  const i = list.indexOf(document.activeElement);
  const next = i < 0 ? list[0] : list[Math.max(0, Math.min(list.length - 1, i + (dir === 'up' || dir === 'left' ? -1 : 1)))];
  next.focus({ preventScroll: true });
  next.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'auto' : 'smooth' });
}
function padStepSelect(sel, step) {
  const i = Math.max(0, Math.min(sel.options.length - 1, sel.selectedIndex + step));
  if (i === sel.selectedIndex) return;
  sel.selectedIndex = i;
  sel.dispatchEvent(new Event('change', { bubbles: true }));
  announce(sel.options[i].text);
}
const padItem = el => el?.closest?.('#main .card, #main .row');

function padAction(a) {
  const ae = document.activeElement && document.activeElement !== document.body ? document.activeElement : null;
  const dlg = document.querySelector('dialog[open]');
  const menu = ae?.closest('.cmenu, .menu__list');
  const inDetail = ae?.closest('#detail');

  if (PAD_KEYS[a]) {
    if (menu) return padKey(ae, PAD_KEYS[a]);
    if (dlg) return padMoveIn(dlg, a);
    if (padItem(ae)) return padKey(padItem(ae), PAD_KEYS[a]);   // misma navegación que con las flechas
    if (ae?.closest('#tabs')) { if (a === 'down') restoreFocusToSelected(); else if (a !== 'up') padKey(ae, PAD_KEYS[a]); return; }
    if (inDetail) {
      if (ae.tagName === 'SELECT' && (a === 'left' || a === 'right')) return padStepSelect(ae, a === 'right' ? 1 : -1);
      return padMoveIn($('#detail'), a);
    }
    if (S.doc && (S.statsMode || S.textMode)) return padMoveIn($('#main'), a);
    return S.doc ? restoreFocusToSelected() : padMoveIn($('#main'), a);
  }

  switch (a) {
    case 'a': {   // abrir o confirmar
      if (menu || dlg) { if (ae && ae !== dlg) ae.click(); return; }
      const item = padItem(ae);
      if (item) {
        const id = item.dataset.id;
        if (S.selected === id) { focusDetailTitle(); return; }
        select(id, false);
        setTimeout(focusDetailTitle, 80);   // tras pintar el inspector del juego nuevo
        return;
      }
      if (inDetail && ae.matches('input:not([type="checkbox"]):not([type="radio"]), textarea')) {
        ae.blur();   // al salir del campo se guarda el cambio (como con Tab)
        restoreFocusToSelected();
        announce('Cambio aplicado');
        return;
      }
      if (ae?.tagName === 'SELECT') return padMoveIn(inDetail ? $('#detail') : (ae.closest('dialog') || $('#main')), 'down');
      if (ae && ae.matches('button, summary, a[href], input[type="checkbox"], input[type="radio"], [role="tab"], [role="menuitem"]')) { ae.click(); return; }
      if (S.doc) restoreFocusToSelected();
      return;
    }
    case 'b': {   // cerrar o volver
      if (menu) return padKey(ae, 'Escape');
      if (dlg) { dlg.close('cancel'); return; }
      if (ae?.id === 'q' || (S.query && !inDetail)) {
        $('#q').value = ''; S.query = ''; render(); restoreFocusToSelected(); announce('Búsqueda borrada');
        return;
      }
      if (inDetail) {
        ae.blur();
        if (narrow()) closeDetail(); else restoreFocusToSelected();
        return;
      }
      if (S.statsMode) return toggleStats();
      if (S.textMode) return toggleText();
      if (narrow() && S.detailOpen) closeDetail();
      return;
    }
    case 'y': {   // marcar como completado hoy
      if (dlg || menu || !S.doc || !S.selected) return;
      const x = findEntry(S.selected);
      if (!x) return;
      if (isDone(x.s)) { announce(`«${x.e.title || 'Sin título'}» ya está completado`); return; }
      if (editBefore != null) ae?.blur();
      markPassed(S.selected);
      restoreFocusToSelected();
      return;
    }
    case 'lb': case 'rb': {   // sección anterior o siguiente
      if (dlg || menu || !S.doc || !sections().length) return;
      if (editBefore != null) ae?.blur();
      const n = sections().length;
      selectSection((S.sec + (a === 'rb' ? 1 : n - 1)) % n, true);
      return;
    }
  }
}

// ───────────────────────────── Arranque ─────────────────────────────
async function init() {
  $('#btnNew').onclick = newFile;
  $('#btnOpen').onclick = () => openFile();
  $('#btnSave').onclick = () => save();
  $('#btnUndo').onclick = undo;
  $('#btnRedo').onclick = redo;
  $('#btnText').onclick = toggleText;
  $('#btnStats').onclick = toggleStats;
  $('#btnExtReload').onclick = () => reloadFromDisk(false);
  $('#btnExtKeep').onclick = keepMine;
  $('#btnImport').onclick = openImport;
  $('#btnSettings').onclick = openSettings;
  $('#btnHelp').onclick = openHelp;
  $('#btnFull').onclick = () => toggleFullScreen();
  wireMenu('#btnSaveAs', '#saveAsMenu');
  $('#saveAsMenu').addEventListener('click', ev => { const b = ev.target.closest('[data-saveas]'); if (b) save(b.dataset.saveas); });
  wireMenu('#btnRecent', '#recentMenu', list => {
    list.replaceChildren(...(S.recent.length ? S.recent.map(r => h('button', { role: 'menuitem', onclick: () => openFile(r.path) },
      h('img', { class: 'brand', src: /\.md$/i.test(r.path) ? 'icons/markdown.svg' : 'icons/text.svg', alt: '' }),
      h('span', { style: 'min-width:0' }, r.path.split(/[\\/]/).pop(), h('span', { class: 'path' }, r.path)))) : [h('button', { role: 'menuitem', disabled: true }, 'Nada reciente')]));
  });
  wireMenu('#btnSectionMenu', '#sectionMenu');
  $('#sectionMenu').addEventListener('click', ev => { const b = ev.target.closest('[data-sec]'); if (b) sectionAction(b.dataset.sec); });
  ['#dlgImport', '#dlgSettings', '#dlgHelp', '#dlgNext'].forEach(s => closeOnBackdrop($(s)));
  $('#dlgWelcome').addEventListener('close', onWelcomeClosed);
  $('#dlgNext').addEventListener('close', () => { if (!document.activeElement || document.activeElement === document.body) ($('#btnNext') || $('#main')).focus(); });
  $('#dlgImport').addEventListener('close', () => { $('#statusLeft').textContent = ''; render(); restoreFocusToSelected(); });
  $('#dlgSettings').addEventListener('close', () => render());

  let qTimer;
  $('#q').addEventListener('input', ev => { clearTimeout(qTimer); qTimer = setTimeout(() => { S.query = ev.target.value; S.textMode = false; S.statsMode = false; render(); }, 120); });
  const hi = $('#headerInput');
  hi.addEventListener('focus', beginEdit);
  hi.addEventListener('input', () => {
    beginEdit();
    const upd = hi.dataset.updated;
    S.doc.header = hi.value.trim() + (upd ? ` (Actualizado el ${upd})` : '');
    S.doc.hasHeader = true;
    dirtyChanged();
  });
  hi.addEventListener('blur', () => endEdit('editar título de la lista'));

  const r = await Host.call('init');
  S.settings = r?.settings || {};
  I18N.set(S.settings.language);
  I18N.translateTree(document.body, true);
  S.recent = r?.recent || [];
  S.sagas = r?.sagas || [];
  // Sin menú del navegador fuera de los campos de texto (el menú propio se abre en juegos, sagas y pestañas)
  document.addEventListener('contextmenu', ev => {
    if (ev.defaultPrevented || ev.target.closest('input, textarea, [contenteditable]') || String(getSelection())) return;
    ev.preventDefault();
  });
  if (r?.version) $('#statusVer').textContent = /^\d/.test(r.version) ? `v${r.version}` : r.version;
  applyTheme();
  if (r?.file) loadFile(r.file); else { render(); enter(); }
  // Primera vez: bienvenida. Si no, y no se ha abierto un archivo, se ofrece continuar con la última lista.
  if (!S.settings.onboarded) openWelcome(0);
  else if (!r?.file && r?.resume?.path) askResume(r.resume.path);
  // Un mando ya conectado antes de abrir la app aparece al pulsar cualquier botón (gamepadconnected)
  if ([...(navigator.getGamepads?.() || [])].some(p => p && p.connected)) Pad.start();
  dirtyChanged();
}
init();
