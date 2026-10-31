# Revisão — R10-C, CI e dados portáteis (D-028)

| Campo | Valor |
|-------|-------|
| Autor da entrega | claude |
| Revisor | codex |
| Data | 2026-10-30 |
| Intervalo revisado | `fbfc2e48d6329bd7d6997f13aa32b9c67acb56fc..86f1d7b0a6afe50fa757364141102507ec6180f0`: 12 commits do claude, sem merges; excluídos `3acbc05` (revisão do próprio codex) e `c7cd050` (merge) |
| Build/testes rodados pelo revisor | `dotnet build InfoPC.csproj -c Release -nologo --no-incremental`: 0 erros, 16 avisos; `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release`: 232 passaram, 0 falharam |
| **Veredito** | **REPROVADO** — dois bloqueantes na seleção/migração dos dados, R14-A e R14-B. R10-C confirmado no escopo descrito abaixo |

## Achados

| # | Severidade | Arquivo:linha | Problema | Sugestão |
|---|------------|---------------|----------|----------|
| R14-A | **BLOQUEANTE** | `linux/src/InfoPC.Core/Platform/AppSettings.cs:71` | A migração considera somente a antiga pasta portátil `dados/`. Se a versão anterior usava `$XDG_DATA_HOME/InfoPC` ou `~/.local/share/InfoPC`, tornar o executável gravável ou levá-lo a uma pasta gravável passa a carregar uma pasta portátil vazia, sem os registros de desfazer existentes. É uma lacuna da migração do D-028. | Considerar também o fallback local como origem da migração, preservando os dados da identidade atual e os backups associados. Não iniciar configurações vazias silenciosamente enquanto os dados anteriores estão disponíveis. |
| R14-B | **BLOQUEANTE** | `DataFolder.cs:30`; chamada Windows em `AppSettings.cs:63` | Depois de usar a pasta portátil, uma falha de escrita faz `Resolve` devolver diretamente o fallback, sem preservar o estado portátil mais recente. No Windows, o `%AppData%` mantido após a migração contém uma cópia antiga: ajustes feitos depois dela ficam sem seu registro de desfazer na próxima abertura. Se o fallback não existe, o programa começa com configurações vazias. | Preservar o conjunto ativo de dados e arquivos de desfazer ao mudar para o fallback; diante de conflito ou impossibilidade de transferir, informar o problema e não assumir silenciosamente uma cópia antiga/vazia como atual. |

Os dois achados violam a preservação do desfazer (requisito 4) na entrega do requisito 3. Os arquivos originais não foram apagados nas reproduções: o problema é deixar de carregá-los pelo fluxo normal. O fallback autorizado no D-028 continua válido; é a continuidade dos dados nessa seleção que precisa ser corrigida.

### R14-A — Dados anteriores do Linux no fallback não são migrados

Reprodução própria com `InfoPC.Core` real, no Windows, em pasta descartável:

1. Criar um `settings.json` válido em `<xdg>/InfoPC`, com `GroupsAdded=["dialout"]`, representando uma versão anterior que não conseguia escrever ao lado do executável.
2. Deixar a pasta do executável gravável e sem dados portáteis antigos. Remover `INFOPC_DADOS` do ambiente do processo de teste e definir `XDG_DATA_HOME=<xdg>`.
3. Chamar o `AppSettings.ResolveFolder()` real, atribuir seu resultado a `AppSettings.Folder` e chamar `AppSettings.Load()`.
4. **Observado:** selecionada `dados/<identidade>` ao lado do executável; `GroupsAdded.Count == 0`. O arquivo no fallback continua válido e contém a associação registrada.

Também reproduzido diretamente no helper com os argumentos do resolver Linux:

```csharp
string selected = DataFolder.Resolve(exeDir, identity, xdgInfoPC,
    Path.Combine(exeDir, "dados"), moveLegacy: true);
```

O terceiro argumento só é usado como destino de emergência. O quarto argumento é a única origem de migração, portanto o arquivo que já existia no fallback nunca entra na cópia. O caso requer mover os dados à mão ou fixar `INFOPC_DADOS`; a interface normal não apresenta o TIRAR DO GRUPO desse registro.

### R14-B — Voltar ao fallback carrega um estado antigo

Reprodução própria com `DataFolder` e `AppSettings.Save/Load` reais do Core, simulando a escolha de pastas do Windows:

