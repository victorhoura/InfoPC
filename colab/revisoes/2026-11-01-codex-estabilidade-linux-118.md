# Revisão — estabilidade Linux e v1.1.8

| Campo                          | Valor                                                  |
|--------------------------------|--------------------------------------------------------|
| Autor da entrega               | codex                                                  |
| Revisor                        | claude                                                 |
| Data                           | 2026-11-01                                             |
| Intervalo revisado             | `547b845..3e216ea` (`56f08c6`, `58f82a3`, `3e216ea`; `be3b54c` é a revisão anterior) |
| Build/testes rodados pelo revisor | `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release --nologo --blame-hang-timeout 60s` em `3e216ea`, no worktree: **277 passaram, 0 falhas, 2 min 6 s**. Depois das correções do revisor: ver "Verificação das correções". Sem Linux físico nem NVIDIA. Não rodei `InfoPC.exe` nem o binário Linux nesta etapa; a abertura real fica para os runners do workflow Publicar. |
| **Veredito**                   | **APROVADO COM RESSALVAS** |

## Resposta direta ao pedido

O Victor pediu para verificar se o Codex corrigiu os travamentos, os congelamentos e o fechamento no Linux.

- **O que o Codex corrigiu está correto e sem regressão encontrada.** As quatro falhas que ele demonstrou existem no código anterior e não existem mais.
- **Ainda não dá para afirmar que o problema do Victor acabou.** O relato não tem log, distribuição, GPU nem passos. Nada foi repetido no computador que falhou, e o Codex diz isso nos registros dele.
- **A correção não cobria todos os caminhos com o mesmo sintoma.** Encontrei cinco, listados abaixo. Um deles reproduzi com teste; os outros quatro vêm da leitura do código. Corrigi os cinco, porque o pedido original era corrigir e a regra recíproca de 01/11/2026 manda resolver, não só listar.

## O que foi conferido na entrega do Codex

| Item | Como conferi | Resultado |
|---|---|---|
| Painel `_extras` com dois pais ao voltar à GPU | Diff e `GpuPage.Rebuild` | Confirmado: painel novo a cada reconstrução; geração descarta consulta atrasada |
| Mesmo padrão em outras páginas | Busca por campos colocados em contêiner novo fora do construtor | Só os sliders do OC, que ele corrigiu. Nas demais, o pai é reutilizado com `Children.Clear()` |
| Fila de atualizações com a janela ocupada | `Services.StartAsync` e teste | Confirmado: `InvokeAsync` aguardado, no máximo uma atualização pendente |
| Assinante com erro não pula os outros | `NotifyTick` e teste | Confirmado |
| Fechar com stress e overlay | `OnClosed`, `ShutdownMode`, testes | Confirmado: cargas param antes de `Services.Stop` |
| Corrida do `GpuStress` | `token.Register` depois de criar o processo | Confirmado: token já cancelado dispara o `KillTree` na hora |
| Snapshots em Energia e Visão geral | Diff; busca por `S.Hub` dentro de `Task.Run` | Confirmado, sem outro acesso ao hub em worker |
| Diagnóstico antes da plataforma | `CrashLog`, `StartupProbe` | Confirmado: o probe entrou no CI |
| Publicação | `gh`: tag `v1.1.8` = `58f82a3`, pré-lançamento, quatro arquivos, runs `41350090896` e `41350094350` com sucesso | Confirmado |
| Comando de atualização | `instalar.sh`: `--yes` existe e a cópia é por troca atômica | Confirmado |
| `infopc --diagnostico` citado nos registros | `Cli.cs` | Existe |
| Licença, `Drivers/`, dependências novas no pacote | Diff | Nada mudou; `Avalonia.Headless` fica só no probe |

Não repeti os testes vermelhos dele contra o código antigo. Para a GPU e a fila, a leitura do código anterior basta para ver o defeito.

## Achados

Severidade conforme o modelo. Os cinco estão corrigidos pelo revisor em commit próprio e aguardam a revisão do codex.

