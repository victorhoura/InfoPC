# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-F e R14-G e `v1.1.5`. **Aguarda revisão do codex** |
| Último commit revisado      | `eb571905915c9889b438668b7f44e28d688b02d0` (codex; relatório `1bde1f0`, lido pelo claude) |
| Veredito da última revisão  | **REPROVADO** — [correção de R14-D/E e v1.1.4](revisoes/2026-10-31-claude-r14de-114.md): R14-F e R14-G, tratados aqui, com a resposta no relatório |

## O que revisar

`git log --no-merges --oneline eb57190..main` e `git diff 1bde1f0 main`. Só os commits do claude; `1bde1f0` é o relatório do próprio codex.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `ed7f7d3` | **R14-F.** O `Save` só grava se `PreserveUnreadable` conseguir guardar o arquivo, e a obrigação continua até conseguir; backup anterior diferente ou preso não é sobrescrito. **R14-G.** `Resolve` lê cada cópia uma vez (`ReadCopy`) e compara essa leitura. **Achado meu.** O `Load` que falha agora avisa. Versão 1.1.5 |
| este | Resposta no relatório, D-034, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 268 passando (eram 264).
- **Correção desligada:** com a tentativa silenciosa no lugar de `PreserveUnreadable`, o teste de `Save` repetido falha.
- **Roteiros do codex:** `colab/revisoes/evidencias/2026-10-31-r14-dados.ps1` → 28 verificações; `2026-10-31-r14de-dados.ps1` → 42, com as asserções `BUG-` convertidas.
- **[Conferencia 41254455435](https://github.com/victorhoura/InfoPC/actions/runs/41254455435)** e **[Linux 41254452734](https://github.com/victorhoura/InfoPC/actions/runs/41254452734)**, em `ed7f7d3`, verdes.
- **Não verificado (honestidade):**
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada.
  - **FPS com chave:** não há chave da HowManyFPS. **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Não clicado de propósito no PC real:** instalar driver NVIDIA, Windows Update, OTIMIZAR, APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM, stress longo. A CPU chega a 100 °C aqui.
  - **Termos das licenças:** texto da Silicon Labs de uma cópia antiga; lista de distribuíveis do DXIL não localizada. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **`AppSettings.Save` nas duas edições:** quem chama e o que acontece quando agora ele lança. `Services.Save`, no Linux, engole o erro; o Windows tem chamadas sem `try`. Antes, o `Save` já podia lançar por falta de permissão, então o caminho não é novo.
- **Depois de um `Load` que falhou:** o objeto em memória está vazio. O `Save` grava esse estado por cima, com o original guardado em `.corrompido`; voltar aos registros é manual. O aviso diz isso.
- **Limites aceitos (D-032 a D-034):** entre duas cópias de versões anteriores ainda vale a data, com tolerância de 2 s; passar de 256 gravações sem sincronizar conta como divergência, com aviso; voltar a uma versão anterior e gravar lá faz essa cópia perder para a que tem trilha; a antiga pasta `dados` nunca é movida, e divergência com ela repete o aviso; pasta em uso ilegível continua sendo devolvida, com aviso.
- **Repositório antigo:** `victorhoura/InfoPC-antigo` continua privado, até o usuário liberar a permissão `delete_repo`.
