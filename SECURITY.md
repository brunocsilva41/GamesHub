# Política de segurança

## Versões com suporte

Correções de segurança são publicadas apenas para a versão mais recente.

| Versão | Suporte |
|---|---|
| 2.x (mais recente) | ✅ |
| < 2.0 | ❌ |

O GamesHub se atualiza sozinho pelas Releases deste repositório; mantenha a verificação de atualizações ligada
(Configurações → Atualizações).

## Como as atualizações são verificadas

O atualizador embutido só executa um instalador que consegue provar que veio do projeto:

1. **Origem fixa.** As versões são buscadas sempre no repositório oficial (`brunocsilva41/GamesHub`); isso não é
   configurável. O app aceita apenas `https` e só baixa de `github.com`, `objects.githubusercontent.com` e
   `release-assets.githubusercontent.com` — cada redirecionamento é conferido. As respostas têm tamanho máximo
   (JSON da API: 1 MB; lista de somas: 16 KB; assinatura: 1 KB; instalador: o tamanho anunciado pelo GitHub + 64 KB).
2. **Assinatura própria.** A cada publicação, o `release.yml` assina a lista de somas
   `GamesHub-Setup-X.Y.Z.exe.sha256` com uma chave **ECDSA P-256** (SHA-256) guardada apenas no segredo
   `GAMESHUB_UPDATE_SIGNING_KEY` do ambiente `release` e publica a assinatura em `GamesHub-Setup-X.Y.Z.exe.sha256.sig`.
   A parte pública da chave vem embutida no app (`src/GamesHub/Update/UpdateSigningKey.cs`).
3. **Verificação antes de executar.** O app baixa a lista de somas e a assinatura, confere a assinatura com a chave
   embutida, exige que a lista cite o instalador pelo nome exato da versão (`GamesHub-Setup-X.Y.Z.exe`) e compara o
   SHA-256 do arquivo baixado com o valor assinado.
4. **Sem janela para troca do arquivo.** O instalador é gravado numa subpasta aleatória nova de
   `%TEMP%\GamesHub` (downloads antigos são apagados) e fica aberto sem permitir escrita, renomeação ou exclusão
   entre a conferência do SHA-256 e o início da execução.
5. **Falha fechada.** Se faltar a lista de somas ou a assinatura, se a assinatura for inválida, se o nome ou o
   SHA-256 não conferirem, nada é executado: o app mostra o motivo e abre a página da versão para um download
   manual (que pode ser conferido como em [docs/PIPELINE.md](docs/PIPELINE.md#como-verificar-um-download)).

A chave SteamGridDB informada pelo usuário é guardada no `settings.json` protegida pela DPAPI do Windows (escopo do
usuário atual) e nunca é enviada de volta para a interface.

## Como relatar uma vulnerabilidade

**Não abra uma issue pública.** Relate de forma privada pelos
[GitHub Security Advisories](https://github.com/brunocsilva41/GamesHub/security/advisories/new)
(aba **Security → Report a vulnerability**).

Inclua, se possível:

- versão do GamesHub e do Windows;
- descrição do problema e do impacto;
- passos para reproduzir ou prova de conceito;
- trechos relevantes de `%LOCALAPPDATA%\GamesHub\logs\gameshub.log` (revise antes para remover dados pessoais).

O que esperar:

- confirmação de recebimento em até **7 dias**;
- avaliação e plano de correção assim que o problema for confirmado;
- publicação de uma versão corrigida e de um advisory, com crédito a quem relatou (se desejar).

## Escopo

Estão no escopo, por exemplo:

- execução de código, abertura de arquivos, pastas ou URLs arbitrários a partir da interface (WebView2) ou de
  dados importados (atalhos, manifestos da Steam/Epic, dados da Riot/Hydra, respostas de APIs);
- contorno da filtragem de navegação, da verificação de origem da ponte ou da lista de caminhos de `openPath`;
- falhas no instalador, desinstalador ou atualizador (por exemplo, executar um instalador sem assinatura válida
  ou com SHA-256 diferente do assinado, apagar arquivos fora da pasta do app);
- vazamento de dados locais ou da chave do SteamGridDB para terceiros;
- problemas na pipeline de publicação que permitam distribuir um binário adulterado.

Fora do escopo:

- vulnerabilidades no Windows, no Microsoft Edge WebView2 Runtime, na Steam, Epic, Riot, Hydra ou em serviços
  externos (relate aos respectivos fornecedores);
- ataques que exigem que o invasor já controle a conta do usuário ou tenha acesso de administrador;
- o aviso do Windows SmartScreen para instaladores sem assinatura de código (veja
  [docs/PIPELINE.md](docs/PIPELINE.md#como-verificar-um-download));
- jogos, atalhos ou programas que o próprio usuário adiciona e manda executar (inclusive nas ações antes/depois de
  jogar).

Detalhes do modelo de segurança em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#modelo-de-segurança).
