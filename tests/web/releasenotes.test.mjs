// Update banner / "Novidades" dialog: release notes (Markdown + HTML) become plain structured text.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { importWeb } from './_setup.mjs';

const { plainText, parseReleaseNotes, notesSummary } = await importWeb('js/components/releasenotes.js');

const MD = `<p align="center"><img src="https://x/biblioteca.png" alt="Biblioteca" width="820"></p>

**Versão 2.0.3** · lançada em 30 de setembro · [ver todas as mudanças](https://github.com/x)

## 📋 O que mudou

### ✨ Novidades

- **Configurações › Discos** mostra onde cada jogo está.
- **Encontrar jogos no PC** (Adicionar jogo): busca jogos.
- **Abrir launchers minimizados**: a Steam inicia na bandeja.
- **Quarto item** extra.

### 🐛 Correções

- O tamanho usa a **pasta raiz** (não \`Binaries\Win64\`).

## ⬇️ Download

| Arquivo | Tamanho |
|---|---|
- não é novidade
`;

test('HTML, images, links, bold and code never reach the text', () => {
  assert.equal(plainText('<p><img src="a.png"></p> **a** [b](https://x) `c` ![i](j.png)'), 'a b c');
});

test('sections come only from "O que mudou", icons stripped', () => {
  const s = parseReleaseNotes(MD);
  assert.deepEqual(s.map((x) => x.title), ['Novidades', 'Correções']);
  assert.equal(s[0].items.length, 4);
  assert.equal(s[0].items[0].lead, 'Configurações › Discos');
  assert.equal(s[1].items[0].text, 'O tamanho usa a pasta raiz (não Binaries\Win64).');
  assert.ok(!JSON.stringify(s).includes('<'), 'no markup left');
});

test('banner summary lists the highlights in one line', () => {
  assert.equal(notesSummary(MD), 'Configurações › Discos · Encontrar jogos no PC · Abrir launchers minimizados e mais 1');
});

test('notes without sections fall back to plain text', () => {
  assert.equal(notesSummary('<p>Correções de <b>estabilidade</b>.</p>'), 'Correções de estabilidade.');
  assert.equal(notesSummary(''), '');
});
