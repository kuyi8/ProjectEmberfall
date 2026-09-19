// dl.js -- manifest.txt lines: <url>\t<destdir>  ; downloads each file, follows redirects,
// writes into destdir, prints result. Run: node dl.js
const fs = require('fs');
const path = require('path');
const https = require('https');
const http = require('http');

const lines = fs.readFileSync(path.join(__dirname, 'manifest.txt'), 'utf8').split(/\r?\n/).filter(l => l.trim() && !l.trim().startsWith('#'));
const BASE = 'D:\\desk\\DeepseekHarness\\artres\\UnityFreeAssets';

function get(url, out, cb) {
  const mod = url.startsWith('https') ? https : http;
  const req = mod.get(url, { headers: { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', 'Referer': 'https://opengameart.org/' } }, res => {
    if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
      res.resume();
      return get(res.headers.location, out, cb);
    }
    if (res.statusCode !== 200) { res.resume(); return cb(new Error('HTTP ' + res.statusCode)); }
    const len = parseInt(res.headers['content-length'] || '0', 10);
    let bytes = 0;
    const ws = fs.createWriteStream(out);
    res.on('data', d => bytes += d.length);
    res.pipe(ws);
    ws.on('finish', () => ws.close(() => cb(null, bytes, len)));
    ws.on('error', e => cb(e));
    res.on('error', e => { ws.destroy(); cb(e); });
  });
  req.setTimeout(120000, () => { req.destroy(new Error('timeout')); });
  req.on('error', e => cb(e));
}

(async () => {
  let ok = 0, fail = 0;
  for (const line of lines) {
    const [url, dir] = line.split('\t');
    const fname = decodeURIComponent(url.split('/').pop().split('?')[0]);
    const dest = path.join(BASE, dir.trim());
    fs.mkdirSync(dest, { recursive: true });
    const out = path.join(dest, fname);
    await new Promise(resolve => {
      let attempts = 0;
      const attempt = () => {
        attempts++;
        get(url, out, (err, bytes, len) => {
          if (!err && len && bytes !== len) err = new Error('size mismatch ' + bytes + '/' + len);
          if (err && attempts < 3) { console.log('RETRY ' + fname + ' (' + err.message + ')'); return attempt(); }
          if (err) { fail++; console.log('FAIL  ' + fname + ' :: ' + err.message); }
          else { ok++; console.log('OK    ' + fname + ' :: ' + (bytes / 1048576).toFixed(1) + ' MB'); }
          resolve();
        });
      };
      attempt();
    });
  }
  console.log('DONE ok=' + ok + ' fail=' + fail);
})();
