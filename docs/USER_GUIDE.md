# Guia do usuário — GamesHub

## Primeiros passos

Na primeira vez que você abre o GamesHub, ele:

1. lê os atalhos (`.lnk`, `.url`, `.exe`) da sua **pasta de jogos** (por padrão `Área de Trabalho\jogos`);
2. importa os jogos instalados da **Steam**, da **Epic Games**, da **Riot** e do **Hydra**;
3. baixa as artes de cada jogo em segundo plano (as capas vão aparecendo aos poucos).

Se você usava a versão 1.x, o histórico de jogadas e as capas antigas são importados automaticamente.

## Adicionando jogos

- **Arrastar e soltar**: arraste um atalho, um `.exe` ou um `.url` para a janela do GamesHub. Um atalho é criado na
  sua pasta de jogos.
- **Arquivo**: botão **Adicionar** (ou **Ctrl+N**) → escolha o executável ou atalho.
- **Steam**: em **Adicionar**, busque pelo nome do jogo na loja da Steam e escolha o resultado — útil para jogos que
  você ainda não instalou ou que abre pela Steam.
- **Importações automáticas**: Steam, Epic, Riot e Hydra são lidos sozinhos. Ligue ou desligue cada fonte em
  **Configurações → Biblioteca**. Jogos novos aparecem assim que são instalados; use **F5** para reescanear na hora.

### Variações de execução

Quando um jogo tem mais de uma forma de abrir (por exemplo, com e sem mods, ou DirectX 11/12), elas ficam agrupadas
num único card. Use **Jogar ▾** para escolher qual abrir.

### Atalhos quebrados e limpeza

Atalhos que apontam para arquivos que não existem mais são marcados na biblioteca. Use **Limpar biblioteca** para
revisá-los e removê-los de uma vez.

### Removendo jogos

**Delete** (ou o menu do card → **Remover**) tira o jogo da biblioteca. Atalhos da pasta de jogos vão para a lixeira
do GamesHub (`%LOCALAPPDATA%\GamesHub\trash`) — nada é apagado de verdade — e você pode **Desfazer** pelo aviso que
aparece. Jogos importados (Steam, Epic…) ficam apenas ocultos; para desinstalá-los de verdade use
**Desinstalar pela plataforma** na página de detalhes.

## Página de detalhes

Abra com **I**, **Shift+Enter**, botão **X** do controle ou clicando no jogo. Lá você encontra:

- informações do jogo: gêneros, descrição, desenvolvedora, data de lançamento e nota do Metacritic;
- tempo de jogo total e quando jogou por último;
- tamanho em disco e espaço livre na unidade;
- aviso de **atualização pendente** (Steam) e **Verificar arquivos**;
- link do **PCGamingWiki** e botões para abrir as pastas de **saves** e **configurações**;
- edição de nome, argumentos de execução, Steam App ID, coleções e artes;
- **ações antes/depois de jogar** (veja abaixo).

Clique num gênero para filtrar a biblioteca por ele.

## Favoritos e coleções

- **F** (ou **Y** no controle) marca/desmarca o jogo como favorito. Favoritos têm sua própria seção.
- Na página de detalhes, adicione o jogo a uma ou mais **coleções** (por exemplo "Coop", "Zerados", "Com a família").
  As coleções aparecem na barra lateral.
- Jogos **ocultos** somem da biblioteca, mas podem ser mostrados de novo pelo filtro.

## Artes

As artes vêm da Steam automaticamente (capa vertical, banner horizontal, imagem de fundo "hero" e logo). Para jogos
fora da Steam, o GamesHub procura o jogo pelo nome; se ele errar, informe o **Steam App ID** correto na página de
detalhes.

Para personalizar: na página de detalhes, **arraste uma imagem** sobre a arte que quer trocar, ou clique nela e
escolha um arquivo. **Restaurar** volta para a arte automática e **Atualizar artes** baixa tudo de novo.

Com uma chave do **SteamGridDB** (Configurações → Artes) o GamesHub também busca artes lá — ótimo para jogos que não
estão na Steam.

## Tempo de jogo

O GamesHub mede o tempo enquanto o processo do jogo está aberto, mesmo quando o jogo é aberto por um launcher. O
tempo registrado pela Steam (e a data em que jogou por último) também é importado. Você pode desligar a medição em
**Configurações → Tempo de jogo**.

## Ações antes/depois de jogar

Cada jogo pode executar ações automáticas ao abrir: **abrir um programa**, **fechar um processo**, trocar o **plano
de energia**, o **dispositivo de áudio** ou a **resolução da tela**, ou **esperar** alguns segundos. Quando o jogo
fecha, o que foi alterado volta ao normal sozinho.

## Configurações

Abra com **Ctrl+,**. Principais opções:

