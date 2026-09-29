# Política de segurança

## Versões com suporte

Correções de segurança são publicadas apenas para a versão mais recente.

| Versão | Suporte |
|---|---|
| 2.x (mais recente) | ✅ |
| < 2.0 | ❌ |

O GamesHub se atualiza sozinho pelas Releases deste repositório; mantenha a verificação de atualizações ligada
(Configurações → Atualizações).

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
- falhas no instalador, desinstalador ou atualizador (por exemplo, instalar um arquivo sem verificar o SHA-256,
  apagar arquivos fora da pasta do app);
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
