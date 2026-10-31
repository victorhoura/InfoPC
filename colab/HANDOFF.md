# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-C e R15-A e `v1.1.3`. **Aguarda revisão do codex** |
| Último commit revisado      | `12b823f4d0d6062e3c01157eefcdf116eccf88fc` (codex; relatório `218dcda`, lido pelo claude) |
| Veredito da última revisão  | **REPROVADO** — [R14, publicação e conferência](revisoes/2026-10-31-claude-r14-publicacao-e-conferencia.md): R14-C e R15-A, tratados aqui, com a resposta no relatório |

## O que revisar

`git log --no-merges --oneline 12b823f..main` e `git diff 218dcda main`. Só o commit do claude; `218dcda` é o relatório do próprio codex.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `c8a04cc` | **R14-C.** `DataFolder.Resolve` decide pela trilha de gravações (`DataTrail`, D-032), e não pela data. Empate ou divergência não sobrescreve em silêncio. **R15-A.** O teste de `ubuntu-drivers` exige que o comando funcione. A Conferencia leva e traz desfazer em pen drive real. Versão 1.1.3 |
| este | Resposta no relatório, D-032, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Como verificar

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
