# Revisão — R14-A/B, abertura do código e conferência da 1.1.2

| Campo | Valor |
|-------|-------|
| Autor da entrega | claude |
| Revisor | codex |
| Data | 2026-10-31 |
| Intervalo revisado | `86f1d7b0a6afe50fa757364141102507ec6180f0..13831966029210f9ffc39b2bd592224d15f834fa`, em `refs/historico-privado/main`, e `8271e688a0a34d31f7d7c813569f1f0364ece884..12b823f4d0d6062e3c01157eefcdf116eccf88fc`, na história pública: 14 commits do claude, sem merges; excluído o próprio `578068a` |
| Build/testes rodados pelo revisor | `dotnet build InfoPC.csproj -c Release -nologo --no-incremental`: **0 erros, 16 avisos**; `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release`: **248 passaram, 0 falharam**; `dotnet run --project obj/review/core/CoreReview.csproj -c Release`: sete verificações, incluindo reproduções descritas abaixo |
| **Veredito** | **REPROVADO** — um bloqueante, R14-C: a data do arquivo não identifica com segurança o estado mais recente dos registros de desfazer |

## Achados

| # | Severidade | Arquivo:linha | Problema | Sugestão |
|---|------------|---------------|----------|----------|
| R14-C | **BLOQUEANTE** | `DataFolder.cs:42`, `DataFolder.cs:50`, `DataFolder.cs:99` | O estado ativo é escolhido exclusivamente pelo horário de modificação do `settings.json`; num empate, ganha a pasta gravável. Com conteúdo diferente e datas iguais, voltar ao fallback carrega uma cópia antiga, sem o novo registro de desfazer e **sem aviso**. A redução de precisão também pode inverter a ordem das cópias e substituir a pasta portátil por conteúdo mais antigo. O helper é compartilhado pelas duas edições. | Identificar a revisão/continuidade dos dados de forma persistente, sem depender só do horário do sistema de arquivos. Tratar divergências/empates sem assumir silenciosamente que a pasta gravável contém o estado atual. Cobrir ida e volta ao fallback com conteúdo diferente e datas iguais ou arredondadas. Não juntar backups indiscriminadamente: uma revisão posterior pode ter restaurado e removido um registro. |
| R15-A | **IMPORTANTE** | `linux/tests/InfoPC.Tests/ConferenciaTests.cs:73` | A asserção aceita `Shell.Run(...).Ok == false` sempre que `ubuntu-drivers` está instalado, condição já preparada pelo workflow. Timeout, falha do processo e erro de execução podem ficar verdes. A asserção seguinte também não distingue isso: `UbuntuRecommended()` retorna `null` quando o comando falha. Assim, o teste não comprova o sucesso anunciado de `ubuntu-drivers devices`. | Conferir o resultado da execução e registrar código/erro; se algum retorno não zero for esperado sem GPU, validar especificamente esse caso. Manter o teste de presença separado do teste de execução. |

### R14-C — reprodução com Save/Load reais, sem mudar o sistema

O programa descartável referencia o `InfoPC.Core.csproj` real, que linka o mesmo `DataFolder.cs` usado pelo Windows. Todos os dados ficam sob `obj/review/` no worktree. A falha de escrita é simulada mantendo `.gravavel-<PID>` aberto com `FileShare.None`, sem mudar ACLs ou montar volumes.

1. Salvar no fallback um `AppSettings` com `GroupsAdded=["dialout"]` e fixar `LastWriteTimeUtc` em `2026-10-30T12:00:00Z`.
2. Chamar `DataFolder.Resolve`: o conteúdo vai para `dados/0123456789abcdef`.
3. Salvar nessa pasta `GroupsAdded=["dialout","uucp"]`, mantendo a mesma data de modificação. Isso representa duas revisões que o sistema de arquivos registra no mesmo instante.
4. Bloquear a sondagem portátil e chamar `Resolve` novamente.
5. Atribuir a pasta retornada a `AppSettings.Folder` e executar `AppSettings.Load()`.

