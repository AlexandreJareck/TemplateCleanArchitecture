# Relatório da Fase 20 — Isolar e modernizar o projeto VSIX

Data de execução: 2026-10-12 (America/Sao_Paulo). Checkout autoritativo: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `0c963be` (Fase 19 mergeada) com a árvore de trabalho limpa no início.

## 1. Resumo executivo e status

**Status: implementada, mas verificada apenas em parte. Não declarada completa.**

Concluído e verificado:

- A decisão 1 (VSIX opcional e isolado, só VSSDK 17.x) foi confirmada e não foi revogada. Durante a execução, o dono decidiu explicitamente manter a VSSDK 17.x e não migrar para a 18.
- Pacotes atualizados dentro da linha 17.x: `Microsoft.VisualStudio.SDK` 17.0.32112.339 → **17.14.40265** e `Microsoft.VSSDK.BuildTools` 17.12.2069 → **17.14.2142**. Foi adicionado um pin transitivo `MessagePack`/`MessagePack.Annotations` **2.5.307**, que só o VSIX resolve, para eliminar 11 advisories (2 altos).
- Novo caminho de validação próprio do repositório, `scripts/vsix-validation.ps1`, com MSBuild do Visual Studio descoberto via `vswhere`. Ele restaura com audit, compila sem deploy, inspeciona o pacote e retorna exit ≠ 0 em qualquer falha. Ficou verde com o VS 2022 17.14.16 e com o VS 2026 Insiders 18.11.
- Instalação e desinstalação no hive **experimental** do VS 2022 (`17.0_<id>Exp`): OK.
- O gate da aplicação (`TemplateCleanArchitecture.App.slnf`) continua independente. Os grafos de pacotes de produção e de teste ficaram idênticos e o OpenAPI não mudou (ver §11).

Não verificado (bloqueado):

- **Carga da extensão no VS 2022.** O smoke (`devenv /RootSuffix Exp /Log /Command File.Exit`) saiu com código 0 em 8 s, mas o ActivityLog mostra que o devenv terminou antes de carregar o shell: o hive ficou sem cache de componentes e sem settings. Uma abertura manual do mesmo hive exibiu o diálogo "Erro de Configuração" e encerrou em 4 s. A causa não foi diagnosticada, porque o dono pediu para deixar o VS 2022 de lado ("esqueça o 22, eu tenho o 26").
- **VS 2026.** O dono escolheu manter a 17.x e testar no 2026, mas **não autorizou** instalar no hive experimental do VS 2026 Insiders, que estava aberto com esta solução. Instalação, carga e rejeição fora do intervalo no 2026 ficaram sem verificação.
- **CI.** O repositório não tem provedor de CI. Há um ponto de entrada adequado para CI, mas nenhum CI o consumiu.
- **Ativação funcional.** Não se aplica: a extensão é um "Empty VSIX Project", sem package, comando ou template (ver §3).

## 2. Checkout, mudanças preservadas e lote lógico

- No início, `git status --short` estava vazio e o HEAD em `0c963be`. Não havia mudanças concorrentes a preservar. O `Directory.Packages.props` foi alterado só nas entradas do VSIX (duas versões trocadas e duas entradas MessagePack com comentário), aplicadas sobre o arquivo atual.
- Arquivos alterados nesta fase (um único lote lógico): `Directory.Packages.props`, `scripts/vsix-validation.ps1` (novo), `ExtensionVsix/README.md` (novo), `AGENTS.md` e este relatório.
- Projeto (`Template.csproj`), manifesto (`source.extension.vsixmanifest`) e `AssemblyInfo.cs`: **sem mudança**.

## 3. Decisão, entregável e faixa de Visual Studio

