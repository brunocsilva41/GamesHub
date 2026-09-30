# Guia do usuário — GamesHub

## Primeiros passos

Na primeira vez que você abre o GamesHub, ele:

1. lê os atalhos (`.lnk`, `.url`, `.exe`) da sua **pasta de jogos** (por padrão `Área de Trabalho\jogos`, ou a
   pasta escolhida no instalador);
2. importa os jogos instalados da **Steam**, da **Epic Games**, da **Riot Games** e do **Hydra Launcher**;
3. baixa as artes de cada jogo em segundo plano (as capas vão aparecendo aos poucos).

Se você usava a versão 1.x, o histórico de jogadas e as capas antigas são importados automaticamente.

## Adicionando jogos

- **Arrastar e soltar**: arraste um atalho, um `.exe` ou um `.url` para a janela do GamesHub. Um atalho é criado na
  sua pasta de jogos.
- **Adicionar jogo** (botão na barra lateral ou **Ctrl+N**): escolha um executável ou atalho, ou busque o jogo na
  loja da Steam pelo nome ou pelo App ID — útil para jogos que você abre pela Steam.
- **Importações automáticas**: Steam e Epic são ligadas em **Configurações → Biblioteca**; Riot e Hydra em
  **Configurações → Fontes e dados**. Jogos novos aparecem assim que são instalados; use **F5** para reescanear na
  hora.

### Variações de execução

Quando um jogo tem mais de uma forma de abrir (por exemplo, com e sem mods, ou DirectX 11/12), elas podem ficar
agrupadas num único card. O GamesHub sugere grupos quando encontra jogos que parecem ser o mesmo: clique em
**Revisar** no aviso da biblioteca, escolha a versão principal e clique em **Agrupar** (ou **Não são o mesmo
jogo**). Depois, use a seta de **Jogar ▾** (ou **Jogar como** no menu do card) para escolher qual versão abrir. Na
página de detalhes você renomeia cada versão, troca a principal ou desfaz o grupo.

### Atalhos quebrados e limpeza

Atalhos que apontam para arquivos que não existem mais (ou jogos que o Hydra não tem mais instalados) são marcados
na biblioteca, e um aviso mostra quantos são. Clique em **Revisar** e depois em **Limpar biblioteca** para
removê-los de uma vez. Os atalhos vão para a lixeira do GamesHub, com opção de desfazer.

### Removendo jogos

**Delete** (ou o menu do card → **Remover…**) tira o jogo da biblioteca, e o aviso que aparece tem **Desfazer**.

- Atalhos da pasta de jogos vão para a lixeira do GamesHub (`%LOCALAPPDATA%\GamesHub\trash`); nada é apagado de
  verdade.
- Jogos importados (Steam, Epic…) apenas deixam de aparecer; o jogo continua instalado.
- Para esconder um jogo sem removê-lo, use **Ocultar**. Jogos ocultos ficam na seção **Ocultos** da barra lateral.
- Para desinstalar de verdade, use **Desinstalar…** na página de detalhes: o GamesHub abre a desinstalação da
  Steam, a biblioteca da Epic ou o desinstalador do Windows, conforme o jogo.

## A biblioteca

- **Clique** num card (ou **Enter**) para jogar. Se o jogo já estiver aberto, abre os detalhes.
- A barra lateral tem as seções **Todos**, **Favoritos**, **Jogados recentemente** e **Ocultos**, além das suas
  **Coleções**.
- A barra de ferramentas tem busca, filtro por gênero, ordenação (**Nome**, **Jogados recentemente**, **Mais
  jogados**, **Adicionados recentemente**, **Maior tamanho**), grade ou lista e estilo dos cards. Os filtros
  rápidos mostram **Nunca jogados**, **Sem jogar há 3+ meses** ou **Instalados**.
- Clique com o botão direito num card (ou **Shift+F10**) para o menu do jogo.

## Página de detalhes

Abra com o botão de informações do card, **I**, **Shift+Enter** ou o botão **X** do controle. Lá você encontra:

- informações do jogo (da loja da Steam): gêneros, descrição, desenvolvedora, data de lançamento e nota do
  Metacritic (para ver todos os jogos de um gênero, use o filtro de gêneros da biblioteca);
- tempo jogado, última vez que jogou, data em que foi adicionado, tamanho em disco;
- aviso de **atualização pendente** e **Verificar arquivos** (jogos da Steam);
- **Correções e dicas (PCGamingWiki)**, que abre a página do jogo no navegador;
- **Saves e configurações**: clique em **Localizar** para o GamesHub consultar o PCGamingWiki e mostrar onde ficam
  os saves e as configurações neste PC, com botão para abrir cada pasta;
- **Coleções**, **Antes e depois de jogar** (veja abaixo), **Editar** (nome, argumentos de inicialização, Steam
  App ID) e **Imagens**;