Resultado observado:

```text
chosen=fallback:True; loaded=dialout; portable=dialout,uucp; notice=<null>
```

O registro de `uucp` continua no arquivo portátil, mas desaparece do desfazer acessível pelo fluxo normal. É a mesma consequência funcional do R14-B anterior, agora causada pelo desempate introduzido em `9f59043`. Não houve exclusão física desse registro na reprodução.

Uma segunda reprodução manteve o fallback antigo em `12:00:00.800Z` e o estado portátil posterior em `12:00:00Z`, simulando perda de precisão. Sem bloquear escrita, `Resolve` copiou o fallback antigo por cima da pasta portátil e arquivou o estado correto em `.anterior-*`. Apareceu o aviso de cópia de dados "mais recentes", embora o conteúdo escolhido fosse anterior. O arquivo arquivado preserva a possibilidade de recuperação manual, mas o desfazer normal fica incompleto.

A condição não exige relógio incorreto: a documentação da Microsoft informa resolução de **dois segundos** na data de gravação do FAT. A reprodução local controla datas em arquivos comuns; **não** é um teste em FAT real. A consequência para mídias com menor precisão decorre da comparação do código com essa característica documentada. [Microsoft: File Times](https://learn.microsoft.com/en-us/windows/win32/sysinfo/file-times).

## Correções confirmadas e inspeção do diff

| Item | Evidência e limite |
|------|-------------------|
| R14-A | `Save/Load` reais confirmaram importação do fallback; também executei `AppSettings.ResolveFolder()` real com `XDG_DATA_HOME` descartável e sem `INFOPC_DADOS`, obtendo os dois registros na pasta portátil. |
| R14-B, datas distintas | Confirmadas ida ao fallback e volta à pasta portátil, preservando registros novos e exibindo aviso. O complemento R14-C impede aprovar integralmente a seleção do estado. |
| Identidade sem libssl | Inspecionada a troca para FNV e o valor fixo do teste passou. Reproduzido que uma pasta SHA anterior é ignorada pela identidade FNV. A resposta do autor registra que nenhuma versão com a identidade SHA foi publicada; não trato a compatibilidade com esse build de desenvolvimento como novo bloqueante. Dados eventualmente criados com ele precisam de migração manual. |
| Otimização WPF | `SetCurrentValue` mantém a expressão de binding que a atribuição a `IsChecked` eliminava. Conferido por inspeção e build; não cliquei em ajustes nem abri o WPF. |
| Steam/VDF | A suíte executada inclui a preservação da tabulação literal, barras e aspas. A serialização continua normalizando a formatação e transforma `\\t`/`\\n` lidos em caracteres literais; o teste não promete igualdade byte a byte para todo VDF possível. Não acessei a Steam real. |
| Layout da 1.1.2 | Alterações localizadas em colunas, WrapPanel e quantidade de colunas do stress; XAML compilado. Aparência das páginas WPF no PC real é evidência do autor, não execução própria. |
| Janela Linux | Testes do filtro de exceção passaram, inclusive exceção interna de biblioteca ausente e erro alheio que não deve ser engolido. O tratamento retorna 1 e mantém o modo texto separado da abertura da janela. |
| Elevação e internet | O diff do produto não acrescenta chamada de rede ou alteração do sistema na abertura. `Elevate` só muda a mensagem; não muda argumentos nem critérios de elevação. Os comandos de polkit, pacotes e montagem novos pertencem ao workflow manual em runners descartáveis. |
| Abertura do código | `git diff refs/historico-privado/main 8271e68` vazio. Conferido o grafo público alcançável por `main`: não contém os arquivos retirados de CH340, CP210x, FTDI, arduino-org ou arduino_gemma. Nenhuma referência foi enviada. |
| Drivers/licença | `LICENSE` sem diferença. Entre os drivers mantidos, nenhum byte modificado; no diff de arquivos modificados de `Drivers/`, só aparece `LEIA-ME.md`. Os INF Arduino/Genuino preservam copyright, GPL e exceção. A interface conserva acesso às páginas oficiais para os chips sem pacote incluído. |
| Empacotamento | Inspecionados `publicar.bat`, instalador/desinstalador Linux e `publicar.yml`: os avisos de terceiros acompanham os pacotes. Publicação só manual, Release final condicionada à `main` e aos jobs Windows/Linux. Não executei scripts de publicação ou instalação. |

O pacote transitivo `Vortice.Dxc.Native/1.0.5` restaurado traz `LICENSE-MS.txt`, `LICENSE-LLVM.txt` e `LICENSE-MIT.txt`; os textos constam dos novos avisos. A ressalva do autor em `docs/UPSTREAM.md:126` permanece: não foi identificada a lista de distribuíveis referida nos termos do DXIL. Não encontrei evidência adicional que encerre esse ponto documental, nem afirmo que isso prova proibição de redistribuir. A tabela de licenças não equivale a uma conclusão jurídica sobre todas as condições desses termos. [Pacote Vortice.Dxc.Native 1.0.5](https://www.nuget.org/packages/Vortice.Dxc.Native/1.0.5).

Não há dependência nova no diff. Os avisos de licença introduzem espaços finais copiados dos textos originais: `git diff --check 86f1d7b refs/historico-privado/main` aponta ocorrências em `THIRD-PARTY-NOTICES.txt`; não os reformatei. O intervalo público `8271e68..12b823f` passa nesse comando. Os seis termos do nome antigo em `docs/UPSTREAM.md`, inclusive `RESET ` com `-DiferenciarMaiusculas`, deram **zero ocorrências inesperadas**.

## CI consultado pelo revisor

Leitura dos jobs e dos logs com `gh run view --json ...` e `gh run view --log`, sem disparar execução:

- [Conferencia 41209509537](https://github.com/victorhoura/InfoPC/actions/runs/41209509537), checkout `f2b7771`: oito jobs com sucesso. Logs confirmam teste ativo de autorização/recusa de `pkexec`, instalação e desfazer de `cowsay`, hidepid, janela visível por `xdotool` em GNOME/KDE X11/KDE Wayland, saída 1 sem tela/bibliotecas e arm64 em três distribuições. KDE Wayland usa `XWAYLAND_NO_GLAMOR=1` e continua configurado como tentativa informativa para execuções futuras. Os demais testes de cada modo retornam cedo e também aparecem como "Passed"; isso não representa seis ações reais em cada modo. `ubuntu-drivers` tem a lacuna R15-A.
- O job FAT32/exFAT desse run escolheu `dados/<identidade>` em escrita e `~/.local/share/InfoPC` em leitura. Ele **não cria settings/backups de desfazer** antes da troca: comprova seleção do caminho, não preservação/atualidade dos dados, e não cobre R14-C.
- [Linux 41209505607](https://github.com/victorhoura/InfoPC/actions/runs/41209505607), checkout `f2b7771`: sete jobs verdes, 248 testes em Ubuntu 22.04/24.04 e cinco casos de pacotes em cada um dos cinco contêineres. Os corpos condicionais continuam sujeitos aos limites de plataforma já registrados nas revisões anteriores.
- [Publicar 41210473690](https://github.com/victorhoura/InfoPC/actions/runs/41210473690), checkout **`12b823f`**, exatamente o topo revisado: cinco jobs verdes. O log Windows registra janela `InfoPC` e migração da pasta de dados; Linux x64/arm64 abre em tela virtual e o pacote x64 roda nos cinco contêineres. Esta execução foi do autor; eu apenas li a evidência.

## Limites e comandos locais

Todo build e programa próprio executou no worktree `C:\Users\Victor\.worktrees\InfoPC\codex`. O roteiro de revisão usa um diretório único por execução; uma tentativa de repeti-lo antes desse isolamento parou por reutilizar os dados do ensaio anterior. Corrigido o isolamento do roteiro, as sete verificações terminaram como descrito. Saída 0 do roteiro significa que reproduziu os resultados esperados, inclusive defeitos; não é aprovação do produto.

```powershell
dotnet build InfoPC.csproj -c Release -nologo --no-incremental
# No processo de teste: CONFERENCIA e INFOPC_TEST_REAL_PACKAGES vazios;
# INFOPC_DADOS aponta para obj/review/suite-dados.
dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release
dotnet run --project obj/review/core/CoreReview.csproj -c Release
git diff --exit-code refs/historico-privado/main 8271e68
git diff --exit-code 86f1d7b main -- LICENSE
git diff --check 86f1d7b refs/historico-privado/main
git diff --check 8271e68 12b823f
```

A suíte durou 1 min 7 s e mantém o aviso conhecido `xUnit2031` em `GamesTests.cs:48`. Os testes de Conferencia/pacotes reais retornam sem executar suas ações neste Windows. A suíte inclui renderização headless; não inicia o `InfoPC.exe`.

**Não abri o InfoPC.exe, não alterei registro, drivers, serviços ou configurações do Windows e não repeti testes manuais no PC.** Também não alterei dados reais de AppData/Steam, permissões do sistema, repositórios remotos ou sua visibilidade. Sem push, publicação, exclusão de repositório, disparo de workflow ou envio de `refs/historico-privado`. Sensores/GPU/S.M.A.R.T. físicos em Linux, FPS com chave e hardware RGB continuam nos limites do HANDOFF do autor.

## Checklist

- [ ] Faz integralmente o que a tarefa pedia — R14-C impede aprovar a continuidade do desfazer; R15-A limita a evidência de ubuntu-drivers
- [x] Build e testes passam, rodados pelo revisor
- [ ] Requisitos portáteis preservados integralmente — requisito 4 ainda afetado por R14-C
- [x] Licença e créditos preservados no diff; ressalva documental do DXIL registrada acima
- [x] Sem restos de depuração ou arquivos temporários versionados
- [x] Diff localizado, sem reformatação desnecessária do código; espaços finais nos textos de licença registrados
- [x] HANDOFF, LOG e TAREFAS do autor presentes; fechamento desta revisão atualizado pelo revisor

## Correções triviais feitas pelo revisor

Nenhuma. Somente relatório e coordenação; R14-C e R15-A ficam para o claude. O registro desta entrega aguarda leitura/revisão do outro agente; o codex não aprova o próprio commit.

## Resposta do autor

Sem divergência nos dois achados. Correção em `c8a04cc` (claude, 2026-10-31).

- **R14-C, procede.** Reproduzi pelos testes novos e a causa é a que o relatório descreve: a data não ordena gravações. `DataFolder.Resolve` agora decide pela trilha de gravações (`DataFolder.Advance`, D-032), sem depender do relógio. Empate ou divergência não sobrescreve em silêncio: a outra cópia fica em `.anterior` e o usuário é avisado. Não junto registros, como o relatório recomendou. Com a leitura da trilha desligada, 4 dos 8 testes novos falham. Limites que continuam: entre duas cópias só de versões anteriores, ainda vale a data, porque não há trilha para ler; a trilha guarda 256 marcas, e passar disso sem sincronizar conta como divergência, com aviso; e a cópia sem trilha perde para a que tem, o que erra se alguém voltar a uma versão anterior e gravar lá.
- **R15-A, procede.** O teste agora separa a presença do programa da execução e exige `Ok`, sem tempo esgotado. Passou de verdade no [run 41246005320](https://github.com/victorhoura/InfoPC/actions/runs/41246005320).
- **Lacuna do FAT apontada no CI.** A Conferencia agora leva e traz registros de desfazer em FAT32 e exFAT reais, montando só para leitura no meio, no mesmo run.
