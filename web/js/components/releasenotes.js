// Release notes (GitHub Markdown + HTML) → plain structured text for the update banner and the "Novidades" dialog.
// Nothing from the notes is ever inserted as HTML: parsing yields plain strings, rendering uses textContent.

/** Markdown/HTML inline markup → plain text ("**a** [b](url) `c` <img …>" → "a b c"). */
export function plainText(s) {
  return String(s || '')
    .replace(/<[^>]*>/g, ' ')                       // HTML tags (images, <p>, <details>…)
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')          // markdown images
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')        // links → their text
    .replace(/(\*\*|__)(.*?)\1/g, '$2')             // bold
    .replace(/(^|[^\w*])[*_]([^*_\n]+)[*_](?!\w)/g, '$1$2') // italics
    .replace(/`([^`]*)`/g, '$1')                    // inline code
    .replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"')
    .replace(/\s+/g, ' ')
    .replace(/\s+([.,;:!?)])/g, '$1')
    .trim();
}

// Leading emoji / symbols of a heading ("✨ Novidades" → "Novidades").
const stripIcon = (s) => s.replace(/^[^\p{L}\p{N}]+/u, '').trim();

/**
 * Sections of "What changed": [{ title, items: [{ lead, text }] }]. Reads the "###" subsections (with "-" bullets)
 * under the first "##" heading that has any; stops at the next "##". `lead` is the bold opening of an item, if any.
 */
export function parseReleaseNotes(md) {
  const sections = [];
  let inChanges = false, current = null;
  for (const raw of String(md || '').split(/\r?\n/)) {
    const line = raw.trim();
    const h2 = /^##\s+(.*)$/.exec(line);
    if (h2 && !line.startsWith('###')) {
      if (sections.length) break;
      inChanges = true; current = null;
      continue;
    }
    if (!inChanges) continue;
    const h3 = /^###\s+(.*)$/.exec(line);
    if (h3) { current = { title: stripIcon(plainText(h3[1])), items: [] }; sections.push(current); continue; }
    const li = /^[-*]\s+(.*)$/.exec(line);
    if (li && current) {
      const lead = /^\*\*(.+?)\*\*/.exec(li[1]);
      current.items.push({ lead: lead ? plainText(lead[1]).replace(/[:.]$/, '') : '', text: plainText(li[1]) });
    } else if (line && current && current.items.length && !/^[|>#<]/.test(line)) {
      const last = current.items[current.items.length - 1];   // wrapped bullet continuation
      last.text = `${last.text} ${plainText(line)}`.trim();
    }
  }
  return sections.filter((s) => s.items.length);
}

/** One-line summary for the banner: the highlights of the first section ("A · B · C"), else the first sentence. */
export function notesSummary(md, max = 3) {
  const sections = parseReleaseNotes(md);
  if (sections.length) {
    const items = sections[0].items;
    const leads = items.map((i) => i.lead).filter(Boolean).slice(0, max);
    const more = items.length - leads.length;
    if (leads.length) return leads.join(' · ') + (more > 0 ? ` e mais ${more}` : '');
    return items[0].text;
  }
  const text = plainText(md);
  return text.length > 140 ? `${text.slice(0, 139)}…` : text;
}

/** DOM for the "Novidades" dialog body (built with textContent only). */
export function notesNode(md) {
  const wrap = document.createElement('div');
  wrap.className = 'rn';
  const sections = parseReleaseNotes(md);
  if (!sections.length) {
    const p = document.createElement('p');
    p.className = 'muted';
    p.textContent = plainText(md) || 'Sem descrição para esta versão.';
    wrap.append(p);
    return wrap;
  }
  for (const s of sections) {
    const sec = document.createElement('section');
    sec.className = 'rn-sec';
    const h = document.createElement('h3');
    h.textContent = s.title;
    const ul = document.createElement('ul');
    for (const it of s.items) {
      const li = document.createElement('li');
      if (it.lead) {
        const b = document.createElement('b');
        b.textContent = it.lead;
        // Keep the original punctuation after the bold lead ("Lead: text" / "Lead mostra…").
        li.append(b, it.text.startsWith(it.lead) ? it.text.slice(it.lead.length) : ` ${it.text}`);
      } else li.textContent = it.text;
      ul.append(li);
    }
    sec.append(h, ul);
    wrap.append(sec);
  }
  return wrap;
}
