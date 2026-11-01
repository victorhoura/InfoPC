# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-11-01                                                  |
| Assunto                     | Sensores em etapas, campos em branco e `v1.1.7`. **Revisão concluída; aguarda leitura do claude** |
| Último commit revisado      | `5ec4d7fba13d1f8a321b4891246e930f996d4794` (codex, 2026-11-01) |
| Veredito da última revisão  | **APROVADO** — [Sensores em etapas, campos em branco e v1.1.7](revisoes/2026-11-01-claude-sensores-117.md) |

## Revisão cruzada concluída

Codex: nenhum bloqueante ou importante novo demonstrado. Build próprio Windows com 0 erros e 16 avisos; 270 testes Linux no Windows e 13 verificações sintéticas contra o assembly do produto passaram. Conferidos a implementação da biblioteca no NuGet, os chamadores, a tag/Release, os logs de Publicar/Linux e os três pacotes da v1.1.7. Sem abrir o InfoPC ou carregar sensores deste PC. Concorrência real de hardware e AMD físico não foram exercitados pelo revisor; riscos e limites estão no relatório. Esta entrega de revisão aguarda leitura do claude, sem push pelo codex.

## Escopo da revisão concluída

`git log --no-merges --oneline 0f56c5b..main`. Só os commits do claude: `454eff9` é o relatório do próprio codex, e `86e2976` só registra a exclusão do repositório antigo.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

O usuário pediu para continuar até o fim. Ele apagou `victorhoura/InfoPC-antigo`, e sobraram na lista três defeitos de software, que já vinham do original.

- **Sensores em etapas (D-035):** `SensorMonitor` abre primeiro o que é rápido, e memória e discos em segundo plano, em objetos `Computer` separados. As categorias novas entram pelo timer (`AddNewSensorChips`).
- **Campos em branco:** a biblioteca chama os núcleos de `CPU Core #1`, e o código procurava `Core #`. `CpuCoreClocks` aceita os dois, nos três lugares. O Gargalo descartava as linhas `Thread`, as únicas com SMT.
- **Correção de registro:** os "92 °C em repouso" dos registros anteriores não eram repouso.

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 270 passando. A edição Linux só mudou de versão.
- **PC real, com o build deste commit, dirigido por UIAutomation:** janela em 1,5 s; primeiro sensor em 2,7 s (eram 74,8 s); 133 sensores aos 5 s, 211 aos 11 s (memória), 222 aos 75 s (discos); janela respondendo o tempo todo; categorias novas Timing e Fator apareceram sozinhas. Clock da CPU na Visão geral, "núcleo 6" no Gargalo, temperatura do disco na página Discos.
- **Duas instâncias de `Computer` ao mesmo tempo:** roteiro próprio com a mesma biblioteca: 62 leituras da CPU durante a abertura dos discos, 0 nulas e 0 erros.
- **Temperatura, 60 s em uso leve (carga de 17%):** CPU mínima 58 °C, média 63,5 °C, máxima 73 °C.
- **Não verificado (honestidade):**
  - **CPU AMD:** o nome `Core #` foi mantido por compatibilidade, sem testar; os nomes de hoje vêm da biblioteca lida no i5-10400.
  - **Fechamento durante a detecção (atualização recebida na revisão):** segundo evidência do autor transmitida pelo usuário, a v1.1.7 fechou aos 3, 15 e 40 s em menos de 1 s, código 0. O registro original só cobria depois dos 110 s. Codex não repetiu em hardware real.
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo.
  - **Fechamento em Sensores:** uma passada da v1.1.6 terminou com código 0, sem causa confirmada; não voltou a acontecer.

## Riscos e onde olhar com mais atenção

- **Duas instâncias de `Computer`:** a biblioteca tem estado estático (mutex, driver PawnIO). `Close` de uma pode afetar a outra; só acontece ao fechar o programa. Veja `Dispose` e fechar durante a detecção.
- **`Refresh` na thread da janela:** a primeira leitura dos discos levou 1,9 s isolada e pode dar um engasgo único. Já era assim, só que dentro dos 75 s.
- **Quem contava com tudo pronto depois de `OpenAsync`:** `DiskHealth` e a RAM do Gargalo ficam sem esses dados até a etapa chegar, como antes.
- **Stress e OC:** o corte por temperatura da CPU agora vale desde os primeiros segundos; antes de 75 s, o programa dizia que não havia sensor.
- **Limites aceitos (D-032 a D-034):** continuam como no HANDOFF anterior.
