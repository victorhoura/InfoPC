# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | codex — revisão solicitada pelo usuário                     |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Revisão de `c8a04cc`, `ae85961` e v1.1.3 concluída. Relatório aguarda leitura do claude; R14-D/E cabem ao autor |
| Último commit revisado      | `ae859617828b20129ef7b47aab320c28b361996e` (claude; excluída a revisão do próprio codex) |
| Veredito da última revisão  | **REPROVADO** — [correção de R14-C/R15-A e v1.1.3](revisoes/2026-10-31-claude-r14c-r15a-113.md): R14-D bloqueante e R14-E importante |

## Resultado e próximos passos

- **R14-C original confirmado:** Save/Load reais com datas iguais/arredondadas preservam os registros na ida ao fallback e na volta à portátil; restauração posterior não ressuscita registros removidos.
- **R15-A confirmado:** asserção exige sucesso/ausência de timeout; execução ativa no `polkit-sim` do Conferencia `41246005320`.
- **R14-D — BLOQUEANTE:** falha ao ler a trilha vira `null`, tratado como versão antiga. Arquivo posterior temporariamente bloqueado faz carregar o estado anterior **sem aviso**, nos dois sentidos. O registro fica no arquivo posterior, mas some do desfazer acessível.
- **R14-E — IMPORTANTE:** com três candidatas, a redução por parentesco/data pode terminar numa ancestral que a portátil prova ser anterior. Há aviso e preservação física, mas a seleção recua e o aviso usa a origem já movida.
- Sem correção de produto pelo revisor. Resposta anterior do claude lida sem divergência; R14-D/E são cenários complementares. Responder no novo relatório, uma rodada por achado.

## Verificação própria do revisor

- `dotnet build InfoPC.csproj -c Release -nologo --no-incremental`: **0 erros, 16 avisos**, 11,81 s.
- `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release`: **257 passaram, 0 falharam**, 1 min 8 s; aviso conhecido `xUnit2031`. Dados/temporários isolados em `obj/review` e variáveis de testes reais vazias.
- `powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-10-31-r14-dados.ps1`: **28 asserções**, incluindo os defeitos reproduzidos. **Saída 0 confirma as observações; não aprova o produto.** Referencia o Core real e usa dados descartáveis em `obj/review`.
- Seis permutações de três estados da mesma linhagem, divergência, antiga `dados/`, identidade alheia preservada, falha de cópia e sondagens de escrita bloqueadas: detalhes e limites no relatório.
- Release v1.1.3 e tag em **`ae85961`**. Logs lidos: Publicar `41249700936`, Conferencia `41246005320`, Linux `41246001980`, verdes. Três pacotes baixados no worktree; hashes e conteúdo conferidos sem executar binários. O código publicado contém R14-D/E e permanece reprovado.
- Licença e drivers sem mudanças; `git diff --check 218dcda ae85961` passou e seis termos do nome antigo sem ocorrências inesperadas.

**Sem abrir InfoPC.exe ou alterar registro, drivers, serviços, configurações/permissões do Windows ou dados reais de AppData/Steam. Sem push, publicação, workflow disparado, envio de refs privadas, exclusão de repositórios/branches alheias ou mudança de visibilidade.** Diretórios/volumes realmente somente leitura e ubuntu-drivers são evidência do CI; localmente só arquivos descartáveis e renderização headless. Os limites de hardware, FPS com chave e licenças do autor abaixo permanecem.

Este registro e o roteiro aguardam leitura do claude; o codex não aprova o próprio commit. Integração e remoção do worktree por `wt.ps1 concluir`, sem publicação.

## Intervalo revisado

`git log --no-merges --oneline 12b823f..ae85961` e `git diff 218dcda ae85961`. Revisados os dois commits do claude; `218dcda` é o relatório do próprio codex e foi excluído.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## Entrega do autor revisada

| Commit | O quê |
|--------|-------|
| `c8a04cc` | **R14-C.** `DataFolder.Resolve` decide pela trilha de gravações (`DataTrail`, D-032), e não pela data. Empate ou divergência não sobrescreve em silêncio. **R15-A.** O teste de `ubuntu-drivers` exige que o comando funcione. A Conferencia leva e traz desfazer em pen drive real. Versão 1.1.3 |
| `ae85961` | Resposta no relatório, D-032, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Verificação e limites informados pelo autor

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 257 passando (eram 248).
- **Trilha desligada** (`Compare` sem ler `Trail`, depois restaurado): 4 testes novos de `DataFolderTests` falham, inclusive o da reprodução do relatório.
- **[Conferencia, run 41246005320](https://github.com/victorhoura/InfoPC/actions/runs/41246005320)**, em `c8a04cc`, verde: `Pen_drive_de_verdade_leva_e_traz_o_desfazer_com_a_data_arredondada` em FAT32 e exFAT reais; `ubuntu-drivers devices` com sucesso; os demais jobs como antes.
- **Não verificado (honestidade):**
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo. A CPU chega a 100 °C aqui.
  - **Termos das licenças:** texto da Silicon Labs de uma cópia antiga; lista de distribuíveis do DXIL não localizada. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **`DataFolder.Compare` e o fluxo de `Resolve`:** a ordem das candidatas (pasta em uso primeiro) decide os empates. Veja a escolha com três cópias, inclusive a pasta `dados` antiga.
- **`SetAside`:** move a cópia não escolhida para `.anterior`. A pasta `dados` antiga nunca é movida, porque contém as pastas de identidade; divergência com ela repete o aviso a cada abertura enquanto ela existir.
- **Limites aceitos:** entre duas cópias de versões anteriores ainda vale a data (com tolerância de 2 s); passar de 256 gravações sem sincronizar conta como divergência, com aviso; voltar a uma versão anterior e gravar lá faz essa cópia perder para a que tem trilha.
- **Repositório antigo:** `victorhoura/InfoPC-antigo` continua privado, até o usuário liberar a permissão `delete_repo`.
