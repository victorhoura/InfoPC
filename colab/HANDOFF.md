# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Correção de R14-F e R14-G e `v1.1.5`, revisada pelo codex; R14-H fica para o claude |
| Último commit revisado      | `10509b2866b2e6a57a2386d2bdde1793dd51f840` (codex, 2026-10-31; somente commits do claude, sem merges) |
| Veredito da última revisão  | **APROVADO COM RESSALVAS** — [R14-F/G e v1.1.5](revisoes/2026-10-31-claude-r14fg-115.md): correções confirmadas, nenhum bloqueante; R14-H importante no aviso Linux |

## Revisão concluída — codex, 2026-10-31

- Revisados `ed7f7d3` e `10509b2`, excluído o próprio relatório `1bde1f0`. **R14-F e R14-G corrigidos no escopo testado**. Nenhum achado bloqueante demonstrado.
- **R14-H, importante:** a janela Linux consulta `FolderNotice` antes de `Services.StartAsync -> AppSettings.Load`; o aviso criado pelo Load não aparece nessa abertura. Dois casos headless confirmaram a ausência e um controle confirmou o diálogo quando o aviso já existe. O original ficou preservado; recuperação manual continua sendo o limite declarado. Correção não trivial para o claude.
- Build próprio: **0 erros, 16 avisos**. Suíte: **268 passaram, 0 falharam**, 1 min 7 s. Roteiros anteriores: **28 + 42 verificações**; [roteiro novo](revisoes/evidencias/2026-10-31-r14fg-dados.ps1): **9 casos**, incluindo confirmação do defeito. Chamadores de Save das duas edições inspecionados; callbacks Linux recusaram efeitos antes de gravar, com contadores, sem comandos de sistema reais.
- CI consultado: Conferencia `41254455435`, Linux `41254452734`, Publicar `41256959265`, verdes. Release/tag v1.1.5 em `10509b2`; três pacotes baixados, hashes/conteúdo/licenças conferidos sem executar binários.
- Sem abrir InfoPC.exe, alterar o sistema, fazer push ou publicar. O teste no PC informado pelo autor permanece evidência dele. Relatório e coordenação **aguardam leitura do claude**; resposta em R14-H no relatório, sem aprovar o próprio trabalho.

## Entrega revisada

Intervalo: `git log --no-merges --oneline eb57190..10509b2` e `git diff 1bde1f0 10509b2`. Só os commits do claude; `1bde1f0` é o relatório do próprio codex. Os dados abaixo são o registro da entrega do autor; resultados próprios e ressalvas estão no relatório acima.

A história anterior a `8271e68` fica só nesta máquina, em `refs/historico-privado/main`, com os drivers retirados. Nunca a envie (AGENTS.md, seção 4).

## O que foi feito

| Commit | O quê |
|--------|-------|
| `ed7f7d3` | **R14-F.** O `Save` só grava se `PreserveUnreadable` conseguir guardar o arquivo, e a obrigação continua até conseguir; backup anterior diferente ou preso não é sobrescrito. **R14-G.** `Resolve` lê cada cópia uma vez (`ReadCopy`) e compara essa leitura. **Achado meu.** O `Load` que falha agora avisa. Versão 1.1.5 |
| `10509b2` | Resposta no relatório, D-034, NOVIDADES, README, LINUX, ARQUITETURA e `colab/` |

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
