# Contribuindo com o GamesHub

Obrigado pelo interesse! Este guia explica como preparar o ambiente, os padrões do projeto e como enviar uma
contribuição. Antes de começar algo grande, abra uma issue para conversarmos sobre a ideia.

## Ambiente

- **Windows 10 ou 11**
- **[.NET SDK 8](https://dotnet.microsoft.com/download)**: usado apenas pelo compilador Roslyn. O app roda no
  .NET Framework 4.8, que já vem com o Windows.
- **[Node.js](https://nodejs.org/) 20 ou mais recente**: para as verificações e testes da interface.
- Microsoft Edge WebView2 Runtime (já presente no Windows 11 e nos Windows 10 atualizados).

```powershell
git clone https://github.com/brunocsilva41/GamesHub.git
cd GamesHub
./build.ps1 -Test              # compila e roda os testes C#
./ci/Invoke-Pipeline.ps1       # a pipeline completa, igual à CI
```

O app compilado fica em `dist/app/GamesHub.exe`. Dicas:

- `GamesHub.exe --debug` habilita as ferramentas de desenvolvedor do WebView2 (F12).
- Defina `GAMESHUB_DATA_DIR` para uma pasta descartável para não mexer na sua biblioteca real.
- A interface pode ser desenvolvida num navegador comum: sirva a pasta `web/` com qualquer servidor estático e ela
  usa a ponte simulada ("modo demonstração").

Leia [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para entender os módulos e o protocolo da ponte, e
[docs/PIPELINE.md](docs/PIPELINE.md) para as etapas da pipeline.

## Padrões do projeto

- **Sem novas dependências.** Nada de pacotes NuGet, frameworks JS, CDNs ou etapas de build na interface. Use o
  que o .NET Framework 4.8 e o navegador já oferecem. Se achar que uma dependência é indispensável, discuta numa
  issue antes.
- **Arquivos pequenos e focados.** Prefira dividir (por exemplo, com classes `partial` ou módulos ES) a ter
  arquivos com mais de ~500 linhas.
- **Idiomas:** todo texto visível ao usuário em **português do Brasil, com acentos**. Código, identificadores,
  comentários e mensagens de log em **inglês**.
- **Erros nunca são engolidos em silêncio:** no mínimo `Log.Warn(...)`. Mensagens de erro para o usuário são em
  pt-BR e dizem o que fazer.
- **APIs compatíveis com o .NET Framework 4.8** (sem `System.Text.Json`, `Span` ou APIs do .NET 5+). Sintaxe C#
  moderna é bem-vinda.
- **Segurança:** a página do WebView2 é tratada como não confiável. Valide no C# tudo o que vem da interface e não
  abra caminhos ou URLs arbitrários (veja o modelo de segurança em ARCHITECTURE.md).
- **Privacidade:** nenhuma conexão de rede nova sem uma opção para desligá-la e sem documentá-la no README e em
  ARCHITECTURE.md.
- **Testes:** lógica pura (parsers, regras, associação de nomes) deve ter testes em `tests/`. Toda classe estática
  pública terminada em `Tests` com métodos `Test*` é descoberta automaticamente.
- **Arquivos de texto em UTF-8** e finais de linha conforme o `.gitattributes`.

## Como adicionar um comando da ponte

Um comando novo precisa existir em **todos** estes lugares, e a pipeline confere o cruzamento:

1. **C#**: um `case` em `Execute` (`src/GamesHub/Core/BridgeCommands.cs`) ou em `ExecuteIntegration`
   (`BridgeCommands.Integrations.cs`). Trabalho de disco ou rede vai para `Task.Run`; valide os argumentos com
   `RequireString`/`RequireGame` e lance `BridgeException` com mensagem em pt-BR para erros esperados.
2. **DTO**: se a resposta tiver um formato novo, mapeie em `src/GamesHub/Core/BridgeDto.cs` (JSON em camelCase) e
   cubra com testes em `tests/Core/BridgeDtoTests.cs`.
3. **Interface**: chame pelo `web/js/actions.js` / `actions2.js` (ou `bridge.call`). Se o comando for lento,
   ajuste o tempo limite em `web/js/bridge.js`.
4. **Simulação**: implemente o comando na ponte simulada (`web/js/mock.js`, `mock-features.js` ou
   `mock-variants.js`) para que o modo demonstração continue funcionando.
5. **Documentação**: acrescente o comando (e eventos novos) às tabelas do protocolo em
   [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#protocolo-da-ponte-bridge).

## Commits

- Mensagens curtas no imperativo, em inglês ou português, com o assunto em até ~72 caracteres. Ex.:
  `Add Heroic Games Launcher source`, `Corrige capa de jogos da Riot`.
- Um commit por mudança lógica; não misture refatoração com correção.
- Não versione arquivos gerados (`dist/`), dados pessoais, caminhos da sua máquina nem segredos. A etapa de
  higiene da pipeline recusa esses casos.

## Pull requests

1. Faça um fork e crie uma branch a partir de `main` (`feature/…`, `fix/…`).
2. Rode `./ci/Invoke-Pipeline.ps1` e garanta que tudo passa.
3. Atualize a documentação e acrescente uma linha na seção `## [Não lançado]` do [CHANGELOG.md](CHANGELOG.md),
   em pt-BR, na categoria certa (Adicionado, Alterado, Corrigido, Removido, Segurança).
4. Abra o PR preenchendo o modelo. Para mudanças visuais, inclua capturas de tela.
5. A CI precisa passar antes da revisão. Mudanças pedidas na revisão entram como commits novos na mesma branch.

Não altere a versão em `AssemblyInfo.cs`: isso é feito na publicação (veja [docs/RELEASING.md](docs/RELEASING.md)).

## Relatando problemas

Use os modelos de [issue](https://github.com/brunocsilva41/GamesHub/issues/new/choose). Inclua a versão do
GamesHub (Configurações → Sobre), a versão do Windows e um trecho de `%LOCALAPPDATA%\GamesHub\logs\gameshub.log`.
Vulnerabilidades de segurança **não** devem ser relatadas em issues públicas: siga o [SECURITY.md](SECURITY.md).

Ao contribuir, você concorda que sua contribuição será licenciada sob a [licença MIT](LICENSE) do projeto.
