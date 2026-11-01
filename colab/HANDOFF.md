# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em LOG.md.

| Campo | Valor |
|---|---|
| Última entrega por | codex |
| Data | 2026-11-01 |
| Assunto | Estabilidade Linux e v1.1.8 — **aguarda revisão cruzada do claude** |
| Último commit revisado | `547b845a6c628720df2945b5da68a641fcd02ed2` (codex, 2026-11-01, ajuste documental do claude) |
| Veredito da última revisão | **APROVADO** — [ajuste de memória](revisoes/2026-11-01-claude-memoria-documentacao.md); sensores/v1.1.7 no [relatório anterior](revisoes/2026-11-01-claude-sensores-117.md) |

## Pedido e diagnóstico

Victor testou no Linux e relatou travamentos, congelamentos e fechamento sozinho. Pediu correção, GitHub, Obsidian e comando de atualização. Sem log, distribuição, GPU ou sequência de cliques no relato, não se atribui todo o ocorrido a uma causa única.

Reproduções antes das respectivas correções:

- Voltar à página GPU: InvalidOperationException de controle ainda com pai visual anterior, em GpuPage.Rebuild; erro de async void capaz de encerrar o app.
- Janela ocupada por 3,2 s: três atualizações de sensores em rajada; o produtor não aguardava a interface.
- Assinante de Tick com exceção: o próximo monitor não era chamado.
- Fechar principal com stress/overlay: a carga permanecia ativa quando os sensores já seriam cancelados.

## O que mudou

- GPU: painel novo de extras por reconstrução, geração para descartar consultas atrasadas e consultas ao driver em worker, sem sobreposição. Sliders do OC preservam suas linhas para não ganhar dois pais.
- Sensores: coleta e detecção de dispositivos em worker, aguardo do dispatcher e cancelamento ao fechar. Erro síncrono de uma página não pula a segurança dos outros assinantes.
- Energia/Visão geral: snapshots na UI evitam ler a lista/dicionário mutáveis do hub no worker.
- Encerramento: a principal controla o lifetime; todas as páginas recebem OnClosed antes de Services.Stop, inclusive ocultas. Stress CPU/RAM/GPU para, benchmarks/OC são cancelados, overlay e integração fecham. GpuStress vincula o processo ao token mesmo na corrida Start/Stop.
- Diagnóstico: erros.log local e limitado, com rotação, sem envio e sem mascarar erro fatal globalmente.
- Versão comum 1.1.8; [investigação detalhada](../docs/LINUX-ESTABILIDADE.md), notas e comando de atualização.

## Verificação

Suíte final repetida após o ajuste de inicialização: **277 testes passaram, 0 falhas**, em 1 min 53 s, no Windows com Avalonia/Skia headless. `dotnet run --project linux/tests/InfoPC.StartupProbe -c Release` também passou. O aviso xUnit2031 de GamesTests já existia.

- Windows: `dotnet build InfoPC.csproj -c Release -nologo --no-incremental -v quiet` → 0 erros, 16 avisos conhecidos, versão 1.1.8.
- Linux: `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release --nologo --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=linux-118.trx' --blame-hang-timeout 60s`.
- As 21 páginas são percorridas duas vezes por fixture Intel, AMD e ARM, com render; regressões incluem retorno/saída rápida da GPU, fila limitada, assinante falho e fechamento com stress/overlay.
- Nome antigo: seis termos, 0 ocorrências inesperadas. git diff --check limpo. Drivers/, LICENSE e avisos intactos; nenhuma dependência nova, banco ou migração. Windows só mudou de versão.

## Publicação e revisão

A tentativa inicial `41349512015` em `56f08c6` foi bloqueada nos dois pacotes Linux: o novo diagnóstico acessava o dispatcher antes de existir plataforma. O probe em processo separado reproduziu `SupportsRunLoops == false`; a inscrição de UI foi movida para `OnFrameworkInitializationCompleted`. `InfoPC.StartupProbe` passou depois da correção e agora é exigido pelo CI. O rascunho falho não foi publicado. A suíte headless comum, que já monta a plataforma antes dos testes, não cobria essa ordem de entrada.

Push e Release 1.1.8 serão executados depois da integração e conferidos por jobs, hashes e pacotes; a evidência final entra em nova entrega documental. Não há website ou serviço online a implantar. O repositório é público e a história privada continua só local; push exclusivamente de main.

Revisão técnica auxiliar somente leitura encontrou os cuidados de Tick/fechamento/snapshot e confirmou o diff corrigido, sem bloqueador remanescente demonstrado; sem build/testes pelo auxiliar. **Não substitui a revisão cruzada do claude.**

O revisor deve conferir descarte por geração, cancelamento enquanto detecção/benchmark termina, ordem OnClosed/Stop, GpuStress e segurança do stress. Navegar normalmente continua mantendo o stress ativo; os limites térmicos não mudaram.

## Limites e pendências

- Repetir no Linux que apresentou o problema. NVIDIA, S.M.A.R.T. e drivers físicos não foram exercitados neste Windows; Linux real no CI e tela virtual validarão os pacotes.
- Sinais nativos, falhas de driver e OOM killer podem não passar pelo log gerenciado.
- Pendências históricas de hardware, API, RGB/Arduino e limites D-032 a D-034 permanecem na fila.

Comando para instalação pelo clone em ~/InfoPC, após fechar a janela:

```bash
git -C "$HOME/InfoPC" pull --ff-only && bash "$HOME/InfoPC/linux/instalar.sh" --yes
```