- ações: **Abrir local**, **Ocultar**, **Desinstalar…** e **Remover…**.

## Favoritos e coleções

- **F** (ou **Y** no controle) marca/desmarca o jogo como favorito. Favoritos têm sua própria seção.
- Na página de detalhes (ou em **Adicionar à coleção** no menu do card), coloque o jogo em uma ou mais
  **coleções** (por exemplo "Coop", "Zerados", "Com a família"). As coleções aparecem na barra lateral.

## Artes

As artes vêm da Steam automaticamente: cabeçalho (card paisagem), capa (card retrato), fundo, logotipo e ícone.
Para jogos fora da Steam, o GamesHub procura o jogo pelo nome. Se ele errar:

- informe o **Steam App ID** correto em **Editar** (há uma busca "Encontrar na Steam" ali mesmo); ou
- use **0** como Steam App ID para dizer que o jogo **não está na Steam** — ele deixa de ser confundido com jogos da
  Steam e passa a usar só as artes do SteamGridDB ou o ícone.

O GamesHub também busca artes no **SteamGridDB** automaticamente — ótimo para jogos da Riot, Roblox e outros que
não estão na Steam. Não precisa configurar nada; se preferir usar a sua própria chave, informe-a em
**Configurações → Artes → Chave da SteamGridDB**.

Para personalizar: em **Imagens**, na página de detalhes, **arraste uma imagem** sobre o espaço que quer trocar ou
clique nele e escolha um arquivo. O botão de restaurar de cada espaço volta para a arte automática e **Buscar artes
novamente** baixa tudo de novo.

## Tempo de jogo

O GamesHub mede o tempo enquanto o processo do jogo está aberto, mesmo quando o jogo é aberto por um launcher.
O tempo registrado pela Steam neste PC (e a data em que jogou por último) também é importado; o GamesHub usa o
maior dos dois valores, sem contar a mesma sessão duas vezes. Desligue a medição em **Configurações → Tempo de
jogo → Rastrear tempo de jogo** e a importação em **Configurações → Fontes e dados → Importar tempo de jogo da
Steam**.

## Antes e depois de jogar

Ações automáticas ao abrir um jogo: **Abrir programa**, **Fechar programa**, **Plano de energia**, **Saída de
áudio**, **Resolução** ou **Aguardar** alguns segundos. Quando o jogo fecha, o que foi alterado volta ao normal
sozinho (mesmo que o GamesHub tenha sido fechado de forma inesperada: a restauração acontece na próxima abertura).

- Por jogo: página de detalhes → **Antes e depois de jogar**.
- Para todos os jogos: **Configurações → Automação padrão**. Cada jogo pode desligar o perfil padrão.
- Para desligar tudo: **Configurações → Automação padrão → Automação antes e depois de jogar**.

## Busca rápida

Pressione **Ctrl+Shift+Espaço** em qualquer lugar do Windows (mesmo com o GamesHub na bandeja) para abrir uma
paleta flutuante. Digite parte do nome e:

- **Enter** abre o jogo;
- **Ctrl+Enter** abre a pasta do jogo;
- **Esc** fecha.

Sem digitar nada, a paleta mostra os jogos recentes. Troque a combinação ou desligue em **Configurações → Atalhos
de teclado**.

## Configurações

Abra com **Ctrl+,** ou pela barra lateral. As mudanças são salvas na hora.

| Seção | O que dá para ajustar |
|---|---|
| Biblioteca | pasta de jogos, importar jogos da Steam e da Epic Games, reescanear |
| Comportamento | ao iniciar um jogo (esconder na bandeja, minimizar ou não fazer nada), fechar para a bandeja, iniciar com o Windows, iniciar minimizado |
| Atalhos de teclado | atalho para abrir o GamesHub (padrão Ctrl+Alt+G) e busca rápida (padrão Ctrl+Shift+Espaço): ligar/desligar e trocar a combinação |
| Fontes e dados | importar jogos da Riot Games e do Hydra Launcher, importar tempo de jogo da Steam, buscar informações dos jogos, consultar o PCGamingWiki |
| Aparência | estilo dos cards (paisagem ou retrato), exibição (grade ou lista), reduzir animações, idioma |
| Artes | buscar artes automaticamente, chave da SteamGridDB |
| Tempo de jogo | rastrear tempo de jogo |
| Automação padrão | ligar/desligar a automação e editar o perfil padrão |
| Discos | espaço livre de cada unidade |
| Atualizações | verificar atualizações ao iniciar, repositório, verificar agora |
| Sobre | versão, abrir a pasta de dados, abrir os logs |

A ordenação da biblioteca fica na barra de ferramentas da própria biblioteca.

## Atalhos de teclado

