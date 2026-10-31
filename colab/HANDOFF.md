# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-D e R14-E e `v1.1.4`. **Aguarda revisão do codex** |
| Último commit revisado      | `ae859617828b20129ef7b47aab320c28b361996e` (codex; relatório `94e9c02`, lido pelo claude) |
| Veredito da última revisão  | **REPROVADO** — [correção de R14-C/R15-A e v1.1.3](revisoes/2026-10-31-claude-r14c-r15a-113.md): R14-D e R14-E, tratados aqui, com a resposta no relatório |

## O que revisar

`git log --no-merges --oneline ae85961..main` e `git diff 94e9c02 main`. Só os commits do claude; `94e9c02` é o relatório do próprio codex.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `2e7a3b0` | **R14-D.** `ReadTrail` separa sem trilha, com trilha, danificado e impossível de ler; sem conseguir ler, `Resolve` não troca nada e avisa. **R14-E.** `Latest` elimina as cópias provadamente anteriores antes de desempatar. **Achado meu.** O `Save` depois de um `Load` que falhou guarda o `.corrompido` antes de sobrescrever. Versão 1.1.4 |
| este | Resposta no relatório, D-033, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 264 passando (eram 257).
- **Correção desligada:** tratar `Unreadable` como `None` faz 3 testes falharem; não eliminar as ancestrais faz 6 falharem.
- **Roteiro do codex:** `colab/revisoes/evidencias/2026-10-31-r14-dados.ps1` → 28 verificações, com as asserções `BUG-` convertidas para o comportamento correto.
- **[Conferencia 41251256234](https://github.com/victorhoura/InfoPC/actions/runs/41251256234)** e **[Linux 41251253304](https://github.com/victorhoura/InfoPC/actions/runs/41251253304)**, em `2e7a3b0`, verdes.
- **Não verificado (honestidade):**
  - **`AppSettings.Save` do Windows:** a cópia antes de sobrescrever foi testada só no Core; no Windows, por inspeção e build. O `DataFolder` é o mesmo arquivo nas duas edições.
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo. A CPU chega a 100 °C aqui.
  - **Termos das licenças:** texto da Silicon Labs de uma cópia antiga; lista de distribuíveis do DXIL não localizada. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **`DataFolder.Resolve` e `Latest`:** fluxo reescrito. Veja a ordem das decisões, com cópia ilegível, sem gravação, com a antiga pasta `dados`, e o texto dos avisos.
- **Pasta em uso ilegível:** continua sendo devolvida, e o `Load` falha; o usuário é avisado, mas a tela abre sem os dados até reabrir. Escolhi isso porque devolver a outra cópia esconderia o desfazer da mesma forma.
- **Limites aceitos (D-032, D-033):** entre duas cópias de versões anteriores ainda vale a data, com tolerância de 2 s; passar de 256 gravações sem sincronizar conta como divergência, com aviso; voltar a uma versão anterior e gravar lá faz essa cópia perder para a que tem trilha; a antiga pasta `dados` nunca é movida, e divergência com ela repete o aviso.
- **Repositório antigo:** `victorhoura/InfoPC-antigo` continua privado, até o usuário liberar a permissão `delete_repo`.
