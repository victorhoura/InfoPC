# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em LOG.md.

| Campo | Valor |
|---|---|
| Última entrega por | claude |
| Data | 2026-11-01 |
| Assunto | Revisão da v1.1.8 do codex e correções da v1.1.9 — **aguarda revisão cruzada do codex** |
| Último commit revisado | `3e216eaa4181bf2b2c531c2ce8a70140150138e0` (claude, 2026-11-01, entrega de estabilidade do codex) |
| Veredito da última revisão | **APROVADO COM RESSALVAS** — [relatório](revisoes/2026-11-01-codex-estabilidade-linux-118.md) |

## Pedido

O Victor pediu ao codex a correção dos travamentos, congelamentos e fechamento no Linux, e depois pediu ao claude para verificar se ficou corrigido. O pedido original também incluía GitHub, Obsidian e o comando de atualização no terminal.

## Resultado da verificação

As correções do codex estão certas e sem regressão encontrada: 277 testes passaram na minha execução, sobre `3e216ea`. Ficaram de fora outros caminhos com o mesmo sintoma, agora corrigidos em `13551d2`.

**A causa do caso real do Victor continua desconhecida.** O relato não tem log, distribuição nem GPU, e nada foi repetido naquele computador.

## O que mudou (`13551d2`)

- **R16-A, reproduzido:** exceção em ação de página (`async void`) encerrava o programa. Falha isolada agora vai para `erros.log` e a janela avisa no alto da página. Falta de memória ou mais de 5 erros em 10 s ainda encerram.
- **R16-B, por leitura:** o stress de RAM media a memória só no início. Agora confere antes de cada bloco e devolve memória abaixo da reserva (5% da RAM, mínimo 512 MB). Os blocos passaram para memória nativa. Vale também para o verificador de estabilidade, que usa o mesmo `StressTester`.
- **R16-C, por leitura:** o `statfs` das montagens saiu da thread da janela em Discos.
- **R16-D, por leitura:** a leitura de `/proc/*/cmdline` saiu da thread da janela em Overlay e Gargalo.
- **R16-E, por leitura:** `Thread.Priority` não vale no Linux; as threads de carga usam nice 10.
- `RunCpu`, `RunSim`, `RunGpu` e o verificador tratam erro comum, sem deixar `Busy` preso. No verificador, erro do programa dá resultado inconclusivo.
- Versão 1.1.9. A edição Windows só mudou o número da versão.

## Verificação

- Linux, no Windows: `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release --nologo --blame-hang-timeout 120s` → **281 passaram, 0 falhas**, 1 min 54 s.
- `dotnet run --project linux/tests/InfoPC.StartupProbe -c Release` → passou.
- Windows: `dotnet build InfoPC.csproj -c Release -nologo --no-incremental -v quiet` → 0 erros, 16 avisos conhecidos.
- `Stress_threads_yield_to_the_desktop_on_linux` não testa nada no Windows: a verificação real é no CI Linux.
- As correções R16-C e R16-D não têm teste próprio: só a suíte de páginas confirma que continuam funcionando, sem unidade de rede caída nem processo em swap.
- `Drivers/`, `LICENSE` e `THIRD-PARTY-NOTICES.txt` intactos; sem dependência nova, banco ou migração.

## Onde o revisor deve olhar

- `CrashLog.CanContinue`: se o limite de 5 erros em 10 s é adequado, e o que acontece com erro repetido em layout ou desenho. Só a contagem foi testada, não um erro real de layout.
- `StressTester.KeepHeadroom`: o índice ao remover blocos durante as passadas e a integridade da verificação depois de reduzir.
- `setpriority` com `who = 0`: vale só para a thread no Linux, e o teste confere isso no CI.
- Decisão de seguir aberto depois de uma exceção: o estado da página pode ficar parcial até reabrir. Considerei melhor que encerrar e abandonar a carga de GPU, mas é mudança em relação à escolha do codex de não recuperar globalmente.

## Limites e pendências

- Repetir no Linux que apresentou o problema (Victor). Se voltar a fechar, os dados úteis são `erros.log`, a saída de `infopc` no terminal e `journalctl -b --no-pager | tail -100`.
- Lista completa do que continua sem cobertura no [relatório](revisoes/2026-11-01-codex-estabilidade-linux-118.md).
- Pendências históricas de hardware, API, RGB/Arduino e limites D-032 a D-034 permanecem na fila.

Comando para instalação pelo clone em ~/InfoPC:

```bash
git -C "$HOME/InfoPC" pull --ff-only && bash "$HOME/InfoPC/linux/instalar.sh" --yes
```
