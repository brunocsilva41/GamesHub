# Pipeline de CI/CD

O GamesHub é verificado, empacotado e publicado por uma pipeline única, que roda **igual** na sua máquina e no
GitHub Actions. Nenhuma versão é publicada sem passar por todas as etapas.

- Ponto de entrada local: `ci/Invoke-Pipeline.ps1`
- Workflows: `.github/workflows/ci.yml`, `release.yml`, `codeql.yml`
- Procedimento de publicação: [RELEASING.md](RELEASING.md)

## Executar localmente

Pré-requisitos: Windows 10/11, [.NET SDK 8](https://dotnet.microsoft.com/download) (só pelo compilador Roslyn) e
[Node.js](https://nodejs.org/) 20 ou mais recente.

```powershell
./ci/Invoke-Pipeline.ps1
```

O script executa as etapas 1 a 6 abaixo, na ordem, para na primeira falha e termina com um resumo. Na sua
máquina, a etapa ponta a ponta roda em **modo App**: o app compilado é aberto com uma pasta de dados temporária
(`GAMESHUB_DATA_DIR`), que também lhe dá uma instância única própria — ele nunca conversa com o GamesHub que você
usa. A instalação/desinstalação real só acontece na CI, num Windows descartável. A etapa 7 é feita pelos serviços
de segurança do GitHub.

```powershell
./ci/Invoke-Pipeline.ps1 -Stage Hygiene,Web   # verificação rápida antes de um commit (segundos)
./ci/Invoke-Pipeline.ps1 -SkipE2E -KeepGoing  # tudo menos a ponta a ponta, sem parar na primeira falha
./ci/stages/Package.ps1                       # cada etapa também pode ser executada sozinha
```

Os relatórios ficam em `dist/reports/` (um JSON por etapa, JUnit dos testes C# e da interface) e as evidências
da ponta a ponta em `dist/e2e/`. Na CI, cada falha vira uma anotação no arquivo e linha exatos, e o resumo de
cada etapa aparece na página da execução.

## Etapas

| # | Etapa | O que verifica |
|---|---|---|
| 1 | **Higiene** | Varredura de segredos e de caminhos pessoais (`C:\Users\<nome>`…); finais de linha e codificação (UTF-8); lista de binários permitidos, com SHA-256 fixado das DLLs de `lib/` (`lib/checksums.sha256`); arquivos obrigatórios (`LICENSE`, `README.md`, `CHANGELOG.md`, `SECURITY.md`…); consistência da versão entre `AssemblyInfo.cs` e o `CHANGELOG.md`. |
| 2 | **Web** | `node ci/web/lint.mjs`: verificações estáticas da interface, incluindo o cruzamento **interface ↔ C# ↔ simulação** dos comandos da ponte (todo comando chamado pela interface precisa existir em `Core/BridgeCommands*.cs` e na ponte simulada). Depois `node --test tests/web/`. |
| 3 | **Build** | Compilação com Roslyn e **avisos tratados como erros** (`build.ps1 -Strict`); **build reproduzível**: o mesmo commit compilado duas vezes precisa gerar um `GamesHub.exe` idêntico byte a byte (caminhos de origem mapeados com `-pathmap`); orçamento de tamanho do executável. |
| 4 | **Testes unitários** | Executor de testes C# (`tests/TestRunner.cs`), com relatório JUnit XML; a contagem de testes não pode regredir e uma execução sem testes é falha. |
| 5 | **Pacote** | Instalador de arquivo único `GamesHub-Setup-<versão>.exe` + `.sha256`, verificado **sem executá-lo**: o payload embutido é idêntico ao que foi compilado e testado; contém todos os arquivos obrigatórios e nada de desenvolvimento (testes, documentação, símbolos); a pasta `web/` empacotada é idêntica à do repositório; as DLLs batem com os hashes fixados; versões de todos os executáveis; o instalador só referencia o .NET Framework; orçamento de tamanho; status da assinatura Authenticode. |
| 6 | **Ponta a ponta** (só CI) | Num runner Windows limpo: instalação silenciosa → o app é aberto e inspecionado pelo protocolo DevTools do WebView2 (interface carregada, biblioteca, capturas de tela) → instalação de atualização por cima → desinstalação silenciosa → verificação de que arquivos, atalhos e registro foram removidos. |
| 7 | **Segurança** | CodeQL para C# e JavaScript; varredura de segredos com gitleaks; GitHub Actions fixadas por SHA de commit; tokens com permissões mínimas; Dependabot para atualizar as Actions. |

## Workflows

### `ci.yml` — integração contínua

- **Quando:** push e pull request para `main` (e reutilizado pelo `release.yml`).
- **O que faz:** Higiene + gitleaks (histórico inteiro) e Web em Linux; Build/testes/Pacote em Windows; a ponta a
  ponta roda num **segundo** runner Windows limpo, sobre o **mesmo artefato** baixado (com o SHA-256 conferido).
- **Check obrigatório:** o job **✔ All checks passed** só passa se todos os outros passarem — é ele que deve ser
  exigido na proteção da branch `main`.
- **Artefatos:** instalador e `.sha256`, relatórios de testes (JUnit), resultado e capturas de tela da ponta a ponta.

### `release.yml` — publicação

- **Quando:** push de uma tag `vX.Y.Z`.
- **O que faz:**
  1. confere que a tag corresponde à versão em `AssemblyInfo.cs` e a uma seção `## [X.Y.Z] - AAAA-MM-DD` do
     `CHANGELOG.md`;
  2. executa novamente todas as etapas;
  3. **assina a lista de somas** para o atualizador do app (`GamesHub-Setup-X.Y.Z.exe.sha256.sig`, veja
     [Assinatura das atualizações](#assinatura-das-atualizações));
  4. publica uma **GitHub Release** com o instalador, o `.sha256`, o `.sha256.sig`, as notas extraídas do
     `CHANGELOG.md` e uma **atestação de proveniência do build** (build provenance attestation).
- Nenhuma expressão `${{ }}` é expandida dentro de scripts `run:`: nome da tag, saídas de etapas e segredos chegam
  aos scripts só por variáveis de ambiente (`env:`), e o formato da tag (`vMAJOR.MINOR.PATCH[-pre]`) é validado
  antes de qualquer uso.
- O gate também exige que a tag seja **anotada**, que o commit esteja na `main`, que a seção do CHANGELOG tenha
  conteúdo e que a versão ainda não tenha sido publicada. O job de publicação usa o ambiente `release` do GitHub
  (onde é possível exigir aprovação manual) e só ele recebe permissão de escrita.
- **Assinatura de código (opcional):** com os segredos `GAMESHUB_SIGN_PFX_BASE64` (certificado `.pfx` em
  Base64) e `GAMESHUB_SIGN_PFX_PASSWORD`, `GamesHub.exe`, `Uninstall.exe` e o instalador são assinados (SHA-256 +
  carimbo de tempo) durante o empacotamento — antes da ponta a ponta — e a etapa Pacote passa a **exigir**
  assinaturas válidas.

#### Assinatura das atualizações

O atualizador embutido só instala uma versão cuja lista de somas tenha assinatura válida (detalhes em
[SECURITY.md](../SECURITY.md#como-as-atualizações-são-verificadas)). Isso depende de um segredo **obrigatório**:

| Segredo | Conteúdo | Usado por |
|---|---|---|
| `GAMESHUB_UPDATE_SIGNING_KEY` | chave privada **ECDSA P-256** em PKCS#8, Base64 numa linha | job `publish` do `release.yml` (ambiente `release`) |

- A etapa **Sign the update manifest** (`ci/release/Sign-UpdateManifest.ps1`) recebe o segredo só por `env:`, assina
  o `.sha256` e confere a assinatura com a chave pública embutida no app antes de publicar. Se o segredo não existir
  ou não corresponder à chave embutida, **a publicação falha**.
- O par de chaves é gerado uma única vez por `pwsh tools/New-UpdateSigningKey.ps1`: a chave privada vai para
  `.local/update-signing.key` do checkout principal (ignorado pelo git; o script nunca a imprime) e a pública é
  escrita em `src/GamesHub/Update/UpdateSigningKey.cs`. Para cadastrar o segredo (no ambiente `release`):

  ```powershell
  Get-Content .local/update-signing.key -Raw | gh secret set GAMESHUB_UPDATE_SIGNING_KEY --env release
  ```

  (em Bash: `gh secret set GAMESHUB_UPDATE_SIGNING_KEY --env release < .local/update-signing.key`). Guarde uma
  cópia da chave privada fora do computador (gerenciador de senhas): sem ela, novas versões não chegam aos apps
  instalados pelo atualizador automático.
- **Troca de chave:** cada app instalado confia só na chave pública embutida nele. `-Rotate` gera um novo par
  (atualize o segredo em seguida); a partir daí, as instalações antigas recusam a atualização automática — com falha
  fechada, abrindo a página da versão — e precisam de **uma** atualização manual para passar a confiar na nova
  chave. Faça isso só se a chave privada for perdida ou vazar (neste caso, o quanto antes).

### `codeql.yml` — análise estática

- **Quando:** semanalmente e em pull requests.
- **O que faz:** análise CodeQL de C# e JavaScript; os alertas aparecem na aba **Security** do repositório.

## Procedimento de publicação (mantenedor)

```powershell
./tools/release.ps1 -Version X.Y.Z
```

O script:

1. atualiza a versão em `src/GamesHub/Properties/AssemblyInfo.cs`;
2. move as entradas de `## [Não lançado]` do `CHANGELOG.md` para uma nova seção `## [X.Y.Z] - AAAA-MM-DD` e
   atualiza os links de comparação no fim do arquivo;
3. executa a pipeline local;
4. cria o commit e a tag anotada `vX.Y.Z`;
5. pede confirmação e envia commit e tag ao GitHub.

Com `-DryRun`, o script faz tudo (inclusive a pipeline) e desfaz as alterações no fim, sem commit nem tag. Se a
pipeline falhar, nada é commitado.

A partir daí, o `release.yml` publica a versão. O passo a passo resumido está em [RELEASING.md](RELEASING.md).

## Capturas de tela do README

`./tools/screenshots.ps1` regenera `docs/screenshots/*.png` a partir do app real, isolado (pasta de dados
temporária e uma biblioteca de demonstração com jogos populares da Steam), navegando pelo protocolo DevTools —
sem tocar nas suas configurações e sem enviar teclas ao Windows.

## Como verificar um download

Cada versão publica, ao lado do instalador, um arquivo `GamesHub-Setup-X.Y.Z.exe.sha256`.

**1. Soma SHA-256** (PowerShell):

```powershell
Get-FileHash .\GamesHub-Setup-X.Y.Z.exe -Algorithm SHA256
Get-Content  .\GamesHub-Setup-X.Y.Z.exe.sha256
```

Os dois valores precisam ser iguais (a comparação não diferencia maiúsculas de minúsculas).

**2. Atestação de proveniência** ([GitHub CLI](https://cli.github.com/)):

```powershell
gh attestation verify .\GamesHub-Setup-X.Y.Z.exe --repo brunocsilva41/GamesHub
```

Isso comprova que o arquivo foi gerado pelo workflow `release.yml` deste repositório, a partir do commit da tag, e
não foi alterado depois.

**3. Aviso do Windows SmartScreen.** Os instaladores não são assinados com certificado de assinatura de código
(que é pago). Por isso, o Windows pode mostrar "O Windows protegeu o computador" nas primeiras vezes que uma versão
é baixada. Depois de conferir a soma e a atestação, clique em **Mais informações → Executar assim mesmo**. A
pipeline já suporta assinatura: basta configurar o certificado como segredo do repositório.

O atualizador do próprio GamesHub faz tudo isso automaticamente antes de instalar — e vai além: exige a
assinatura `GamesHub-Setup-X.Y.Z.exe.sha256.sig` da lista de somas, conferida com a chave pública embutida no app.
