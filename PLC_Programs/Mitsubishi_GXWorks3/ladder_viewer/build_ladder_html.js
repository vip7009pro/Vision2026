// Build Ladder_FIFO_Fixed_v3.html từ Ladder_FIFO_Fixed_v3.md + ladder_template.html
// Chạy: node build_ladder_html.js
const fs = require('fs');
const path = require('path');

const dir = __dirname;
const md = fs.readFileSync(path.join(dir, '..', 'Ladder_FIFO_Fixed_v3.md'), 'utf8');
const tpl = fs.readFileSync(path.join(dir, 'ladder_template.html'), 'utf8');

const blocks = [];
const re = /^###\s+(KHOI_\S+)[^\n]*\n```[^\n]*\n([\s\S]*?)```/gm;
let m;
while ((m = re.exec(md))) blocks.push({ name: m[1], code: m[2].replace(/\r/g, '') });
if (!blocks.length) throw new Error('Không tìm thấy khối KHOI_ nào trong file .md');

const out = tpl.replace('/*__BLOCKS__*/[]', JSON.stringify(blocks, null, 1));
if (out === tpl) throw new Error('Không tìm thấy placeholder /*__BLOCKS__*/[] trong template');
const dst = path.join(dir, '..', 'Ladder_FIFO_Fixed_v3.html');
fs.writeFileSync(dst, out, 'utf8');
console.log(`OK: ${blocks.length} khối -> ${dst}`);