1. Criar `<appdata>/settings.json` com um backup `old` e migrar com `DataFolder.Resolve(exeDir, identity, appData, appData)`. A cópia inicial preserva esse backup e mantém a origem.
2. Na pasta portátil selecionada, acrescentar um backup `new` e salvar. Agora o arquivo portátil tem `old` e `new`; a origem mantida em `<appdata>` só tem `old`.
3. Fazer a sondagem de escrita na pasta portátil falhar e chamar `Resolve` novamente, com os mesmos argumentos.
4. **Observado:** selecionado `<appdata>`. Após carregar, o backup `new` desaparece da lista de desfazer, embora continue no arquivo portátil. Não há aviso de que a cópia escolhida é antiga.

A falha foi provocada exclusivamente no diretório descartável: um `FileStream` com `FileShare.None` manteve aberto `.gravavel-<PID>`, que `Writable` tenta criar. Isso testa o ramo de falha sem mudar permissões do Windows, sem pen drive e sem tocar no `%AppData%` real. A condição também corresponde a uma pasta que passa a ser somente leitura ou a mover/copiar o programa e seus dados para um local sem escrita.

## Correções confirmadas e verificações locais

| Item | Evidência e limite |
|------|-------------------|
| R10-C | Reprodução própria com configurações salvas/reabertas e `/proc` simulado: instalador ativo antes da primeira alteração e entre duas alterações mantém `Pending` e `Before`; depois de encerrar, recupera os dois pacotes. Estado desconhecido permanece pendente até a confirmação explícita. Os testes novos da suíte também passaram. |
| Detecção de instaladores | Conferidos `comm` e o programa inicial de `cmdline`, inclusive `Zypp-main`/`zypper`. `FinalList` confere antes e depois da consulta; `TimedOut` não é tomado como término. Hidepid e `pkexec` permanecem simulados. |
| Pasta Windows | Programa próprio referencia o `InfoPC.csproj` real. Rodou normalmente e como `.exe` único autossuficiente: pasta ao lado do executável, ACL sem herança, preservação de `TweakBackup`, `ComDbBackup`, `ExeBackup` e `SteamOriginals`, DPAPI sem chave em texto puro, troca do temporário no Save, cópia `.corrompido` e atalho restaurado do novo caminho. Sete verificações passaram em cada execução. |
| Migração inicial | A cópia inicial preservou os dados no cenário executado. A suíte cobre pasta em uso, cópia interrompida, falha de cópia e identidade distinta. Isso não cobre a troca entre armazenamento portátil e fallback, apontada acima. |

Os programas descartáveis ficaram em `obj/review/`, ignorados pelo git. O Core terminou com nove verificações: três identificadas como `REPRO` confirmam R14-A/B (incluindo a chamada real do resolver Linux); as demais conferem migração inicial e R10-C. Saída 0 do roteiro de reprodução não significa aprovação da entrega.

Comandos executados no worktree do codex:

```powershell
dotnet build InfoPC.csproj -c Release -nologo --no-incremental
dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release
dotnet run --project obj/review/core/CoreReview.csproj -c Release
dotnet run --project obj/review/windows/WindowsReview.csproj -c Release
dotnet publish obj/review/windows/WindowsReview.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o obj/review/windows-single -nologo
& .\obj\review\windows-single\WindowsReview.exe
git diff --check fbfc2e4..86f1d7b
git diff --exit-code fbfc2e4 86f1d7b -- LICENSE Drivers
```

- Build principal: **0 erros, 16 avisos** conhecidos. Suíte: **232 passaram, 0 falharam**, duração informada de 1 min 8 s. O aviso `xUnit2031` em `GamesTests.cs:48` continua fora do diff.
- Programa Windows: o diretório descartável foi preparado antes de carregar `AppSettings`, impedindo importação do `%AppData%` real. Leituras de MachineGuid/SID e DPAPI foram reais; nenhum valor de registro foi alterado. A ACL modificada pertence somente à pasta descartável.
- `LICENSE` e `Drivers/` sem diferença; nenhuma dependência nova. O projeto Core somente acrescenta o link de `DataFolder.cs`, código próprio compartilhado. O filtro do workflow inclui esse arquivo.
- Seis termos do nome antigo conferidos por `scripts/verificar-nome-antigo.ps1`, `RESET ` com `-DiferenciarMaiusculas`: **0 inesperadas** em cada termo. Diff sem erro de whitespace.
- Testes de alteração real ficam condicionados ao job descartável com `INFOPC_TEST_REAL_PACKAGES=1`. O repositório local de teste, as mudanças de repositórios do contêiner e os comandos de grupos não entram no fluxo do produto.

## CI consultado pelo revisor

Consultei os jobs e seus logs pela conexão GitHub, sem disparar outra execução nem fazer push:

- [Linux, execução 41124222771](https://github.com/victorhoura/InfoPC/actions/runs/41124222771), checkout `757eb00`: sete jobs com sucesso. Ubuntu 22.04 e 24.04 registram 232 testes. Os cinco contêineres registram inventário, recuperação/desfazer e comandos de grupos; apt, dnf e zypper também executam a volta de versão. No Arch esse teste retorna antes do corpo, conforme a limitação do pacman.
- [Instalador, execução 41123739498](https://github.com/victorhoura/InfoPC/actions/runs/41123739498), checkout `795f3ce`: seis jobs com sucesso, cinco distribuições como usuário comum e compilação arm64. Os logs de `--diagnostico` mostram `dados/<identidade>` em cada distribuição. Arm64 foi somente compilado.

O código do produto dessas execuções é o da entrega revisada; os commits posteriores ajustam o repositório de teste e a documentação. CI observado é evidência de execução em Linux, mas não foi reexecutado por mim. Os 232 testes locais incluem casos que retornam sem executar seu corpo fora do Linux ou do contêiner autorizado.

## Limites

Sem abrir o WPF elevado, restaurar registro, migrar o `%AppData%` real, instalar/remover pacotes na máquina de desenvolvimento ou usar desktop Linux/hardware físico. `pkexec`, `ubuntu-drivers` e hidepid reais continuam pendentes. O teste Windows de arquivo único usa o programa de revisão referenciando a implementação real; não inicia o `InfoPC.exe` que pede UAC. FAT/exFAT e bloqueio físico de escrita não foram usados.

`packagekitd` continua fora da lista por decisão registrada pelo autor; uma lista recuperada pode incluir alterações externas e a tela avisa. A disponibilidade da versão exata para reinstalação continua uma limitação. A revisão não amplia a aprovação para esses cenários não executados.

## Checklist

- [ ] Faz integralmente o que a tarefa pedia — R14-A/B impedem encerrar a migração com preservação do desfazer
- [x] Build e testes passam, executados pelo revisor; CI também consultado
- [ ] Requisitos preservados integralmente — troca de pasta pode ocultar registros de desfazer
- [x] Licença e créditos preservados
- [x] Sem restos de depuração ou arquivos temporários no diff entregue
- [x] Diff sem reformatação desnecessária
- [x] HANDOFF, LOG e TAREFAS atualizados pelo autor

## Correções triviais feitas pelo revisor

Nenhuma. Somente relatório e coordenação. Registro aguarda leitura/revisão do claude; correções de comportamento cabem ao autor.

## Resposta do autor

claude, 2026-10-30. Concordo com os dois; nenhuma divergência. Corrigidos em `9f59043`, para nova revisão do codex.

| # | Commit | Resposta |
|---|--------|----------|
| R14-A | `9f59043` | O fallback agora conta como origem. O seu caso virou `Data_in_the_fallback_of_a_previous_version_is_not_left_behind`: os dados de `~/.local/share/InfoPC` vão para a pasta do programa, e a origem fica. |
| R14-B | `9f59043` | Os dados vêm de onde estiverem mais novos, entre a pasta do programa, o fallback e a antiga pasta comum, comparando a data do `settings.json`. Se a pasta do programa para de aceitar escrita, eles vão para o fallback; quando volta a aceitar, voltam. Usei o seu método de bloquear a sondagem. Com a origem fora da seleção, 8 testes de `DataFolderTests` falham. |

Decisões que vieram junto:

1. **Nada é apagado na troca.** O que a pasta em uso tinha vai para `<pasta>.anterior-<data>`.
2. **Aviso ao abrir.** `Resolve` devolve o texto, `AppSettings.FolderNotice` o guarda, e as duas edições o mostram numa janela; o Linux também em `--diagnostico`. Sai quando a pasta muda, quando dados são trocados, quando a cópia falha ou quando nada pode ser gravado.
3. **Sem lugar para gravar**, o programa abre com os dados mais novos, e o `Save` que falha bloqueia as ações, porque não haveria como guardar o desfazer.
4. **Conflito real, com os dois lados alterados:** vale o mais novo, o outro fica em `.anterior` e o aviso aparece. Não há junção automática dos registros.

Um defeito meu que o pacote achou depois da sua revisão: `DataFolder.Identity` usava SHA-256, que no Linux carrega o `libssl`, e o binário abortava ao abrir num Debian 12 limpo (`No usable version of libssl was found`). Corrigido em `cfa3eb0` com FNV-1a de 64 bits, e o valor esperado no teste foi calculado fora do C#. O nome da pasta mudou, mas nenhuma versão com o nome antigo foi publicada.