| Seção | O que dá para ajustar |
|---|---|
| Biblioteca | pasta de jogos, fontes importadas (Steam, Epic, Riot, Hydra) |
| Ao jogar | minimizar para a bandeja, minimizar ou não fazer nada ao abrir um jogo |
| Janela | iniciar com o Windows, iniciar minimizado, fechar para a bandeja |
| Atalhos | atalho global (Ctrl+Alt+G) e paleta de início rápido (Ctrl+Shift+Espaço) |
| Aparência | grade ou lista, capas horizontais ou verticais, ordenação, menos animações |
| Artes | artes automáticas, chave do SteamGridDB |
| Atualizações | verificar ao iniciar, repositório do GitHub (dono/repositório) |

## Atalhos de teclado

| Tecla | Ação |
|---|---|
| **Enter** | Jogar |
| **I** ou **Shift+Enter** | Detalhes |
| **F** | Favoritar / desfavoritar |
| **Delete** | Remover da biblioteca |
| **Ctrl+F** ou **/** | Buscar |
| **Ctrl+N** | Adicionar jogo |
| **Ctrl+,** | Configurações |
| **F11** | Big Picture |
| **Esc** | Voltar |
| **F5** | Reescanear a biblioteca |
| **Setas** | Navegar pela grade |
| **Ctrl+Alt+G** (global) | Mostrar/ocultar o GamesHub de qualquer lugar |
| **Ctrl+Shift+Espaço** (global) | Paleta de início rápido: digite parte do nome e Enter para jogar |

Os atalhos globais podem ser trocados ou desligados em Configurações.

## Controle (Xbox e compatíveis)

| Botão | Ação |
|---|---|
| **D-pad / analógico esquerdo** | Navegar |
| **A** | Jogar |
| **X** | Detalhes |
| **Y** | Favoritar |
| **B** | Voltar |
| **LB / RB** | Trocar de seção |
| **Start** | Big Picture |
| **View** | Buscar |

O modo **Big Picture** (F11 ou Start) ocupa a tela inteira com cards maiores, ideal para TV e sofá.

## Bandeja, Jump List e início com o Windows

- Fechar a janela deixa o GamesHub na **bandeja** (perto do relógio), se essa opção estiver ligada. Clique no ícone
  para reabrir; o menu mostra os **jogos recentes** para abrir direto.
- Clique com o botão direito no GamesHub na **barra de tarefas** para ver os jogos recentes (Jump List).
- **Iniciar com o Windows** abre o GamesHub minimizado na bandeja ao entrar no Windows.

## Atualizações

Se um repositório estiver configurado em **Configurações → Atualizações**, o GamesHub verifica ao iniciar se há uma
versão nova e mostra um aviso. Ao aceitar, ele baixa o instalador, confere a assinatura SHA-256, fecha, atualiza e
abre de novo sozinho — seus dados não são tocados. Também dá para verificar manualmente pelo botão **Verificar agora**.

## Perguntas frequentes e solução de problemas

**O GamesHub não abre / aparece um erro sobre WebView2.**
Instale o [Microsoft Edge WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703) (gratuito). Ele já
vem no Windows 11; em alguns Windows 10 pode faltar.

**Onde ficam os logs?**
`%LOCALAPPDATA%\GamesHub\logs\gameshub.log` (o app) e `%TEMP%\GamesHub\setup.log` / `uninstall.log` (instalador).
Em Configurações há um botão para abrir a pasta de dados. Anexe os logs ao relatar um problema.

**Uma capa está errada.**
Na página de detalhes, corrija o **Steam App ID** ou arraste uma imagem sua. **Atualizar artes** baixa de novo.

**Quero limpar o cache de artes.**
Feche o GamesHub (inclusive na bandeja) e apague a pasta `%LOCALAPPDATA%\GamesHub\cache\art`. As artes são baixadas
de novo na próxima abertura; artes personalizadas também são apagadas.

**Um jogo não aparece.**
Confira se a fonte está ligada em Configurações e pressione **F5**. Para jogos de outras lojas, adicione o atalho ou
o executável arrastando para a janela.

**O tempo de jogo não conta.**
Verifique se **Tempo de jogo** está ligado. Jogos com anti-cheat que roda como administrador podem não ser
detectados; o tempo da Steam continua sendo importado.

**Como volto às configurações padrão?**
Feche o GamesHub e apague `%LOCALAPPDATA%\GamesHub\settings.json`. Sua biblioteca (`library.json`) é mantida.

**Como desinstalo?**
**Configurações do Windows → Aplicativos → GamesHub → Desinstalar**. Marque "Apagar também meus dados" se quiser
remover configurações, biblioteca e capas. Sua pasta de jogos nunca é alterada.
