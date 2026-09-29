// DOM-free model for the automation editor: action types, defaults, normalization and validation.
// AutomationProfile = { enabled, useDefault, before: AutomationAction[], after: AutomationAction[] }
// AutomationAction  = { type, target, args, seconds, enabled }  (see Shared/Contracts.Wave2.cs)

export const ACTION_TYPES = [
  { key: 'run', label: 'Abrir programa' },
  { key: 'close', label: 'Fechar programa' },
  { key: 'powerPlan', label: 'Plano de energia' },
  { key: 'audioDevice', label: 'Saída de áudio' },
  { key: 'resolution', label: 'Resolução' },
  { key: 'wait', label: 'Aguardar' },
];

/** Action type -> key of the automationOptions reply that lists its choices. */
export const OPTION_LISTS = { powerPlan: 'powerPlans', audioDevice: 'audioDevices', resolution: 'resolutions' };
/** Settings restored automatically when the game closes. */
export const RESTORED_TYPES = ['powerPlan', 'audioDevice', 'resolution'];
export const MAX_WAIT = 30;
export const MAX_ACTIONS = 12;

export function blankAction(type = 'run') {
  return { type, target: '', args: '', seconds: type === 'wait' ? 5 : 0, enabled: true };
}

function normAction(a) {
  const type = ACTION_TYPES.some((t) => t.key === a?.type) ? a.type : 'run';
  return {
    type,
    target: String(a?.target ?? ''),
    args: type === 'run' ? String(a?.args ?? '') : '',
    seconds: type === 'wait' ? Math.max(0, Math.min(MAX_WAIT, Math.round(Number(a?.seconds) || 0))) : 0,
    enabled: a?.enabled !== false,
  };
}

/** Defensive copy of a profile from the bridge (missing fields get contract defaults). */
export function normalizeProfile(p) {
  return {
    enabled: p?.enabled !== false,
    useDefault: p?.useDefault !== false,
    before: Array.isArray(p?.before) ? p.before.map(normAction) : [],
    after: Array.isArray(p?.after) ? p.after.map(normAction) : [],
  };
}

/** Payload for saveAutomation (trimmed strings). */
export function profilePayload(p) {
  const clean = (a) => ({ ...normAction(a), target: a.target.trim(), args: a.type === 'run' ? a.args.trim() : '' });
  return { enabled: !!p.enabled, useDefault: !!p.useDefault, before: p.before.map(clean), after: p.after.map(clean) };
}

/** '' when valid, otherwise a pt-BR message. `options` (automationOptions reply) is optional. */
export function validateAction(a, options = null) {
  switch (a.type) {
    case 'run': return a.target.trim() ? '' : 'Informe o programa ou atalho a abrir.';
    case 'close': return a.target.trim() ? '' : 'Informe o nome do processo (ex.: Discord.exe).';
    case 'wait': return a.seconds >= 1 && a.seconds <= MAX_WAIT ? '' : `Use de 1 a ${MAX_WAIT} segundos.`;
    case 'powerPlan': case 'audioDevice': case 'resolution': {
      if (!a.target) return 'Escolha uma opção.';
      const list = options?.[OPTION_LISTS[a.type]];
      if (a.enabled && Array.isArray(list) && list.length && !list.some((o) => o.id === a.target)) {
        return 'Esta opção não está disponível neste PC. Escolha outra ou desative a ação.';
      }
      return '';
    }
    default: return 'Tipo de ação desconhecido.';
  }
}

/** { before: string[], after: string[], count } — one message ('' = ok) per action. */
export function validateProfile(p, options = null) {
  const before = p.before.map((a) => validateAction(a, options));
  const after = p.after.map((a) => validateAction(a, options));
  const count = [...before, ...after].filter(Boolean).length;
  return { before, after, count };
}

/** Moves list[i] by delta (-1 up, +1 down); returns the new index. */
export function moveAction(list, i, delta) {
  const j = i + delta;
  if (j < 0 || j >= list.length) return i;
  [list[i], list[j]] = [list[j], list[i]];
  return j;
}

export const sameProfile = (a, b) => JSON.stringify(profilePayload(a)) === JSON.stringify(profilePayload(b));
