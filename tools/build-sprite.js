// Junta los glifos ui-*.svg (Lucide, de icons.mjs) en un único sprite.svg con <symbol id="nombre">.
// Uso: node tools/build-sprite.js src/ListaJuegos.App/ui/icons
const fs = require('fs');
const path = require('path');
const dir = process.argv[2];
let out = '<svg xmlns="http://www.w3.org/2000/svg" style="display:none">\n';
for (const f of fs.readdirSync(dir).filter(f => f.startsWith('ui-') && f.endsWith('.svg'))) {
  const s = fs.readFileSync(path.join(dir, f), 'utf8');
  const vb = (s.match(/viewBox="([^"]+)"/) || [])[1] || '0 0 24 24';
  const inner = s.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');
  const id = f.slice(3, -4);
  out += `<symbol id="${id}" viewBox="${vb}" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${inner}</symbol>\n`;
}
out += '</svg>\n';
fs.writeFileSync(path.join(dir, 'sprite.svg'), out);
console.log('sprite.svg', out.length, 'bytes');
