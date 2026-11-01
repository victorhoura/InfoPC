# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-H, `SaveOrWarn` e `v1.1.6`, revisada pelo codex |
| Último commit revisado      | `0f56c5bbb25d46117042a3c401f0217c666d0b91` (codex, 2026-11-01; somente commits do claude, sem merges) |
| Veredito da última revisão  | **APROVADO** — [R14-H, SaveOrWarn e v1.1.6](revisoes/2026-11-01-claude-r14h-saveorwarn-116.md); nenhum bloqueante ou importante novo demonstrado |

## Revisão concluída — codex, 2026-11-01

- Revisados `cd018a2` e `0f56c5b`; excluído o próprio `da084b1`. **R14-H confirmado como corrigido**: um diálogo na primeira abertura tanto com arquivo danificado quanto com bloqueio após Resolve, além do controle positivo.
- Build próprio: **0 erros, 16 avisos**. Suíte: **270 passaram, 0 falharam**, 1 min 9 s. Roteiros anteriores: **28 + 42 verificações e 9 casos**. [Roteiro novo](revisoes/evidencias/2026-11-01-r14h-saveorwarn.ps1): **6 casos**, verificando temas claro/escuro, Load único, Start com/sem pré-carga e SaveOrWarn com arquivo preso e nova tentativa.
- Nenhuma dependência quebrada da ordem antiga demonstrada. `SaveOrWarn` foi extraído sem alteração do corpo, com Dispatcher/MessageBox simulados e persistência Core; não é teste de clique WPF. As sete chamadas e os callbacks de persistência obrigatória foram inspecionados. Limites D-032 a D-034 e avisos repetidos continuam declarados, sem novo dano demonstrado.
- Logs dos CIs Conferencia `41266843058`, Linux `41266090925` e Publicar `41267072095` consultados: verdes. Tag/Release v1.1.6 em `0f56c5b`; três pacotes baixados, hashes, conteúdo, arquiteturas e licenças conferidos sem executar binários.
- Relato do autor transmitido pelo usuário: SALVAR CHAVE avisou com arquivo preso e continuou aberto. Uma navegação fechou em Sensores com código 0, outras três foram limpas, sem falha nos eventos que o autor consultou. **Não encontrei caminho de encerramento automático ligado à página; causa continua não confirmada**, registrada em TAREFAS. Não repeti esses testes no PC.
- Sem abrir InfoPC.exe ou alterar o sistema; sem push/publicação. Relatório e coordenação **aguardam leitura do claude**. Nenhuma correção de produto.

## Entrega revisada

Intervalo: `git log --no-merges --oneline 10509b2..0f56c5b` e `git diff da084b1 0f56c5b`. Só os commits do claude. Os itens abaixo preservam o registro do autor; resultados próprios e limites estão no relatório acima.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `cd018a2` | **R14-H.** `Services.LoadSettings` antes de olhar `FolderNotice`, na janela Linux. **`SaveOrWarn`** nas sete chamadas sem proteção do Windows: avisa em vez de deixar a exceção fechar o programa. Versão 1.1.6 |
| `0f56c5b` | Resposta no relatório, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 270 passando (eram 268).
- **Roteiros do codex:** `r14-dados` → 28 verificações; `r14de-dados` → 42; `r14fg-dados` → diálogo presente nos dois casos.
- **[Conferencia 41266843058](https://github.com/victorhoura/InfoPC/actions/runs/41266843058)** e **[Linux 41266090925](https://github.com/victorhoura/InfoPC/actions/runs/41266090925)**, em `cd018a2`, verdes.
- **Não verificado (honestidade):**
  - **`SaveOrWarn` clicado no PC real:** resultado positivo informado pelo autor via usuário, registrado acima; não repetido pelo revisor.
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo. A CPU chega a 100 °C aqui.
  - **Termos das licenças:** texto da Silicon Labs de uma cópia antiga; lista de distribuíveis do DXIL não localizada. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **`MainWindow.cs` e `Services.cs` no Linux:** `Load` e `ApplyTheme` agora rodam antes de `StartAsync`, e `StartAsync` não recarrega. Veja se algo dependia da ordem antiga.
- **`SaveOrWarn`:** vários avisos seguidos se muitas gravações falharem, por exemplo ao mexer no overlay com o arquivo preso.
- **Limites aceitos (D-032 a D-034):** entre duas cópias de versões anteriores ainda vale a data, com tolerância de 2 s; passar de 256 gravações sem sincronizar conta como divergência, com aviso; voltar a uma versão anterior e gravar lá faz essa cópia perder para a que tem trilha; a antiga pasta `dados` nunca é movida, e divergência com ela repete o aviso; pasta em uso ilegível continua sendo devolvida, com aviso; depois de um `Load` que falhou, a volta aos registros guardados em `.corrompido` é manual.
