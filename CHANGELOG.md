# Changelog

Todas as mudanças relevantes do GamesHub. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento [SemVer](https://semver.org/lang/pt-BR/).

## [Não lançado]

## [2.0.0] - 2026-09-29

Reescrita completa do GamesHub (antigo "GamesLounge"): novo app, nova interface e novo instalador.

### Adicionado
- Importação automática de jogos da **Steam** (todas as bibliotecas), **Epic Games**, **Riot** e **Hydra**, junto
  com os atalhos da pasta de jogos.
- **Artes em HD** (capa, banner, hero e logo) baixadas automaticamente da Steam, com SteamGridDB opcional, busca
  aproximada para jogos fora da Steam e artes personalizadas por arrastar e soltar.
- **Página de detalhes** do jogo com informações (gêneros, descrição, desenvolvedora, lançamento, Metacritic),
  filtro por gênero, link do PCGamingWiki, abrir pastas de saves/configurações, tamanho em disco e espaço livre.
- **Favoritos, coleções** e jogos ocultos; ordenação por nome, recentes, tempo de jogo e data de adição.
- **Tempo de jogo** e "jogado por último", medidos pelo app e importados da Steam.
- **Variações de execução** agrupadas num único card ("Jogar ▾").
- **Ações antes/depois de jogar** por jogo (abrir programa, fechar processo, plano de energia, dispositivo de
  áudio, resolução, esperar), restauradas automaticamente quando o jogo fecha.
- Aviso de **atualização pendente**, **Verificar arquivos** (Steam) e **desinstalar pela plataforma**.
- Detecção de **atalhos quebrados** e limpeza da biblioteca.
- Navegação completa por **teclado e controle**, modo **Big Picture** e **paleta de início rápido**
  (Ctrl+Shift+Espaço, personalizável).
- Tela de **Configurações** (pasta de jogos, fontes, comportamento ao jogar, bandeja, atalhos, aparência, artes,
  tempo de jogo, atualizações).
- **Bandeja** com jogos recentes, **Jump List** da barra de tarefas e **atalho global** (Ctrl+Alt+G).
- **Instalador de arquivo único** (`GamesHub-Setup-<versão>.exe`), por usuário e sem administrador, com modo
  silencioso (`/silent`), atualização da 1.x no mesmo lugar e desinstalador que pergunta se deve apagar seus dados.
- **Atualização automática** opcional via GitHub Releases, com verificação SHA-256 do instalador.
- Suporte a telas de alta densidade (DPI por monitor) e visual escuro consistente em todo o app.

### Alterado
- Executável renomeado de `GamesLounge.exe` para `GamesHub.exe` (atalhos existentes são atualizados pelo instalador).
- Dados movidos da pasta `_hub` dentro da pasta de jogos para `%LOCALAPPDATA%\GamesHub`; histórico e capas da
  1.x são importados automaticamente na primeira execução (a pasta antiga não é alterada).
- Interface refeita do zero em HTML/CSS/JS puro, sem servidor local nem portas abertas.
- Remover um jogo move o atalho para uma lixeira interna, com opção de desfazer.

### Corrigido
- Várias instâncias do app podiam ficar abertas ao mesmo tempo; agora há uma única instância, que é trazida para a frente.
- A interface dependia de um servidor HTTP local (`HttpListener`); agora a comunicação é direta com o WebView2,
  sem portas abertas.
- Pasta de jogos padrão fixa no código para um usuário específico; agora o padrão é `Área de Trabalho\jogos`
  do usuário atual (ou a pasta escolhida no instalador).
- A janela não lembrava tamanho, posição e estado maximizado.
- Erros ignorados silenciosamente; agora são registrados em `%LOCALAPPDATA%\GamesHub\logs\gameshub.log`.
- O instalador gravava o resultado em `%TEMP%\opencode`; agora usa `%TEMP%\GamesHub\setup-result.txt`.
- O desinstalador só removia atalhos da Área de Trabalho/Menu Iniciar; agora remove também os fixados na barra de
  tarefas, os da pasta Inicializar e a entrada de início com o Windows.

## [1.0.0]

### Adicionado
- Versão original ("GamesLounge"): grade de jogos a partir dos atalhos da pasta de jogos, capas da Steam,
  registro simples de jogadas e ícone na bandeja.

[Não lançado]: https://github.com/OWNER/GamesHub/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/OWNER/GamesHub/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/OWNER/GamesHub/releases/tag/v1.0.0