- A decisão 1 (2026-10-10, `docs/phase-3-implementation-plan.md`) segue em vigor. Antes desta fase o usuário não a havia revogado. Durante a fase, escolheu "17.x + testar no 2026" em vez de ampliar o manifesto para `[17.0,19.0)` ou migrar para a VSSDK 18.
- O entregável continua opcional. Não há usuários definidos além do mantenedor, que publica como "Alexandre Jareck". O histórico mostra o projeto criado em `25d7028` ("feat: add project extension vsix", 2025-03-11) a partir do template "Empty VSIX Project", sem conteúdo de template até hoje.
- Faixa declarada no manifesto: `Microsoft.VisualStudio.Community` `[17.0, 18.0)` com `ProductArchitecture` amd64 e pré-requisito `Microsoft.VisualStudio.Component.CoreEditor` `[17.0,18.0)`.
- Faixa suportada documentada: **VS 2022 17.14**, o único baseline do VS 2022 com serviço, em mainstream até janeiro de 2032. As versões 17.0–17.13 estão dentro do intervalo do manifesto, mas fora de suporte e não foram testadas.
- VS 2026 (18.x): fora do intervalo do manifesto. A Microsoft afirma que o VS 2026 aceita extensões do VS 2022, mas isso **não foi validado aqui**.

## 4. Inventário pré-mudança

