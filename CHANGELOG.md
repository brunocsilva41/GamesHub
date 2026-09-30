# Changelog

Todas as mudanças relevantes do GamesHub. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento [SemVer](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado

- **Configurações › Discos** mostra onde cada jogo está instalado: cada unidade abre uma lista dos seus jogos, do
  maior para o menor, com o tamanho em GB e a porcentagem do disco, e a barra da unidade separa jogos, outros
  arquivos e espaço livre. Jogos que compartilham a mesma pasta (como um jogo e seu launcher de mods) são contados
  uma vez só; clicar num jogo abre os detalhes.

## [2.0.2] - 2026-09-30

Suporte completo a controles genéricos, artes do SteamGridDB sem nenhuma configuração e uma rodada ampla de
segurança e qualidade de código guiada pela análise estática (CodeQL).

### Adicionado

- Artes do **SteamGridDB** funcionam sem configuração: o GamesHub já inclui uma chave (a sua, se informada nas
  Configurações, tem prioridade).

### Segurança

- Caminhos vindos de fora (manifestos da Steam, Epic, Riot e Hydra, PCGamingWiki, cache de artes, lixeira) não
  conseguem mais escapar da pasta esperada: caminhos absolutos, `..` e fluxos alternativos são recusados.
- Tratamento de erros mais preciso em todo o app: cada operação captura só os erros que pode realmente produzir;
  proteções amplas ficaram restritas às bordas do app (tarefas em segundo plano, eventos e leitura de dados de
  terceiros), todas documentadas.
- Correções de todos os alertas do CodeQL (sanitização, perda de precisão, possíveis nulos, recursos não liberados)
  e novas proteções no repositório: revisão obrigatória, portão de segurança para pull requests e Actions fixadas por
  SHA.

### Corrigido

- Controles genéricos no modo Android/D-input (por exemplo ShanWan, que aparecem como "Android Gamepad") tinham os
  botões embaralhados: X favoritava, RT abria a tela cheia e Start não funcionava. Agora esses controles são
  traduzidos para o layout padrão, incluindo o direcional digital.

## [2.0.1] - 2026-09-30

Controle mais confiável em janela e em tela cheia, navegação organizada por áreas e uma nova rolagem para o
carrossel "Continuar jogando".

### Corrigido

- Controle com botões trocados (X agindo como Y, B como LT…) quando o Windows expõe o mesmo controle duas vezes
  (XInput e dispositivo genérico, comum com Steam Input e drivers): só controles com mapeamento padrão são usados.
- Com o Steam aberto, cada botão do controle contava duas vezes (o Steam também o converte em teclas).
- No modo janela, o controle parava de responder depois de Alt+Tab ou de redimensionar a janela.
- Navegação pelo controle e pelas setas reorganizada por áreas (barra lateral, destaque, "Continuar jogando",
  filtros e grade), lembrando o último item de cada área e sem pular para cards escondidos do carrossel.
- A barra de busca ficava com contorno quadrado ao receber foco; agora é arredondada.

### Adicionado

- Barra de rolagem personalizada no carrossel "Continuar jogando", com botões ◀ ▶, arrasto, bordas esmaecidas e
  rolagem pela roda do mouse.

## [2.0.0] - 2026-09-29

Primeira versão pública. Reescrita completa do GamesHub (antigo "GamesLounge"): novo app, nova interface e novo
instalador.

### Adicionado

- Importação automática dos jogos instalados na **Steam** (todas as bibliotecas), **Epic Games**, **Riot Games**
  e **Hydra Launcher**, junto com os atalhos (`.lnk`, `.url`, `.exe`) da pasta de jogos; novos jogos aparecem
  sozinhos e **F5** reescaneia na hora.
- Adicionar jogos arrastando arquivos para a janela, pelo seletor de arquivos (**Ctrl+N**) ou buscando na loja da
  Steam.
- **Artes em HD** (capa, cabeçalho, fundo, logotipo e ícone) baixadas automaticamente da Steam, com busca
  aproximada para jogos fora da Steam, **SteamGridDB** opcional (com a chave gratuita do usuário) e artes
  personalizadas por arrastar e soltar. Jogos que só têm ícone ganham um fundo desfocado a partir dele.
- **Steam App ID `0`** marca um jogo como "não está na Steam": ele deixa de ser associado a jogos da Steam por
  engano e passa a buscar artes só no SteamGridDB.
- **Página de detalhes** com gêneros, descrição, desenvolvedora, data de lançamento e nota do Metacritic (loja da
  Steam), tempo de jogo, tamanho em disco, aviso de **atualização pendente** (Steam), **Verificar arquivos**
  (Steam) e **Desinstalar** pela Steam, pela Epic ou pelo desinstalador do Windows.
- **Saves e configurações** localizados pelo **PCGamingWiki** (com o manifesto do Ludusavi como alternativa), com
  botões para abrir as pastas, e link "Correções e dicas" para a página do jogo.
- **Variações de execução** agrupadas num único card (**Jogar ▾**), com sugestões automáticas de agrupamento.
- **Ações antes e depois de jogar**, por jogo e num perfil padrão: abrir ou fechar programa, plano de energia,
  saída de áudio, resolução e aguardar; tudo é restaurado quando o jogo fecha, inclusive depois de um
  encerramento inesperado do app.
- **Tempo de jogo** e "jogado por último" medidos pelo app e **importados da Steam** (usa o maior valor, sem
  contar sessões em dobro).
- **Favoritos, coleções** e jogos ocultos; filtro por gênero e filtros rápidos ("Nunca jogados", "Sem jogar há
  3+ meses", "Instalados"); ordenação por nome, jogados recentemente, mais jogados, adicionados recentemente e
  maior tamanho; prateleira "Continuar jogando".
- Detecção de **atalhos quebrados** (inclusive jogos do Hydra desinstalados) e **limpeza da biblioteca** em um
  clique, com desfazer.
- Navegação completa por **teclado e controle**, modo **Big Picture** e **busca rápida**: paleta flutuante aberta
  de qualquer lugar com **Ctrl+Shift+Espaço** (personalizável).
- **Bandeja** com jogos recentes, **Jump List** da barra de tarefas, **atalho global** (Ctrl+Alt+G,
  personalizável) e início com o Windows.
- Tela de **Configurações** (Biblioteca, Comportamento, Atalhos de teclado, Fontes e dados, Aparência, Artes,
  Tempo de jogo, Automação padrão, Discos, Atualizações e Sobre) com espaço livre de cada unidade.
- **Instalador de arquivo único** (`GamesHub-Setup-<versão>.exe`), por usuário e sem administrador, com modo
  silencioso (`/silent`), atualização da 1.x no mesmo lugar e desinstalador que pergunta se deve apagar seus dados.
- **Atualização automática** pelas GitHub Releases deste repositório, com verificação SHA-256 do instalador.
- Suporte a telas de alta densidade (DPI por monitor) e visual escuro consistente em todo o app.

### Alterado

- Executável renomeado de `GamesLounge.exe` para `GamesHub.exe` (atalhos existentes são atualizados pelo
  instalador).
- Dados movidos da pasta `_hub` dentro da pasta de jogos para `%LOCALAPPDATA%\GamesHub`; histórico e capas da 1.x
  são importados automaticamente na primeira execução (a pasta antiga não é alterada).
- Interface refeita do zero em HTML/CSS/JS puro sobre o WebView2, sem servidor local nem portas abertas.
- Remover um jogo move o atalho para uma lixeira interna, com opção de desfazer.

### Corrigido

- Jogos fora da Steam (Riot, Roblox e outros) recebiam artes de jogos da Steam com nome parecido; agora usam o
  SteamGridDB, que também tenta o nome sem sufixos como "Player" ou "Client".
- Quando o SteamGridDB tem vários jogos com exatamente o mesmo nome, é escolhido o lançamento mais recente (em
  geral, a versão de PC que o launcher instalou).
- O controle agia na biblioteca mesmo com outro programa (o jogo) em foco; agora só responde com a janela do
  GamesHub em foco.
- Várias instâncias do app podiam ficar abertas ao mesmo tempo; agora há uma única instância, trazida para a
  frente.
- A pasta de jogos padrão era fixa no código; agora o padrão é `Área de Trabalho\jogos` do usuário atual (ou a
  pasta escolhida no instalador).
- A janela não lembrava tamanho, posição e estado maximizado.
- Erros eram ignorados silenciosamente; agora são registrados em `%LOCALAPPDATA%\GamesHub\logs\gameshub.log`.
- O resultado do instalador agora fica em `%TEMP%\GamesHub\setup-result.txt`, junto com o log.
- O desinstalador só removia atalhos da Área de Trabalho e do Menu Iniciar; agora remove também os fixados na
  barra de tarefas, os da pasta Inicializar e a entrada de início com o Windows.
- Com dois jogos abertos quase ao mesmo tempo usando "antes e depois de jogar", a resolução, o áudio ou o plano
  de energia podiam voltar para o valor do outro jogo, e não para o original.
- Uma letra de unidade sozinha (por exemplo `D:`) era tratada como pasta comum ao calcular o tamanho em disco,
  o que podia medir a unidade inteira.
- Atalhos do Hydra de jogos que não estão mais instalados aparecem como "Atalho quebrado".

### Segurança

- A interface não depende mais de um servidor HTTP local (`HttpListener`): a comunicação é direta com o WebView2,
  sem portas abertas, com navegação restrita ao app e abertura de links externos apenas via `https:`.
- Política de segurança de conteúdo (CSP) estrita na janela principal e na busca rápida: só scripts do próprio app.
- Cada versão é publicada pela pipeline pública, com SHA-256 e atestação de procedência verificáveis.

## [1.0.0]

Versão original ("GamesLounge"), anterior a este repositório.

### Adicionado

- Grade de jogos a partir dos atalhos da pasta de jogos, capas da Steam, registro simples de jogadas e ícone na
  bandeja.

[Não lançado]: https://github.com/brunocsilva41/GamesHub/compare/v2.0.2...HEAD
[2.0.2]: https://github.com/brunocsilva41/GamesHub/compare/v2.0.1...v2.0.2
[2.0.1]: https://github.com/brunocsilva41/GamesHub/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/brunocsilva41/GamesHub/releases/tag/v2.0.0
