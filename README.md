# GamesHub

**Uma biblioteca de jogos leve e bonita para Windows.** O GamesHub reúne num só lugar os jogos da Steam, da
Epic Games, da Riot, do Hydra e os atalhos da sua pasta de jogos — com capas em alta resolução, tempo de jogo,
coleções e navegação por teclado ou controle. Sem conta, sem anúncios, sem serviço rodando em segundo plano.

![Biblioteca](docs/screenshots/library.png)

| Detalhes do jogo | Big Picture | Configurações |
|---|---|---|
| ![Detalhes](docs/screenshots/details.png) | ![Big Picture](docs/screenshots/bigpicture.png) | ![Configurações](docs/screenshots/settings.png) |

## Recursos

- **Importação automática** da Steam (todas as bibliotecas), Epic Games, Riot e Hydra, além dos atalhos
  (`.lnk`, `.url`, `.exe`) da sua pasta de jogos — com detecção de atalhos quebrados e limpeza da biblioteca.
- **Artes em HD**: capa, banner, hero e logo baixados automaticamente (Steam CDN; SteamGridDB opcional com sua chave),
  ou escolhidos por você arrastando uma imagem.
- **Página de detalhes** com gêneros, descrição, desenvolvedora, data de lançamento e nota do Metacritic, filtro por
  gênero, link do PCGamingWiki, atalhos para as pastas de saves/configurações, tamanho em disco e espaço livre.
- **Favoritos, coleções** e jogos ocultos; ordenação por nome, recentes, tempo de jogo ou data de adição.
- **Tempo de jogo** medido pelo próprio GamesHub e importado da Steam (incluindo "jogado por último").
- **Variações de execução** agrupadas num único card ("Jogar ▾") e **ações antes/depois de jogar** por jogo
  (abrir programa, fechar processo, plano de energia, dispositivo de áudio, resolução, esperar) — desfeitas
  automaticamente quando o jogo fecha.
- Aviso de **atualização pendente** (Steam), **Verificar arquivos** e **desinstalar pela plataforma**.
- **Teclado, controle e modo Big Picture**, **paleta de início rápido** (Ctrl+Shift+Espaço, personalizável) e
  **atalho global** (Ctrl+Alt+G) para abrir o GamesHub de qualquer lugar.
- **Bandeja do sistema** com jogos recentes, **Jump List** na barra de tarefas e início com o Windows.
- **Instalador de arquivo único** (sem administrador) e **atualização automática** opcional pelo GitHub Releases.

## Instalação

1. Baixe `GamesHub-Setup-<versão>.exe` na página de versões (Releases).
2. Execute-o e siga o assistente — a instalação é só para o seu usuário, não pede permissão de administrador.
3. Se você já usava a versão 1.x, o instalador atualiza no mesmo lugar e mantém sua pasta de jogos, capas e histórico.

Instalação silenciosa (por exemplo, em scripts):

```powershell
.\GamesHub-Setup-2.0.0.exe /silent [/dir "C:\Apps\GamesHub"] [/games "D:\Jogos"] [/nodesktop] [/nostartmenu] [/autostart]
```

O resultado fica em `%TEMP%\GamesHub\setup-result.txt` (`OK:<pasta>` ou `ERRO:<mensagem>`) e o log em
`%TEMP%\GamesHub\setup.log`. Para remover: **Configurações do Windows → Aplicativos**, ou `Uninstall.exe /silent`
(adicione `/purge` para apagar também seus dados).

## Requisitos

- Windows 10 ou 11 (64 bits recomendado)
- [Microsoft Edge WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703)
- .NET Framework 4.8

O WebView2 e o .NET Framework 4.8 já vêm instalados no Windows 11 (e na maioria dos Windows 10 atualizados).

## Compilar a partir do código

Pré-requisito: [.NET SDK](https://dotnet.microsoft.com/download) (apenas pelo compilador Roslyn — o app roda no
.NET Framework 4.8 que já vem com o Windows; não há pacotes NuGet).

```powershell
./build.ps1                    # app em dist/app
./build.ps1 -Test              # + testes
./build.ps1 -Test -Package     # + Uninstall.exe e instalador em dist/package
./build.ps1 -Clean -Package    # apaga dist/ antes
```

`-Package` gera `dist/package/GamesHub-Setup-<versão>.exe` (com o app embutido) e o `.sha256` correspondente.
A versão vem de `src/GamesHub/Properties/AssemblyInfo.cs`.

Para publicar uma versão: crie uma Release no GitHub com a tag `v<versão>` e anexe o `.exe` e o `.sha256`.
Quem tiver o repositório configurado em **Configurações → Atualizações** recebe o aviso e atualiza com um clique.

## Estrutura do projeto

```
src/GamesHub/        app (C# WinForms + WebView2)
  Core/              janela, ponte JS↔C#, configurações, bandeja, integração com o Windows
  Library/           fontes de jogos (pasta, Steam, Epic…), metadados, execução, tempo de jogo
  Artwork/           download e cache de artes
  Update/            verificador de atualizações (GitHub Releases)
  Shared/            contratos e infraestrutura comuns
src/Installer/       Setup.exe e Uninstall.exe (Common/, Setup/, Uninstall/)
web/                 interface (HTML/CSS/JS puro, sem frameworks, funciona offline)
assets/              ícones, manifestos e configuração do executável
tests/               testes (executados por ./build.ps1 -Test)
docs/                ARCHITECTURE.md, USER_GUIDE.md
```

Detalhes técnicos e contratos entre módulos: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
Guia de uso: [docs/USER_GUIDE.md](docs/USER_GUIDE.md). Histórico: [CHANGELOG.md](CHANGELOG.md).

## Onde ficam os dados

| O quê | Onde |
|---|---|
| Programa | `%LOCALAPPDATA%\Programs\GamesHub` |
| Configurações | `%LOCALAPPDATA%\GamesHub\settings.json` |
| Biblioteca (tempo de jogo, favoritos, coleções, ajustes) | `%LOCALAPPDATA%\GamesHub\library.json` |
| Cache de artes | `%LOCALAPPDATA%\GamesHub\cache\art\` |
| Atalhos removidos (lixeira) | `%LOCALAPPDATA%\GamesHub\trash\` |
| Logs | `%LOCALAPPDATA%\GamesHub\logs\gameshub.log` |
| Logs do instalador | `%TEMP%\GamesHub\setup.log`, `uninstall.log` |

O GamesHub nunca apaga nada da sua pasta de jogos: remover um jogo move o atalho para a lixeira do próprio
GamesHub (com opção de desfazer).

## Privacidade

O GamesHub não tem conta, telemetria nem anúncios. As únicas conexões de rede são:

- **Steam (CDN e API da loja)** — para baixar artes e buscar nomes/informações de jogos. Pode ser desligado em
  Configurações (artes automáticas).
- **SteamGridDB** — somente se você informar sua própria chave de API.
- **GitHub** — somente se você configurar um repositório de atualizações; consulta a última versão publicada e,
  se você aceitar, baixa o instalador.
- Links que você abre (PCGamingWiki, páginas da loja) vão para o seu navegador.

Nada mais é enviado a lugar nenhum; todos os seus dados ficam no seu computador.

## Licença

© 2026 Bruno Silva. O WebView2 é distribuído sob a licença da Microsoft (`lib/WebView2-LICENSE.txt`).
