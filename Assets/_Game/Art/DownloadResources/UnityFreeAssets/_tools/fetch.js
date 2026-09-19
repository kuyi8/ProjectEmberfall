// fetch.js <url> [url...] — crawl pages, print direct .zip links and /content/ links
const pages = process.argv.slice(2);
(async () => {
  for (const p of pages) {
    try {
      const r = await fetch(p, { headers: { 'User-Agent': 'Mozilla/5.0' }, redirect: 'follow' });
      const t = await r.text();
      console.log('=== ' + p + ' [' + r.status + '] len=' + t.length);
      const zips = [...new Set(t.match(/https?:\/\/[^"'\s<>]*\.zip/gi) || [])];
      zips.forEach(z => console.log('ZIP  ' + z));
      const arts = [...new Set(t.match(/\/content\/[\w-]+/g) || [])].filter(a => !a.includes('search'));
      arts.forEach(a => console.log('ART  ' + a));
      // any direct file-ish links (drive/dropbox/kenney media)
      const dl = [...new Set(t.match(/https?:\/\/[^"'\s<>]*(?:drive\.google|dropbox|media\/pages\/assets|files\.opengameart)[^"'\s<>]*/gi) || [])];
      dl.slice(0, 20).forEach(d => console.log('DL   ' + d.replace(/&amp;/g, '&')));
    } catch (e) { console.log('ERR ' + p + ' ' + e.message); }
  }
})();
