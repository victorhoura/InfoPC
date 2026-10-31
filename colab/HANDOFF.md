# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-D e R14-E e `v1.1.4`, revisada pelo codex. **R14-F/G aguardam correção do claude** |
| Último commit revisado      | `eb571905915c9889b438668b7f44e28d688b02d0` (codex; somente commits do claude, excluído o próprio `94e9c02`) |
| Veredito da última revisão  | **REPROVADO** — [correção de R14-D/R14-E e v1.1.4](revisoes/2026-10-31-claude-r14de-114.md): R14-F bloqueante (perda do original após falha na preservação) e R14-G importante (bloqueio após checagem faz retornar ancestral) |

## Intervalo desta revisão

`git log --no-merges --oneline ae85961..main` e `git diff 94e9c02 main`. Só os commits do claude; `94e9c02` é o relatório do próprio codex.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## Fechamento pelo revisor — codex, 2026-10-31

- Revisados `2e7a3b0` e `eb57190`. Reproduções originais R14-D/E confirmadas como corrigidas. Build próprio: **0 erros, 16 avisos**; suíte: **264 passaram, 0 falharam**; roteiro anterior: **28 verificações**; [roteiro novo](revisoes/evidencias/2026-10-31-r14de-dados.ps1): **42 verificações**, incluindo as reproduções dos achados.
- **R14-F, BLOQUEANTE:** um Save que falha com o arquivo ainda preso desarma a preservação; a tentativa seguinte substitui o original sem backup. Também perde o original se só o `.corrompido` está preso. Core executado com dados descartáveis; Windows inspecionado e compilado, sem executar o programa. É perda física adicional ao limite declarado da tela vazia.
- **R14-G, IMPORTANTE:** arquivo da portátil fica preso após `CanRead`; `Latest` ainda trata a nova falha como ausência de trilha. `Resolve` retorna a fallback anterior com aviso de falha de cópia. Três reproduções, sem exclusão física; o aviso não explica que o desfazer posterior ficou na portátil.
- Conferidos logs de Conferencia `41251256234`, Linux `41251253304` e Publicar `41251723181`. Release/tag `v1.1.4` em `eb57190`; três pacotes baixados, hashes e conteúdo conferidos sem executar binários. A abertura com arquivo preso informada pelo autor é evidência dele, compatível com o caminho feliz testado no Core; não é execução do revisor.
- Sem correções no produto, sem abrir InfoPC.exe, alterar o sistema, push ou publicação. O relatório e este registro aguardam leitura do claude; as correções não triviais ficam para ele. Limites D-032/D-033 mantidos; detalhes e reproduções no relatório.

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
