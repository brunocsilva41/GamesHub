// Dev-mode mock of the VARIANTS service: groups collapse into their primary game (members leave `games`),
// name-similarity suggestions, dismissals. Mirrors IVariantService semantics used by the bridge.
const norm = (s) => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const key = (s) => norm(s).replace(/[^a-z0-9 ]+/g, ' ').replace(/\s+/g, ' ').trim();
const idsKey = (ids) => [...ids].sort().join('|');

export function variantCommands({ games, find, emitGames, ok, fail }) {
  const stash = new Map();   // non-primary members (not in `games`)
  const groups = [];
  const dismissed = new Set();
  let seq = 0;
  const any = (id) => find(id) || stash.get(id);
  const groupOf = (id) => groups.find((g) => g.memberIds.includes(id));
  const clone = (g) => JSON.parse(JSON.stringify(g));

  function apply(grp) {
    for (const id of grp.memberIds) {
      if (id === grp.primaryId) continue;
      const i = games.findIndex((g) => g.id === id);
      if (i >= 0) { stash.set(id, games[i]); games.splice(i, 1); }
    }
    let p = find(grp.primaryId);
    if (!p) { p = stash.get(grp.primaryId); stash.delete(grp.primaryId); games.push(p); }
    const ordered = [grp.primaryId, ...grp.memberIds.filter((x) => x !== grp.primaryId)];
    p.variants = ordered.map((id) => ({ id, label: grp.labels[id] || any(id)?.name || id }));
  }

  function release(grp) {
    const p = find(grp.primaryId);
    if (p) p.variants = [];
    for (const id of grp.memberIds) {
      const g = stash.get(id);
      if (g) { stash.delete(id); g.variants = []; games.push(g); }
    }
  }

  function create(ids, primaryId, labels = {}) {
    const grp = { id: `vg${++seq}`, primaryId, memberIds: [...ids], labels: { ...labels } };
    groups.push(grp);
    apply(grp);
    return grp;
  }

  function suggest() {
    const pool = games.filter((g) => !g.hidden && !groupOf(g.id)).sort((a, b) => a.name.length - b.name.length);
    const used = new Set();
    const out = [];
    for (const base of pool) {
      const b = key(base.name);
      if (used.has(base.id) || b.length < 4) continue;
      const members = pool.filter((o) => o !== base && !used.has(o.id) && key(o.name).startsWith(`${b} `));
      if (!members.length) continue;
      const ids = [base.id, ...members.map((m) => m.id)];
      if (dismissed.has(idsKey(ids))) continue;
      ids.forEach((i) => used.add(i));
      const labels = { [base.id]: 'Padrão' };
      for (const m of members) labels[m.id] = m.name.slice(base.name.length).replace(/^[\s:–-]+/, '') || m.name;
      out.push({ id: '', primaryId: base.id, memberIds: ids, labels });
    }
    return out;
  }

  // Initial demo group: Minecraft (Java) + Minecraft Bedrock.
  if (find('folder:minecraft.lnk') && find('folder:minecraft bedrock.lnk')) {
    create(['folder:minecraft.lnk', 'folder:minecraft bedrock.lnk'], 'folder:minecraft.lnk',
      { 'folder:minecraft.lnk': 'Java Edition', 'folder:minecraft bedrock.lnk': 'Bedrock Edition' });
  }

  return {
    variantGroups: () => ({ ok: true, data: { groups: groups.map(clone) } }),
    variantSuggestions: () => ({ ok: true, data: { groups: suggest() } }),

    groupVariants({ ids = [], primaryId }) {
      const unique = [...new Set(ids)];
      if (unique.length < 2) return fail('Escolha pelo menos dois jogos para agrupar.');
      if (!unique.includes(primaryId)) return fail('O jogo principal precisa fazer parte do grupo.');
      if (unique.some((id) => !any(id))) return fail('Jogo não encontrado.');
      const labels = {};
      const members = new Set(unique);
      for (const grp of groups.filter((g) => g.memberIds.some((m) => members.has(m)))) {
        grp.memberIds.forEach((m) => members.add(m));
        Object.assign(labels, grp.labels);
        release(grp);
        groups.splice(groups.indexOf(grp), 1);
      }
      create([...members], primaryId, labels);
      emitGames();
      return ok(`${members.size} jogos agrupados em ${any(primaryId).name}.`, primaryId);
    },

    ungroupVariants({ groupId }) {
      const grp = groups.find((g) => g.id === groupId);
      if (!grp) return fail('Grupo não encontrado.');
      release(grp);
      groups.splice(groups.indexOf(grp), 1);
      emitGames();
      return ok('Variações desagrupadas. Cada jogo voltou a ter seu próprio card.', grp.primaryId);
    },

    setVariantLabel({ id, label = '' }) {
      const grp = groupOf(id);
      if (!grp) return fail('Este jogo não faz parte de um grupo.');
      const text = String(label).trim().slice(0, 60);
      if (text) grp.labels[id] = text; else delete grp.labels[id];
      apply(grp);
      emitGames();
      return ok(text ? `Variação renomeada para "${text}".` : 'Nome da variação restaurado.', grp.primaryId);
    },

    setVariantPrimary({ groupId, primaryId }) {
      const grp = groups.find((g) => g.id === groupId);
      if (!grp) return fail('Grupo não encontrado.');
      if (!grp.memberIds.includes(primaryId)) return fail('Este jogo não faz parte do grupo.');
      release(grp);
      grp.primaryId = primaryId;
      apply(grp);
      emitGames();
      return ok(`${any(primaryId).name} agora é a versão principal.`, primaryId);
    },

    dismissVariants({ ids = [] }) {
      dismissed.add(idsKey(ids));
      return ok('Sugestão dispensada.');
    },
  };
}
