# Estabilidade da edição Linux — 1.1.8

## Pedido e limites da investigação

Em 01/11/2026, Victor relatou travamentos, congelamentos e fechamento espontâneo depois de testar no Linux, e pediu correção, GitHub, Obsidian e comando de atualização. Não foram fornecidos log, distribuição, GPU nem passos exatos. O desenvolvimento desta entrega aconteceu no Windows: as reproduções abaixo usam o aplicativo Avalonia real com renderização headless e hardware por fixtures. Não demonstram que todo fechamento observado no computador do usuário tem a mesma causa.

O commit de partida foi `547b845`. O ajuste documental do claude nesse commit foi lido: distingue o salto histórico de memória no Windows das medições das versões 1.1.6/1.1.7, sem mudança de código. A revisão de sensores Windows até `5ec4d7f` permanece registrada separadamente.

## Causas demonstradas e correções

### Voltar à página da GPU encerra a janela

`GpuPage.Rebuild` montava um novo `liveBox` e adicionava o mesmo `_extras` que ainda era filho do anterior. Remover o cartão externo não remove o vínculo entre seus descendentes. Na segunda visita, Avalonia lançava `InvalidOperationException: The control StackPanel already has a visual parent StackPanel`, na linha que adicionava `_extras`. A exceção vinha de um `async void` e podia encerrar o processo.

O teste `Returning_to_the_gpu_page_does_not_crash_the_window` falhou com essa pilha antes da correção. Cada reconstrução agora cria seu painel de extras, e um número de geração descarta consultas que terminem depois de sair da página ou trocar a seleção. O mesmo padrão nos sliders NVIDIA do Overclock foi corrigido preservando as linhas que já contêm os controles; esse caminho específico não foi executado com driver NVIDIA real.

### A janela ocupada acumula atualizações

O laço coletava sensores e fazia `Dispatcher.Post` a cada segundo sem esperar o consumo. Ao simular a janela ocupada por 3,2 s, o teste `A_busy_window_does_not_accumulate_sensor_updates` recebeu três atualizações seguidas quando a liberou. Cada uma atualizava bindings, gráficos e páginas, prolongando a indisponibilidade em máquinas lentas.

O produtor agora aguarda `Dispatcher.InvokeAsync` e só depois espera um segundo para a próxima rodada. No mesmo teste, a fila fica limitada a uma atualização. A coleta e `DeviceWatcher.Poll` acontecem no worker; os controles são alterados na thread da janela. As consultas de extras e estado suspenso da GPU também passaram para worker, com uma única leitura ativa por página.

### Estado mutável acessado por workers

`EnergyPage` enumerava as linhas do hub em paralelo com `SensorHub.Apply`, que acrescenta linhas. `DashboardPage` consultava papéis de sensores no dicionário que `Apply` limpa e reconstrói. A Energia agora copia as métricas de potência antes de iniciar a consulta de bateria; a Visão geral copia os indicadores de temperatura/RAPL e usa uma explicação estática da ausência de temperatura, sem acessar o dicionário compartilhado no worker. As demais leituras de arquivos continuam em segundo plano.

### Segurança durante falha de página e fechamento

Uma revisão técnica auxiliar, somente leitura, encontrou dois efeitos do ciclo de atualização/encerramento que precisavam ser cobertos:

- Uma exceção síncrona no primeiro assinante de `Tick` pulava os seguintes, inclusive a proteção do stress. `A_failing_page_does_not_skip_the_other_sensor_subscribers` demonstrou a falha; `NotifyTick` agora registra o erro de cada assinante e continua com os outros.
- Com overlay aberto e lifetime `OnLastWindowClose`, fechar a principal podia deixar cargas rodando depois de cancelar os sensores. `Closing_the_main_window_stops_stress_even_with_an_overlay_open` demonstrou que a carga permanecia ativa. O lifetime agora é `OnMainWindowClose`; `OnClosed` é chamado para todas as páginas, inclusive ocultas, antes de `Services.Stop`. O stress para CPU/RAM/GPU; benchmarks e OC são cancelados; overlay e integração são encerrados. Navegar entre páginas continua sem interromper o stress.

Em `GpuStress`, o token passou a registrar `KillTree` imediatamente após criar o processo, inclusive se o cancelamento ocorreu entre o teste do laço e a atribuição de `_proc`. Os handles de cada rodada são liberados. Isso fecha uma corrida preexistente que poderia deixar um processo de carga aberto no encerramento. Os limites de temperatura e duração não foram afrouxados.

## Diagnóstico local

`CrashLog` grava contexto, versão, data e pilha em `erros.log` na pasta de dados já escolhida pelo aplicativo; faz rotação para `erros.log.anterior` e limita o tamanho das entradas. Testes verificam rotação e pasta sem permissão. Não há pacote novo, serviço, telemetria ou envio do registro. Os handlers globais registram exceções fatais, sem marcar a interface inteira como recuperada; falhas protegidas nas consultas são tratadas no seu ponto de origem.

Um sinal nativo, falha do driver ou encerramento pelo OOM killer pode não produzir esse arquivo. Para nova ocorrência, a saída de `infopc` no terminal, `infopc --diagnostico`, o registro local e o journal do Linux distinguem esses casos. A confirmação no hardware que apresentou o problema continua pendente.

## Verificação e publicação

Os testes vermelhos acima foram executados contra os caminhos defeituosos antes das respectivas correções. A suíte também percorre todas as 21 páginas duas vezes em cada fixture Intel, AMD e ARM, desenha cada tela, repete a entrada na GPU e simula navegação rápida enquanto a consulta está pendente. Há cobertura anterior de stress em segundo plano, limites térmicos e comandos de GPU por processos de teste.

Comandos finais, contagens, hashes, workflows e publicação ficam em `colab/HANDOFF.md` e `colab/LOG.md`. A análise auxiliar não equivale à revisão cruzada: a entrega do codex aguarda o claude. Não há banco de dados, migração, website nem serviço online a implantar; a publicação correspondente é a Release com os executáveis portáteis.

Referência da implementação de dispatcher usada pelo projeto: [Avalonia 11.3.22 — DispatcherOperation](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Base/Threading/DispatcherOperation.cs). Documentação de apoio: [thread da interface na Avalonia 11](https://v11.docs.avaloniaui.net/docs/guides/development-guides/accessing-the-ui-thread/).
