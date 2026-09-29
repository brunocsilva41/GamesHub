// Window-wide file drop: shows an overlay while dragging files from Explorer and adds them on drop.
// Also prevents WebView2 from navigating to a dropped file.
import { store } from '../store.js';
import * as A from '../actions.js';
import { modalOpen } from './modal.js';

const hasFiles = (e) => Array.from(e.dataTransfer?.types || []).includes('Files');

/** Local drop targets (e.g. art slots, add-game drop zone) mark themselves with [data-dropzone]. */
const localTarget = (e) => e.target.closest?.('[data-dropzone]');

export function initDropOverlay(overlay) {
  let depth = 0;
  const show = (on) => { overlay.hidden = !on; document.body.classList.toggle('dragging-files', on); };
  const enabled = () => !modalOpen() && !store.state.bigPicture && store.state.route.view !== 'game';

  window.addEventListener('dragenter', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth++;
    if (enabled()) show(true);
  });
  window.addEventListener('dragleave', (e) => {
    if (!hasFiles(e)) return;
    depth = Math.max(0, depth - 1);
    if (depth === 0) show(false);
  });
  window.addEventListener('dragover', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault(); // required for drop; also blocks navigation to the file
    if (!localTarget(e)) e.dataTransfer.dropEffect = enabled() ? 'copy' : 'none';
  });
  window.addEventListener('drop', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth = 0;
    show(false);
    if (localTarget(e) || !enabled()) return;
    A.addFiles(e.dataTransfer.files);
  });
}

/** Wires an element as a local drop target. onFiles(FileList). */
export function makeDropTarget(el, onFiles) {
  el.dataset.dropzone = '';
  let depth = 0;
  el.addEventListener('dragenter', (e) => { if (hasFiles(e)) { depth++; el.classList.add('drag-over'); } });
  el.addEventListener('dragleave', () => { depth = Math.max(0, depth - 1); if (!depth) el.classList.remove('drag-over'); });
  el.addEventListener('dragover', (e) => { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
  el.addEventListener('drop', (e) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth = 0;
    el.classList.remove('drag-over');
    onFiles(e.dataTransfer.files);
  });
}