| # | Severidade | Arquivo:linha | Problema | Correção |
|---|------------|---------------|----------|----------|
| R16-A | **IMPORTANTE** | `linux/src/InfoPC.Linux/CrashLog.cs` | **Reproduzido.** Só parte dos 41 `async void` das páginas tem `try/catch`. Exceção em qualquer outro chega ao laço principal e encerra o programa: é o sintoma "fecha sozinho". O teste `An_error_in_a_page_action_does_not_close_the_program` falhou na 1.1.8 com a pilha `SendOrPostCallbackDispatcherOperation … ManagedDispatcherImpl.RunLoop`. Encerrar também abandona a carga de GPU e seus limites de temperatura: o `vkmark` ou `glmark2` é processo filho e continua rodando. | Falha isolada é registrada e o programa continua, com aviso na página. Falta de memória ou rajada (mais de 5 em 10 s, típica de erro em todo layout) ainda encerra, para não travar repetindo. |
| R16-B | **IMPORTANTE** | `linux/src/InfoPC.Core/Bench/StressTester.cs` | Por leitura. O stress de RAM mede a memória só no início (até 80% da disponível) e nunca mais. Se outro programa crescer, o sistema entra em swap e **congela**; o OOM killer escolhe o maior processo, o próprio InfoPC, que **some sem erro**. É a sequência relatada, mas não há evidência de que o Victor tenha usado esse teste. | Antes de cada bloco, confere a memória e devolve blocos abaixo da reserva (5% da RAM, mínimo 512 MB). Blocos passaram a memória nativa: `free` devolve na hora, e a coleta do GC pausaria a janela nesse momento. Testado com memória simulada. |
| R16-C | **IMPORTANTE** | `linux/src/InfoPC.Linux/Pages/DisksPage.cs:63` | Por leitura. `DiskCollector.Mounts()` roda na thread da janela e faz `statfs` em cada montagem. Unidade de rede caída faz a chamada esperar indefinidamente e **congela a janela**. Só ao abrir Discos, não em uso normal. | Leitura em worker, com o erro tratado na própria página. |
| R16-D | SUGESTÃO | `linux/src/InfoPC.Linux/OverlayWindow.cs:112`, `Pages/BottleneckPage.cs:103` | Por leitura. `GameDetector.Any` lê `/proc/*/cmdline` na thread da janela a cada 3 s. Essa leitura espera o processo lido, e sob pressão de memória isso demora. | Leitura em worker, uma por vez. |
| R16-E | SUGESTÃO | `linux/src/InfoPC.Core/Bench/StressTester.cs:60` | O comentário dizia que `BelowNormal` mantém a interface responsiva, mas o .NET ignora `Thread.Priority` no Linux. A carga disputava os núcleos em pé de igualdade com a janela e o compositor. O efeito não foi medido. | `setpriority` (nice 10) só nas threads de carga. O teste só roda no Linux, no CI. |

Complementos da mesma correção: `RunCpu`, `RunSim`, `RunGpu` e o verificador de estabilidade agora tratam erro comum, para não deixar `Busy` preso nem botão em "PARAR". No verificador, erro do programa dá resultado inconclusivo, não "instável".

## O que continua sem cobertura

- Sinal nativo, falha do driver gráfico e OOM killer não passam pelo `erros.log`. Para esses casos, o que distingue a causa é o journal do Linux.
- Unidade de rede caída ainda pode prender a detecção inicial em worker (`DiskCollector.Disks`): a janela responde, mas os sensores não começam. O Windows separou isso no D-035; no Linux não houve evidência de que valha a mudança.
- `OverlayWindow.Lines` e `OcPage.BuildGpuBox` ainda consultam a NVML na thread da janela. A página Overlay só faz isso com o item `gpu_perfcap` ligado, e o OC ao abrir a página. Não exercitado sem NVIDIA.
- `RunCheck` não cobre o `KernelLog.ErrorsSince` antes do `try`; se falhar, é o tratamento geral que segura e o `Busy` fica preso até fechar.
- Renderização: `UsePlatformDetect` usa os padrões da Avalonia. Falha de GLX com driver específico não foi investigada, por falta de evidência.

## Checklist

- [x] Faz o que a tarefa pedia, sem escopo extra (diagnóstico local está dentro do pedido)
- [x] Build e testes passam (rodados pelo revisor)
- [x] Requisitos portáteis preservados (AGENTS.md, seção 2)
- [x] Licença e créditos preservados
- [x] Sem restos de depuração, código morto ou arquivos temporários
- [x] Diff limpo, sem reformatação desnecessária
- [x] HANDOFF, LOG e TAREFAS atualizados pelo autor

## Correções feitas pelo revisor

O AGENTS.md limita o revisor a correções triviais; a regra global de 01/11/2026 manda corrigir dentro do escopo autorizado, e o pedido original do Victor era corrigir. Segui a regra mais nova e o pedido, em commit separado e com revisão pendente do codex.

## Verificação das correções

Commit `13551d2`. Suíte com **281 testes, 0 falhas** (os 277 anteriores mais 4 novos); `StartupProbe` passou; Windows com 0 erros e 16 avisos conhecidos. R16-A falhou antes e passou depois. R16-C e R16-D não têm teste próprio. O teste de R16-E só exerce algo no Linux do CI.

## Resposta do autor

- (codex responde aqui, achado por achado)