| Item | Valor |
|---|---|
| Formato | csproj legado (não SDK), `ToolsVersion` 15.0, `TargetFrameworkVersion` v4.7.2, `MinimumVisualStudioVersion` 17.0 |
| Project types | `{82b43b9b-a64c-4715-b499-d71e9ca2bd60}` (VSIX) e `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` (C#) |
| Imports | `Microsoft.Common.props`, `Microsoft.CSharp.targets` e `$(VSToolsPath)\VSSDK\Microsoft.VsSDK.targets` (condicional) |
| Empacotamento | `GeneratePkgDefFile`, `IncludeAssemblyInVSIXContainer`, `IncludeDebugSymbolsInVSIXContainer`/`LocalVSIXDeployment`, `CopyBuildOutputToOutputDirectory` = false; Debug inicia `devenv.exe /rootsuffix Exp` |
| Pacotes | `Microsoft.VisualStudio.SDK` 17.0.32112.339 (`ExcludeAssets="runtime"`), `Microsoft.VSSDK.BuildTools` 17.12.2069, `Microsoft.VsSDK.CompatibilityAnalyzer` 17.12.2069 (transitivo). 128 pacotes resolvidos |
| Audit | 12 avisos NU1902/NU1903 em `MessagePack` 2.5.192… o grafo antigo resolvia `MessagePack` **2.2.85** (12 advisories, 2 altos) |
| Artefato existente | `bin\Debug\Template.vsix`, 2.876 bytes, 2026-10-08 13:38, SHA-256 `CECCAAE87F0D51BC2B9ACF770BAA1D4BD606B0F3772A44876BEC06242D8971E2` (preservado em `artifacts/validation/vsix/previous-debug-artifact/`, junto com o `project.assets.json` antigo) |
| Baseline Release (pacotes antigos) | `artifacts/validation/vsix/baseline-before/Template.vsix`, 1.815 bytes, SHA-256 `545FFDB329EF1A3AA6EFED922A93EC85C43B09BA0B7EFFA8A199BA7527B046E0`, build com 0 warnings e 0 erros, todas as checagens de pacote PASS, audit FAIL (MessagePack) |
| Solução | `TemplateCleanArchitecture.sln` contém o VSIX. `TemplateCleanArchitecture.App.slnf` tem os 17 projetos de aplicação e teste, sem o VSIX |
| Scripts/CI | Nenhum script de VSIX. `run-phase-validation.ps1` não toca no VSIX. Sem `.github`, Azure Pipelines, GitLab, AppVeyor ou similar |
| Docs com expectativa não suportada | `AGENTS.md` (não-gate com `dotnet build TemplateCleanArchitecture.sln` e validação VS MSBuild "pending") |

Toolchain instalado (via `vswhere -all -prerelease -products *`):

| Instância | Versão | MSBuild | Componentes relevantes |
|---|---|---|---|
| Visual Studio Community 2022 (`1c0c740f`) | 17.14.16 (17.14.36518.9), estável | 17.14.23.42201 | Workload `VisualStudioExtension` 17.14, `Microsoft.VisualStudio.Component.VSSDK`, targeting pack .NET Framework 4.7.2 |
| Visual Studio Community 2026 Insiders (`ab47ee71`) | 18.11.12224.323, prerelease | 18.11.0.42713 | Componente VSSDK presente; estava aberto com esta solução durante toda a fase |

## 5. Pesquisa oficial (2026-10-12) e matriz de compatibilidade

Fontes consultadas em 2026-10-12:

- Ciclo de vida e serviço do Visual Studio (VS 2022 com suporte só no 17.14, mainstream até janeiro de 2032; VS 2026 em mainstream): https://learn.microsoft.com/en-us/visualstudio/releases/2026/servicing-vs
- Como direcionar versões anteriores (sintaxe de `InstallationTarget`/`Prerequisite` e casamento das versões dos pacotes VSSDK com o VS alvo): https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/target-previous-versions
- Instância experimental (`devenv /RootSuffix Exp`): https://learn.microsoft.com/en-us/visualstudio/extensibility/the-experimental-instance
- Descoberta do MSBuild com `vswhere` (`-latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`, local estável em `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe`): https://github.com/microsoft/vswhere/wiki/Find-MSBuild
- Compatibilidade de extensões do VS 2022 com o VS 2026 (blog oficial do Visual Studio, 2025-10-21): https://devblogs.microsoft.com/visualstudio/modernizing-visual-studio-extension-compatibility-effortless-migration-for-extension-developers-and-users/
- NuGet (API oficial `api.nuget.org`): https://www.nuget.org/packages/Microsoft.VisualStudio.SDK. A última 17.x estável é **17.14.40265** (2025-05-14, listada, sem advisory). Não existe metapacote 18.x do SDK.
- NuGet: https://www.nuget.org/packages/Microsoft.VSSDK.BuildTools. A última 17.x estável é **17.14.2142** (2026-04-09, listada, sem advisory, depende de `Microsoft.VisualStudio.SDK.Analyzers` ≥ 17.7.79 e `Microsoft.VsSDK.CompatibilityAnalyzer` 17.14.2142). A linha 18.x (mais recente 18.9.820) foi **descartada** pela decisão 1.
- NuGet: https://www.nuget.org/packages/MessagePack. A 2.5.307 (2026-10-03, listada, sem advisory) é o último patch da linha 2.5.
- Advisories do MessagePack (GitHub Advisory Database): GHSA-2f33-pr97-265q, GHSA-2x83-8g95-xh59, GHSA-cj9g-3mj2-g8vv, GHSA-cxmj-83gh-fp49, GHSA-hv8m-jj95-wg3x (alto), GHSA-q2h6-ghwm-5qm8, GHSA-qhmf-xw27-6rqr, GHSA-v72x-2h86-7f8m, GHSA-vh6j-jc39-fggf (alto), GHSA-w567-gjr2-hm5j e GHSA-wfr3-xj75-pfwh são todos corrigidos na 2.5.301. GHSA-4qm4-8hg2-g2xm foi corrigido na 2.5.187. Exemplo: https://github.com/advisories/GHSA-hv8m-jj95-wg3x

| Dimensão | Valor |
|---|---|
| VS alvo / faixa | VS 2022, manifesto `[17.0,18.0)`; suportado: 17.14 |
| Instância de validação | VS 2022 Community 17.14.16 (build, instalação e smoke no hive `Exp`); VS 2026 Insiders 18.11 (só build) |
| `Microsoft.VisualStudio.SDK` | 17.14.40265 (casa com o baseline 17.14 suportado; referência só de compilação, `ExcludeAssets=runtime`, sem assembly no `.vsix`) |
| `Microsoft.VSSDK.BuildTools` | 17.14.2142 (última 17.x, mesma linha 17.14 do SDK e do toolchain) |
| .NET Framework | Targeting pack 4.7.2 (presente; o TFM não mudou) |
| Formato do projeto / schema do manifesto | csproj legado; `PackageManifest` 2.0.0 (`vsx-schema/2011`) |
| MSBuild | `<VS>\MSBuild\Current\Bin\MSBuild.exe` 17.14.23.42201 (VS 2022) e 18.11.0.42713 (VS 2026 Insiders) |
| VSIX produzido | Target `Microsoft.VisualStudio.Community` `[17.0, 18.0)`, amd64 |

Justificativa:

- O SDK 17.14 corresponde ao único VS 2022 com serviço. Como o pacote não contém assembly, a referência de compilação mais nova não impõe nenhum mínimo de runtime.
- O BuildTools 17.14.2142 é o último patch 17.x e se alinha ao SDK.
- O SDK 17.14 ainda traz `MessagePack` 2.5.192 vulnerável. Por isso o pin transitivo fica na mesma linha 2.5 (sem salto de major). Com `CentralPackageTransitivePinningEnabled` ativo, o pin só afeta projetos que já resolvem MessagePack, e nenhum projeto de aplicação ou teste o resolve (grafos idênticos, §11).

## 6. Prova de que tudo ficou em 17.x

`artifacts/validation/vsix/updated/resolved-packages.txt` (125 pacotes): `Microsoft.VisualStudio.SDK/17.14.40265`, `Microsoft.VSSDK.BuildTools/17.14.2142` e `Microsoft.VsSDK.CompatibilityAnalyzer/17.14.2142`. A checagem `no-visual-studio-18-packages` dá PASS: nenhum `Microsoft.VisualStudio.*`, `Microsoft.VSSDK.*` ou `Microsoft.VsSDK.*` em 18.x. O manifesto empacotado e o `catalog.json` não têm intervalo 18 (checagens `installation-target-17x`, `prerequisite-17x` e `catalog-no-visual-studio-18-range`).

## 7. Mudanças

- `Directory.Packages.props`: as duas versões VSSDK, mais `MessagePack` e `MessagePack.Annotations` 2.5.307 com um comentário de escopo.
- `scripts/vsix-validation.ps1` (novo):
  - descoberta via `vswhere` (faixa padrão `[17.0,19.0)` para o toolchain, a mais nova estável primeiro, `-Prerelease` opcional). A faixa escolhe só o MSBuild, não o alvo do manifesto;
  - verifica o MSBuild, o componente VSSDK e o targeting pack 4.7.2;
  - `msbuild /t:Restore` com `NuGetAuditMode=all`;
  - `msbuild /t:Rebuild /p:DeployExtension=false`;
  - inspeção do pacote e dos pacotes resolvidos;
  - `-Install`, `-Smoke` e `-Uninstall` opcionais, restritos ao hive `<major>.0_<instância><RootSuffix>`;
  - logs em `artifacts/validation/vsix/<Label>`.
- `ExtensionVsix/README.md` (novo): política de suporte, componentes, níveis de validação, CI e rollback.
- `AGENTS.md`: descrição do VSIX, remoção da expectativa de `dotnet build TemplateCleanArchitecture.sln` como caminho do VSIX, comando verificado e status de execução.
- O projeto, o manifesto e o `run-phase-validation.ps1` não foram alterados: o gate já não tocava no VSIX.

## 8. Comandos de build e resultados

Comando padrão (CI-suitable):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1
```

Comandos que o script executa (MSBuild = `<VS>\MSBuild\Current\Bin\MSBuild.exe`):

```
MSBuild ExtensionVsix\Template\Template.csproj /t:Restore /p:Configuration=Release /p:NuGetAudit=true /p:NuGetAuditMode=all /p:NuGetAuditLevel=low /nologo /nr:false /m:1
MSBuild ExtensionVsix\Template\Template.csproj /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:DeployExtension=false /nologo /nr:false /m:1
```

| Label | Toolchain | Restore | Audit | Build | Checagens | Exit |
|---|---|---|---|---|---|---|
| `baseline-before` (pacotes antigos) | VS 2022 / MSBuild 17.14.23 | OK | **FAIL**: 12 NU190x (MessagePack 2.2.85) | 0 warnings, 0 erros | todas PASS | 1 |
| `updated` (SDK/BuildTools 17.14 sem pin) | VS 2022 | OK | **FAIL**: 11 NU190x (MessagePack 2.5.192) | 0 warnings, 0 erros | todas PASS | 1 |
| `updated` (com pin 2.5.307) | VS 2022 | OK | 0 | 0 warnings, 0 erros | todas PASS | 0 |
| `exp-install` | VS 2022 | OK | 0 | 0/0 | + instalação PASS | 0 |
| `exp-smoke` | VS 2022 | OK | 0 | 0/0 | + smoke PASS (ver §10) | 0 |
| `exp-uninstall` | VS 2022 | OK | 0 | 0/0 | + desinstalação PASS | 0 |
| `vs2026-build` (`-VisualStudioVersionRange '[18.0,19.0)' -Prerelease`) | VS 2026 Insiders / MSBuild 18.11.0 | OK | 0 | 0 warnings, 0 erros | todas PASS | 0 |

Revisão de warnings: o build não teve nenhum warning em nenhuma execução. Os únicos avisos foram os de audit NuGet listados acima, resolvidos pelo pin.

## 9. Artefato e inspeção

- `artifacts/validation/vsix/updated/Template.vsix`: 1.846 bytes, SHA-256 `1A8640B32B7D9844E3F02690E46310F1DF1C920C58FD875EDA5AC42079A5667B`. O build pelo VS 2026 gerou 1.846 bytes com SHA-256 `3EFA767E58AFF1701370BBD09158AA93DCF8C9F5789A240F2987EFAC9109C01A`.
- O zip não é reprodutível byte a byte: os hashes mudam a cada build por timestamps e pelo `extensionDir` aleatório do `catalog.json`.
- Conteúdo: `extension.vsixmanifest` (888 bytes), `[Content_Types].xml` (243), `manifest.json` (387) e `catalog.json` (830). Não há `.dll`, `.pdb` ou `.pkgdef`.
- O manifesto empacotado é idêntico ao do baseline: identidade `Template.e9f68a18-5d0f-445b-836d-4ebdbca840f0` 1.0 (Alexandre Jareck), target Community `[17.0, 18.0)` amd64, pré-requisito CoreEditor `[17.0,18.0)` e dependência `Microsoft.Framework.NDP` `[4.5,)`.
- O `catalog.json` difere do baseline apenas pelo novo campo `"automaticallyAddedByExtensionPack":false` (BuildTools 17.14), pelo tamanho e pelo diretório aleatório.

## 10. Instalação, carga e smoke

- **Instalação (VS 2022, hive `17.0_1c0c740fExp`)**: `VSIXInstaller.exe /quiet /rootSuffix:Exp /instanceIds:1c0c740f` retornou exit 0. O log registra "Beginning installation … PerMachine: False" e o commit nos caches `VisualStudioExtensionCache` e `PerUserEnabledExtensionsCache`, ou seja, a extensão ficou instalada e **habilitada**. O manifesto extraído em `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_1c0c740fExp\Extensions\<pasta aleatória>` tem a versão 1.0. O log também aponta assinatura `Unsigned` (esperado).
- **Smoke**: `devenv /RootSuffix Exp /Log … /Command File.Exit` saiu com código 0 em 8 s. O ActivityLog tem 90 entradas e 0 erros. Os 40 warnings são todos de merge de binding redirects/code bases do próprio VS, nenhum referente à extensão.
  - Porém o log termina em "PkgDef registry shutdown complete" sem nenhuma carga de package, e o hive ficou sem `ComponentModelCache` e sem settings: o shell **não chegou a carregar**.
  - Uma abertura manual do mesmo hive (`devenv /RootSuffix Exp /Log`) mostrou a janela "Erro de Configuração" e saiu em 4 s.
  - Logo, **a carga da extensão no VS 2022 não foi comprovada**. A investigação parou por decisão do dono. Hipótese não verificada: o hive experimental foi criado pelo VSIXInstaller sem inicialização (a ferramenta oficial seria o reset `CreateExpInstance`/`ResetExperimental`).
- **Desinstalação**: `VSIXInstaller /uninstall:Template.e9f68a18-… /rootSuffix:Exp` retornou exit 0 e a pasta da extensão foi removida. O hive `17.0_1c0c740fExp` ficou só com `CompatibilityList.xml`, `extensions.configurationchanged`, `devenv.exe.config` e `privateregistry.bin`. Esse hive foi criado nesta fase e não foi removido.
- **Rejeição fora da faixa**: não foi testada. Instalar no VS 2026 provavelmente seria *aceito* pela política de compatibilidade e mexeria no hive experimental de um VS aberto, o que o dono não autorizou.
- **VS 2026**: sem instalação e sem runtime (decisão do dono). O perfil normal de nenhum Visual Studio foi tocado, nenhuma instância aberta foi fechada e nenhuma extensão do usuário foi desinstalada.
- **Pacote reconstruído**: a instalação usou o artefato reconstruído do zero pelo script (sem ajuste manual).

## 11. Gate da aplicação

`powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1` (sem switches de ensaio) rodou de 2026-10-12 19:00:11 a 19:07:42 com o SDK `C:\Users\alexa\.dotnet\dotnet.exe` (10.0.401). **Todos os passos terminaram com exit 0.** O gate não restaura, não compila e não instala o VSIX, e não depende do Visual Studio: só usa `dotnet` com `TemplateCleanArchitecture.App.slnf` e projetos individuais.

| Passo | Resultado |
|---|---|
| `01-restore` (`App.slnf`) | exit 0 |
| `02-build-webapi` | exit 0 |
| `03-test-architecture` | 30/30 |
| `04-test-unit` | 107/107 |
| `05-test-integration` | 141/141 |
| `06-test-postgresql` | 5/5 |
| `06b-test-rabbitmq` | 19/19 |
| `06c-test-elasticsearch` | 13/13 |
| `06f-test-redis` | 23/23 |
| `07-test-functional` | 191/191 |
| `07b-coverage` | exit 0 (descoberta = execução = aprovação; "Coverage report: OK") |
| `08-vulnerable-packages` | os 17 projetos sem pacote vulnerável |
| `09-docker-compose-config` | exit 0 |
| `10-capture-openapi` | exit 0 |
| `11-graphify` | exit 0 (4.060 nós, 8.577 arestas, 231 comunidades; `graphify-out/` é git-ignored) |
| `12-git-diff-check` | exit 0 |

Comparação exata do OpenAPI contra `docs/refactoring/baseline-phase-6`: `openapi-v1.json` e `openapi-v2.json` são **idênticos** depois de normalizar CRLF/LF (o hash bruto só difere pelo fim de linha do checkout, como nas fases anteriores). As amostras `health.json` e `product-v1/v2-get.json` diferem apenas em `totalDuration` e `createdDateTime`, que são valores de execução.

Isolamento: o gate não referencia `ExtensionVsix`. O `App.slnf` não contém o VSIX. A falta de Visual Studio não afeta nenhum passo. Os grafos de pacotes da aplicação ficaram idênticos (§12).

## 12. Audits de dependências

- VSIX: audit NuGet do restore isolado (`NuGetAuditMode=all`, nível low, diretos e transitivos) = **0 achados** com o pin; antes eram 12 (pacotes antigos) e 11 (17.14 sem pin).
- Aplicação: `dotnet list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive` (passo `08` do gate), resultado na §11.
- Grafo da aplicação: `scripts/package-graph-snapshot.ps1 -Label phase20-after -CompareWith phase20-before` deu produção 246 entradas com **0 diferenças** e teste 469 entradas com **0 diferenças**. Nem o pin MessagePack nem os pacotes VSSDK chegam aos projetos de aplicação.

## 13. CI

Não existe provedor de CI no repositório. O ponto de entrada CI-suitable é `powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1` (sem switches, exit ≠ 0 em falha), em um agente Windows com VS 2022 17.14 ou Build Tools e o componente VSSDK. Ele deve rodar como job separado e opcional. **Não verificado em CI**, e nenhum provedor foi criado.

## 14. Graphify

Não foi necessário regenerar. A fase não mudou projeto, referência nem código C# (só versões centrais de pacotes do VSIX, um script e docs). O passo `11-graphify` do gate rodou mesmo assim (§11).

## 15. Rollback

- Estado anterior: `Microsoft.VisualStudio.SDK` 17.0.32112.339 e `Microsoft.VSSDK.BuildTools` 17.12.2069, sem as entradas MessagePack. O projeto e o manifesto não mudaram.
- Procedimento:
  1. Reverter essas linhas do `Directory.Packages.props` como um lote, sem comandos Git destrutivos.
  2. Rodar `scripts/vsix-validation.ps1`, que já compilou esse estado em 2026-10-12 com o MSBuild 17.14.23 (a única falha é o audit do MessagePack).
  3. Reinstalar só em hive experimental (`-Install`) e remover com `-Uninstall`.
  4. Rodar de novo o `run-phase-validation.ps1`.
- Artefatos preservados (git-ignored): `artifacts/validation/vsix/previous-debug-artifact/` (o `.vsix` Debug de 2026-10-08, SHA-256 `CECCAAE8…71E2`, mais o `project.assets.json` antigo) e `artifacts/validation/vsix/baseline-before/` (Release dos pacotes antigos com logs e hash).
- Toolchain anterior = o atual (VS 2022 17.14.16, MSBuild 17.14.23), que continua instalado.

## 16. Riscos residuais e limitações

- A carga em runtime não foi comprovada em nenhuma versão do Visual Studio (§10). A extensão também não tem funcionalidade para ativar: é um VSIX vazio. Tornar o template útil seria mudança de produto, fora do escopo.
- Instalação no VS 2026 não verificada. Se for o alvo real, falta decidir entre manter `[17.0,18.0)` (dependendo da compatibilidade do VS 2026) e ampliar para `[17.0,19.0)`, que exige autorização para um alvo VS 18.
- O manifesto aceita de 17.0 a 17.13, que estão fora de suporte. Restringir para `[17.14,18.0)` seria uma decisão de produto e não foi feita.
- O pin MessagePack 2.5.307 foi publicado 9 dias antes desta execução. É só de compilação (`ExcludeAssets=runtime`, nada vai para o `.vsix`). Em runtime o VS usa o próprio MessagePack.
- O pacote não é assinado e não é reprodutível byte a byte.
- O hive experimental `17.0_1c0c740fExp` do VS 2022 ficou criado e com "Erro de Configuração" na abertura. Ele não foi resetado nem apagado por falta de autorização para limpar estado do Visual Studio.
- Sem CI.

## 17. Comandos executados (resumo sanitizado)

| Comando | Resultado |
|---|---|
| `vswhere -all -prerelease -products * -format json` (+ `-include packages`) | 2 instâncias (§4) |
| `dotnet restore TemplateCleanArchitecture.App.slnf …` + `package-graph-snapshot.ps1 -Label phase20-before` | exit 0; 246 entradas de produção e 469 de teste |
| `vsix-validation.ps1 -Label baseline-before` | exit 1 (só audit MessagePack 2.2.85) |
| `vsix-validation.ps1 -Label updated` (sem pin) | exit 1 (só audit MessagePack 2.5.192) |
| `vsix-validation.ps1 -Label updated` (com pin) | exit 0 |
| `vsix-validation.ps1 -Label exp-install -Install` | exit 0 |
| `vsix-validation.ps1 -Label exp-smoke -Smoke` | exit 0 (com a ressalva da §10) |
| `devenv /RootSuffix Exp /Log …` (manual) | "Erro de Configuração", encerrou em 4 s |
| `vsix-validation.ps1 -Label exp-uninstall -Uninstall` | exit 0 |
| `vsix-validation.ps1 -Label vs2026-build -VisualStudioVersionRange '[18.0,19.0)' -Prerelease` | exit 0 |
| `dotnet restore …` + `package-graph-snapshot.ps1 -Label phase20-after -CompareWith phase20-before` | exit 0; 0 diferenças |
| `run-phase-validation.ps1` | ver §11 |

## 18. Confirmações

- A VSSDK 18 não foi introduzida, e o manifesto não ganhou alvo Visual Studio 18.
- O comportamento de produção não mudou: nenhum código de WebApi, Domain, Application ou Infrastructure foi tocado, o grafo de produção ficou idêntico e o OpenAPI também (§11).
- Nenhum serviço, imagem, volume, banco, fila ou índice foi alterado.
- A Fase 21 não foi iniciada.