| Tecla | Ação |
|---|---|
| **Setas** | Navegar |
| **Enter** | Jogar (ou ativar o botão em foco) |
| **I** ou **Shift+Enter** | Detalhes |
| **F** | Favoritar / desfavoritar |
| **Delete** | Remover da biblioteca |
| **Shift+F10** ou tecla de menu | Menu do jogo |
| **Ctrl+F** ou **/** | Buscar |
| **Ctrl+N** | Adicionar jogo |
| **Ctrl+,** | Configurações |
| **F11** | Big Picture |
| **Esc** | Voltar / fechar |
| **F5** | Reescanear a biblioteca |
| **Ctrl+Alt+G** (global) | Mostrar o GamesHub de qualquer lugar (ou escondê-lo, se já estiver em primeiro plano) |
| **Ctrl+Shift+Espaço** (global) | Busca rápida |

Os atalhos globais podem ser trocados ou desligados em **Configurações → Atalhos de teclado**.

## Controle (Xbox e compatíveis)

| Botão | Ação |
|---|---|
| **D-pad / analógico esquerdo** | Navegar |
| **A** | Jogar (ou ativar o botão em foco) |
| **X** | Detalhes |
| **Y** | Favoritar |
| **B** | Voltar |
| **LB / RB** | Seção anterior / próxima (no Big Picture, prateleira anterior / próxima) |
| **Start** | Big Picture |
| **View** | Buscar |

O controle só age quando a janela do GamesHub está em foco, então não interfere no jogo aberto. O modo **Big
Picture** (**F11** ou **Start**) ocupa a tela inteira com cards maiores, ideal para TV e sofá.

## Bandeja, Jump List e início com o Windows

- Fechar a janela deixa o GamesHub na **bandeja** (perto do relógio), se **Fechar para a bandeja** estiver ligado.
  Clique no ícone para reabrir; o menu mostra os **jogos recentes** para abrir direto.
- Clique com o botão direito no GamesHub na **barra de tarefas** para ver os jogos recentes (Jump List).
- **Iniciar com o Windows** abre o GamesHub ao entrar no Windows; com **Iniciar minimizado**, ele fica só na
  bandeja.

## Atualizações

O GamesHub verifica ao iniciar se há uma versão nova nas
[Releases do GitHub](https://github.com/brunocsilva41/GamesHub/releases) e mostra um aviso. Ao aceitar, ele baixa o
instalador, confere a soma SHA-256, fecha, atualiza e abre de novo sozinho — seus dados não são tocados. Também dá
para verificar manualmente em **Configurações → Atualizações → Verificar agora**, ou desligar a verificação ao
iniciar.

## Perguntas frequentes e solução de problemas

**O GamesHub não abre / aparece um aviso sobre o WebView2.**
Instale o [Microsoft Edge WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703) (gratuito). Ele já
vem no Windows 11; em alguns Windows 10 pode faltar.

**O Windows SmartScreen bloqueou o instalador.**
O instalador não tem assinatura de código paga. Confira o download (veja
[PIPELINE.md](PIPELINE.md#como-verificar-um-download)) e clique em **Mais informações → Executar assim mesmo**.

**Onde ficam os logs?**
`%LOCALAPPDATA%\GamesHub\logs\gameshub.log` (o app) e `%TEMP%\GamesHub\setup.log` / `uninstall.log` (instalador).
Em **Configurações → Sobre** há botões para abrir a pasta de dados e os logs. Anexe um trecho do log ao relatar um
problema.

**Uma capa está errada.**
Em **Editar**, corrija o **Steam App ID** (ou use **0** se o jogo não estiver na Steam), ou arraste uma imagem sua
em **Imagens**. **Buscar artes novamente** baixa de novo.

**Quero limpar o cache de artes.**
Feche o GamesHub (inclusive na bandeja) e apague a pasta `%LOCALAPPDATA%\GamesHub\cache\art`. As artes são baixadas
de novo na próxima abertura; artes personalizadas também são apagadas.

**Um jogo não aparece.**
Confira se a fonte está ligada em Configurações e pressione **F5**. Para jogos de outras lojas, adicione o atalho ou
o executável arrastando para a janela.

**O tempo de jogo não conta.**
Verifique se **Rastrear tempo de jogo** está ligado. Jogos com anti-cheat que roda como administrador podem não ser
detectados; o tempo da Steam continua sendo importado.

**O atalho global não funciona.**
Outro programa pode estar usando a mesma combinação; o GamesHub avisa quando não consegue registrá-la. Escolha
outra em **Configurações → Atalhos de teclado**.

**Como volto às configurações padrão?**
Feche o GamesHub e apague `%LOCALAPPDATA%\GamesHub\settings.json`. Sua biblioteca (`library.json`) é mantida.

**Como desinstalo?**
**Configurações do Windows → Aplicativos → GamesHub → Desinstalar**. Marque **Apagar também meus dados do
GamesHub** se quiser remover configurações, biblioteca e capas. Sua pasta de jogos nunca é alterada.
