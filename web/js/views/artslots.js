// Image slots on the details page: drop an image (setArt + File), click to pick, reset (clearArt).
import { icon } from '../components/icons.js';
import { makeDropTarget } from '../components/dropzone.js';
import { toast } from '../components/toast.js';
import * as A from '../actions.js';

const KINDS = [
  { kind: 'header', label: 'Cabeçalho', hint: '460 × 215' },
  { kind: 'capsule', label: 'Capa (retrato)', hint: '600 × 900' },
  { kind: 'hero', label: 'Fundo', hint: '1920 × 620' },
  { kind: 'logo', label: 'Logotipo', hint: 'PNG transparente' },
  { kind: 'icon', label: 'Ícone', hint: 'Quadrado' },
];

export function renderArtSlots() {
  return KINDS.map(({ kind, label, hint }) => `
    <div class="art-slot slot-${kind}" data-kind="${kind}">
      <button type="button" class="slot-preview" data-nav data-slot="pick" aria-label="${label}: escolher imagem">
        <img alt="" decoding="async" hidden>
        <span class="slot-empty">${icon('image')}<span>Sem imagem</span></span>
        <span class="slot-drop">${icon('upload')}<span>Solte para usar</span></span>
      </button>
      <div class="slot-foot">
        <span class="slot-label"><b>${label}</b><small>${hint}</small></span>
        <button type="button" class="icon-btn" data-slot="clear" aria-label="Restaurar ${label.toLowerCase()} padrão" title="Restaurar padrão">${icon('refresh')}</button>
      </div>
    </div>`).join('');
}

export function bindArtSlots(container, getId) {
  const busy = (slot, on) => slot.classList.toggle('busy', on);

  async function setArt(slot, files) {
    const id = getId();
    if (!id) return;
    const kind = slot.dataset.kind;
    const file = files ? Array.from(files)[0] : null;
    if (files && (!file || !/^image\//.test(file.type))) {
      toast('Solte um arquivo de imagem (PNG, JPG ou WEBP).', { kind: 'err' });
      return;
    }
    busy(slot, true);
    const data = await A.run('setArt', { id, kind }, file ? { files: [file] } : undefined);
    busy(slot, false);
    if (data && !data.cancelled) toast(data.message || 'Imagem atualizada.');
  }

  container.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-slot]');
    if (!b) return;
    const slot = b.closest('.art-slot');
    if (b.dataset.slot === 'pick') setArt(slot, null);
    else if (b.dataset.slot === 'clear') {
      const id = getId();
      busy(slot, true);
      const data = await A.run('clearArt', { id, kind: slot.dataset.kind });
      busy(slot, false);
      if (data) toast(data.message || 'Imagem restaurada.');
    }
  });
  for (const slot of container.querySelectorAll('.art-slot')) makeDropTarget(slot, (files) => setArt(slot, files));
}

export function updateArtSlots(container, g) {
  for (const slot of container.querySelectorAll('.art-slot')) {
    const url = g.art?.[slot.dataset.kind] || null;
    const img = slot.querySelector('img');
    if ((img.getAttribute('src') || null) === url) continue;
    slot.classList.toggle('has-img', !!url);
    if (url) {
      img.onload = () => { img.hidden = false; };
      img.onerror = () => { img.hidden = true; slot.classList.remove('has-img'); };
      img.src = url;
    } else {
      img.hidden = true;
      img.removeAttribute('src');
    }
  }
}
