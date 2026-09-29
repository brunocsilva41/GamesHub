# Publicando uma versão

Lista de verificação do mantenedor. Detalhes de cada etapa em [PIPELINE.md](PIPELINE.md).

## Antes

- [ ] Estou na branch `main`, atualizada, com a árvore de trabalho limpa (`git status`).
- [ ] O último CI de `main` passou.
- [ ] A seção `## [Não lançado]` do `CHANGELOG.md` descreve tudo o que muda, em pt-BR, dividida em
      Adicionado / Alterado / Corrigido / Removido / Segurança.
- [ ] Escolhi o número de versão seguindo [SemVer](https://semver.org/lang/pt-BR/): correção → `X.Y.Z+1`,
      recurso compatível → `X.Y+1.0`, mudança incompatível → `X+1.0.0`.
- [ ] Documentação atualizada (`README.md`, `docs/USER_GUIDE.md`, `docs/ARCHITECTURE.md`) se algo visível mudou.

## Publicar

```powershell
./tools/release.ps1 -Version X.Y.Z
```

- [ ] O script atualizou `AssemblyInfo.cs` e o `CHANGELOG.md` (seção `## [X.Y.Z] - AAAA-MM-DD` e links).
- [ ] A pipeline local passou.
- [ ] Revisei o commit e a tag `vX.Y.Z` e confirmei o envio.

## Depois

- [ ] O workflow **release** terminou com sucesso na aba Actions.
- [ ] A Release `vX.Y.Z` tem `GamesHub-Setup-X.Y.Z.exe`, `GamesHub-Setup-X.Y.Z.exe.sha256` e as notas corretas.
- [ ] `gh attestation verify GamesHub-Setup-X.Y.Z.exe --repo brunocsilva41/GamesHub` confirma a proveniência.
- [ ] Instalei (ou atualizei pelo próprio app, em Configurações → Atualizações → Verificar agora) e abri o app.

## Se algo der errado

- **A pipeline local falhou:** corrija, faça commit e rode o script de novo (nada foi enviado).
- **O workflow de release falhou antes de publicar:** apague a tag (`git push --delete origin vX.Y.Z` e
  `git tag -d vX.Y.Z`), corrija em `main` e publique novamente.
- **Uma versão publicada tem um problema grave:** não reutilize o número. Marque a Release como pré-lançamento ou
  remova o instalador e publique `X.Y.Z+1` com a correção.
