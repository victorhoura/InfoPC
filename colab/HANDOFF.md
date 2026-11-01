# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-H, `SaveOrWarn` e `v1.1.6`. **Aguarda revisão do codex** |
| Último commit revisado      | `10509b2866b2e6a57a2386d2bdde1793dd51f840` (codex; relatório `da084b1`, lido pelo claude) |
| Veredito da última revisão  | **APROVADO COM RESSALVAS** — [correção de R14-F/G e v1.1.5](revisoes/2026-10-31-claude-r14fg-115.md): R14-H, tratado aqui, com a resposta no relatório |

## O que revisar

`git log --no-merges --oneline 10509b2..main` e `git diff da084b1 main`. Só os commits do claude; `da084b1` é o relatório do próprio codex.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `cd018a2` | **R14-H.** `Services.LoadSettings` antes de olhar `FolderNotice`, na janela Linux. **`SaveOrWarn`** nas sete chamadas sem proteção do Windows: avisa em vez de deixar a exceção fechar o programa. Versão 1.1.6 |
| este | Resposta no relatório, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 270 passando (eram 268).
- **Roteiros do codex:** `r14-dados` → 28 verificações; `r14de-dados` → 42; `r14fg-dados` → diálogo presente nos dois casos.
- **[Conferencia 41266843058](https://github.com/victorhoura/InfoPC/actions/runs/41266843058)** e **[Linux 41266090925](https://github.com/victorhoura/InfoPC/actions/runs/41266090925)**, em `cd018a2`, verdes.
- **Não verificado (honestidade):**
  - **`SaveOrWarn` clicado no PC real:** ver o resultado no fim do LOG, se já houver; a lógica é um `try` em volta do `Save`.
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo. A CPU chega a 100 °C aqui.
  - **Termos das licenças:** texto da Silicon Labs de uma cópia antiga; lista de distribuíveis do DXIL não localizada. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **`MainWindow.cs` e `Services.cs` no Linux:** `Load` e `ApplyTheme` agora rodam antes de `StartAsync`, e `StartAsync` não recarrega. Veja se algo dependia da ordem antiga.
- **`SaveOrWarn`:** vários avisos seguidos se muitas gravações falharem, por exemplo ao mexer no overlay com o arquivo preso.
- **Limites aceitos (D-032 a D-034):** entre duas cópias de versões anteriores ainda vale a data, com tolerância de 2 s; passar de 256 gravações sem sincronizar conta como divergência, com aviso; voltar a uma versão anterior e gravar lá faz essa cópia perder para a que tem trilha; a antiga pasta `dados` nunca é movida, e divergência com ela repete o aviso; pasta em uso ilegível continua sendo devolvida, com aviso; depois de um `Load` que falhou, a volta aos registros guardados em `.corrompido` é manual.
- **Repositório antigo:** `victorhoura/InfoPC-antigo` só sai com a permissão `delete_repo`, que depende de o usuário confirmar no navegador.
