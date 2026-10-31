# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-31                                                  |
| Assunto                     | Conferência no PC real e em Linux, correções e `v1.1.2`. **Aguarda revisão do codex**, junto com as duas entregas anteriores |
| Último commit revisado      | `86f1d7b0a6afe50fa757364141102507ec6180f0` (codex), na história antiga: veja abaixo |
| Veredito da última revisão  | **REPROVADO** — [R10-C e dados portáteis](revisoes/2026-10-30-claude-r10c-e-dados-portateis.md): R14-A/B corrigidos, ainda sem revisão |

## O que revisar (três entregas, todas do claude)

O repositório público nasceu em `8271e68`, com a árvore de `refs/historico-privado/main`. A história antiga fica só nesta máquina, com os drivers retirados: nunca a envie (AGENTS.md, seção 4).

1. **R14-A/B, identidade sem `libssl`, workflow de publicação e abertura do código (D-030):**
   `git log --no-merges --oneline 86f1d7b..refs/historico-privado/main` e `git diff 86f1d7b refs/historico-privado/main`.
2. **Correções da 1.1.2 e workflow Conferencia (D-031):**
   `git log --no-merges --oneline 8271e68..main` e `git diff 8271e68 main`.

`git diff refs/historico-privado/main 8271e68` deve sair vazio. Todo hash antigo citado em `colab/` e em `docs/` está em `refs/historico-privado/main`.

## O que foi feito nesta entrega

O usuário perguntou se o programa perdeu função em relação ao início e pediu para verificar o que faltava. A comparação com a importação (`fc4571c`) mostra que saíram só o botão INSTALAR TODOS e os drivers de D-030.

| Commit | O quê |
|--------|-------|
| `9fa58b3` | Interruptores da Otimização ficavam errados depois de RESTAURAR ORIGINAIS: `box.IsChecked = ...` apagava o binding (já no original). `Vdf.Esc` trocava a tabulação de outra opção da Steam por `\t`, e o arquivo não voltava igual. Sobreposições na Visão geral, Placa de vídeo, Gargalo e Stress test. Versão 1.1.2 |
| `cc0f37a`, `5297adb` | Workflow Conferencia e `ConferenciaTests` (D-031) |
| `7c884b5`, `f2b7771` | Sem tela ou sem biblioteca gráfica, `Cli.WhyNoWindow` explica em vez de abortar. Recusa do polkit (127) com mensagem própria |

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 248 passando (eram 237).
- **[Conferencia, run 41209509537](https://github.com/victorhoura/InfoPC/actions/runs/41209509537)**, em `f2b7771`, verde:
  - `pkexec` real roda como root; instalar `cowsay` por ele e desfazer remove o pacote; recusa do polkit dá a mensagem certa; `ubuntu-drivers` real responde; com hidepid, `InstallerActivity` diz `Unknown`.
  - FAT32 e exFAT: dados no pen drive; montado só para leitura, vão para `~/.local/share/InfoPC`.
  - Janela vista em GNOME (Wayland com XWayland), KDE em X11 e KDE em Wayland, em máquina virtual. GNOME com `--no-x11`, sem tela e contêiner sem bibliotecas: código 1 e a mensagem, sem core dump.
  - Pacote arm64 em Debian 12, Fedora 41 e openSUSE Tumbleweed, modo texto.
- **PC real** (Dell XPS 8940, i5-10400, GTX 1650 SUPER, Windows 11), programa elevado e dirigido por UIAutomation; com o pacote da `v1.1.1` e, nas correções, com o build da 1.1.2:
  - **As 21 páginas** abriram, com dados coerentes. Sem clique: 0 conexões externas e 0 de 11 itens do sistema alterados. No original 0.16, 2 conexões externas sem clique.
  - **Otimização:** 5 ajustes aplicados mudam 7 itens; RESTAURAR ORIGINAIS deixa 0 diferenças, e os interruptores acompanham.
  - **Inicialização:** desativar, reativar, REMOVER e Restaurar, numa entrada de teste criada e apagada por mim.
  - **Steam:** aplicar e RESTAURAR ORIGINAL deixam o `localconfig.vdf` igual byte a byte.
  - **Perfil de jogo:** JOGAR aplica GPU, tela cheia e plano; ao fechar, o plano volta; desmarcado, o próximo JOGAR devolve o registro.
  - **Maker:** instalar o Arduino, remover e restaurar, com o aviso de segurança do Windows; PÁGINA OFICIAL.
  - **Outros:** VERIFICAR TUDO trouxe a versão da NVIDIA (610.60 → 621.01); os 6 benchmarks; o corte do stress (limite 82 °C: parou sozinho, pico 89 °C); relatório, overlay e tema.
- **Não verificado (honestidade):**
  - **FPS com chave:** não há chave da HowManyFPS.
  - **Não clicado de propósito, para não alterar este PC:** instalar driver NVIDIA, Windows Update, OTIMIZAR (fecha programas), APLICAR do Overclock, Instalar apps, remover dispositivos e liberar números COM. Stress test longo também não, porque a CPU chega a 100 °C aqui.
  - **Sem hardware aqui:** Integração RGB e placas Arduino conectadas.
  - **Linux em PC físico:** sensores, S.M.A.R.T. e GPU de verdade. Janela de senha do polkit fechada pelo usuário (126) só está simulada, porque exige um agente de verdade.
  - **Termos das licenças:** o texto da Silicon Labs lido é uma cópia antiga. Não é parecer jurídico.

## Riscos e onde olhar com mais atenção

- **Efeitos de teste, já desfeitos no PC real:** entrada no Run, opção da Steam, certificado da Arduino em TrustedPublisher (o aviso do Windows o adicionou), drivers Arduino e valores do jogo de teste. Conferi cada um depois, mas o usuário não acompanhou.
- **`Vdf.Esc`:** deixou de escapar tabulação e quebra de linha. `Parse` continua lendo `\t` e `\n`; um arquivo que os traga escapados volta com o caractere puro. O formato da Steam aceita os dois, mas confira.
- **`Cli.WhyNoWindow`:** o filtro do `catch` só pega `XOpenDisplay` e `DllNotFoundException`, em qualquer nível de `InnerException`. Veja se não esconde outro erro.
- **`THIRD-PARTY-NOTICES.txt` e o DirectX Shader Compiler:** vêm da entrega anterior. Veja `docs/UPSTREAM.md`, seção 7.
- **Repositório antigo:** `victorhoura/InfoPC-antigo` continua privado, até o usuário liberar a permissão `delete_repo`.
- **Observação sobre o PC do usuário:** CPU a 92 °C antes do benchmark, 100 °C durante, GPU com hot spot de 95 °C e `PerfCap` térmico. É do PC, não do programa.
