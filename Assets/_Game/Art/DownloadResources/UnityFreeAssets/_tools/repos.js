// repos.js — list KayKit GitHub org repos (full_name, size)
(async () => {
  for (const org of ['KayKit-Game-Assets']) {
    const r = await fetch(`https://api.github.com/users/${org}/repos?per_page=100`, { headers: { 'User-Agent': 'Mozilla/5.0' } });
    const j = await r.json();
    for (const repo of j) console.log(repo.full_name + ' | size=' + repo.size + 'KB | ' + (repo.description || '').slice(0, 90));
  }
})();
