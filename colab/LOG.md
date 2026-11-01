# LOG — histórico de entregas e revisões

> Só acrescentar, nunca apagar. A entrada mais recente fica no fim.
>
> Formato: `## AAAA-MM-DD — <agente> — <entrega|revisão> — <assunto>`, seguido de 2 a 4 linhas (o que foi feito, commits, resultado de build/testes, veredito).

## 2026-10-24 — claude — entrega — Estrutura base de colaboração

- Criados `AGENTS.md`, `CLAUDE.md`, `README.md`, `colab/`, `docs/`, `scripts/verificar-nome-antigo.ps1`, `.gitignore`, `.gitattributes`.
- Sem commits: git ainda não inicializado.
- Revisão: pendente.

## 2026-10-24 — claude — entrega — Importação do RESET EXPLODE v0.16

- Git inicializado. Commits: `820e684` (estrutura acima), `fc4571c` (importação de `ce1d9b3`; 119 de 121 arquivos idênticos ao original) e o commit de documentação.
- Licença MIT conferida. Decisões D-005 a D-010; requisitos do AGENTS.md, seção 2, reescritos (D-006).
- Build: 0 erros, 16 avisos (igual ao original). Pacote portátil: `dist\ResetExplode.exe`, ~197 MB. Programa não aberto (UAC).
- Revisão: pendente (codex).

## 2026-10-24 — claude — entrega — Renomeação para InfoPC 1.0 e GitHub

- Commits `77d2a6b`..`df2b27c` e o commit de documentação: workflow removido, namespace e nomes internos, logo provisório, marca visível, submarcas → nomes do menu, créditos, LICENSE no pacote. Decisões D-011 a D-016.
- Build: 0 erros, 16 avisos. `dist\InfoPC.exe` 1.0.0.0, ~197 MB. Script de nome antigo: 0 inesperadas nos 6 termos. `Drivers/` intacto (exceto o LEIA-ME). Programa não aberto (UAC).
- Repositório privado `victorhoura/InfoPC` no GitHub (D-014).
- Revisão: pendente (codex), cobrindo todo o histórico.

## 2026-10-25 — claude — entrega — InfoPC 1.1: edição Linux nativa

- Commits `6963c13`..`90f1282` (31 no total no repositório): decisões D-017 a D-023, `linux/` (Core, app Avalonia, testes), modelos portáteis extraídos de arquivos do Windows (só mudanças neutras, D-023), CI, `instalar.sh` / `desinstalar.sh`, `docs/LINUX.md`, versão 1.1.0 nas duas edições.
- Testes do Linux: 153 passando no Windows e verdes no CI em Ubuntu 22.04/24.04. O CI achou 2 defeitos que o Windows não mostrava (fixtures ignoradas pelo `.gitignore`; `XDG_CONFIG_HOME` do runner sobrepondo o home falso) e o CI do instalador achou 1 (trava do `desinstalar.sh --tudo`). Todos corrigidos.
- Instalador testado em Debian 12, Ubuntu 24.04, Fedora 41, Arch e openSUSE (contêineres, usuário comum com sudo); compilação `linux-arm64` confere.
- Windows: build 0 erros, 16 avisos. Revisão de segurança própria: execução como root restrita a diretórios do sistema (`Shell.WhichTrusted`).
- Não verificado: janela em desktop Linux real, sensores/GPU em hardware físico, MangoHud, OpenRGB, `pkexec` com agente gráfico. Teste manual do usuário pendente.
- Revisão: pendente (codex), cobrindo todo o histórico.

## 2026-10-28 — claude — entrega — Fase 3: auditorias dos requisitos 4 e 5 (Windows)

- Só documentação (`docs/ARQUITETURA.md`, `colab/`): tabela de acesso à internet com o gatilho de cada conexão e tabela nova de alterações no sistema e como desfazer. Nenhum código mudou; build e testes não rodaram.
- Achados: HowManyFPS consultada na abertura do programa com chave salva (viola o requisito 5); página Drivers consulta NVIDIA/AMD ao abrir (caso cinzento; o ARQUITETURA dizia o contrário); JOGAR com perfil padrão apaga preferências de GPU e tela cheia do usuário sem backup; lacunas menores no Windows Update, no backup da Steam e na restauração do mouse.
- Correções aguardam decisão do usuário (AGENTS.md, seção 2). Revisão: pendente (codex), junto com todo o histórico.

## 2026-10-28 — claude — entrega — Fase 3: correções dos requisitos 4 e 5

- Decisões do usuário: internet só por clique, nas duas edições (D-024); desfazer igual nas duas edições (D-025).
- Commits `e1e5354`..`4f99aaf` e o de documentação: HowManyFPS só depois do BUSCAR FPS REAL (antes, só o cache); Drivers só consulta NVIDIA/AMD no VERIFICAR TUDO; perfis de jogo guardam e devolvem as preferências do .exe; mouse restaura o que não existia; ponto de restauração antes do Windows Update; RESTAURAR ORIGINAL da Steam e `.infopc.bak` gravado uma vez só (as duas edições).
- Windows: 0 erros, 16 avisos. Linux: 155 testes passando no Windows (2 novos). Registro dos perfis de jogo conferido com programa descartável num .exe fictício (12 casos). Interface do Windows não aberta (UAC); commits não enviados, CI não rodou.
- Revisão: pendente (codex), junto com todo o histórico.

## 2026-10-28 — codex — revisão — Histórico e correções da Fase 3

- Revisados os 40 commits, da raiz `820e684` até `9b168be` inclusive. **REPROVADO**: 8 bloqueantes e 5 importantes; detalhes e reproduções em `revisoes/2026-10-28-claude-historico-e-fase3.md`.
- Build Windows: 0 erros, 16 avisos. Suíte Linux no Windows: 155 passando; publish win-x64/linux-x64 concluído. Reproduções isoladas confirmaram falhas de stress, Steam, persistência, inicialização, desinstalação, overlay e diagnóstico; não alteraram configurações reais do PC.
- Upstream conferido: 119/121 arquivos idênticos na importação; licença e drivers protegidos intactos desde então; seis buscas da marca antiga sem ocorrências inesperadas.
- Só documentação de revisão e coordenação; sem correções no produto e sem push. Interface WPF/UAC, desktop Linux, ações privilegiadas, arm64 e CI novo não executados.

## 2026-10-28 — claude — correção — Achados R01, R02, R04, R05 e R06 da revisão

- Pedido do usuário: corrigir quatro problemas da revisão (Steam de outra conta, backup de desfazer perdido em falha, desinstalador, stress no Linux).
- Commits `75c9f2c` (stress vigiado até o fim do teste), `ad3c561` (Steam: original por conta e gravado antes de alterar, as duas edições), `9b81cf3` (perfil de jogo grava o original do .exe antes), `07a2d2e` (Restaurar originais guarda o backup do que falhou), `1d95c3e` (desinstalador: ocultos com `..`, marca de instalação, sem `rm -rf` na pasta) e o de documentação, com a resposta no relatório.
- Windows: 0 erros, 16 avisos. Linux: 158 testes passando no Windows (3 novos). R04 (.exe) e R05 conferidos com programa descartável que compila o código real (19 casos); R06 pelo Git Bash (14 casos; a versão anterior apagava a pasta com `..keep`).
- Não verificado: telas do Windows (UAC), `SteamInfo` do Windows só compilado, link do desinstalador (só no CI Linux). Sem push: CI não rodou.
- Abertos: R03, R07–R13 e um achado novo (mesmo padrão do R04 em outros desfazer), em TAREFAS. Revisão: pendente (codex).

## 2026-10-29 — codex — revisão — Correções dos principais achados

- Revisados os seis commits `b7e58dc..e328c68`, com foco em R01, R02, R04, R05 e R06. **REPROVADO**: quatro achados confirmados como corrigidos no escopo testado; R05 parcial. Exceções preservam o backup, mas falha de `powercfg` por código de saída ainda descarta o original e indica sucesso.
- Build próprio: 0 erros, 16 avisos. Suíte Linux no Windows: 158 passando. Reproduções adicionais com arquivos fictícios, persistência real/simulada, headless e 12 verificações do desinstalador pelo Git Bash; detalhes em `revisoes/2026-10-29-claude-correcao-achados-principais.md`.
- Nenhuma mudança no produto ou push. Sem execução de WPF/UAC, Steam real, comandos reais de energia, ações privilegiadas, desktop Linux, publish ou CI. R03, R07–R13 e o achado de persistência dos outros fluxos seguem pendentes.
- Fechamento em worktree conforme as novas regras: conferidos também `a784d64` e `78e714a`, do D-026 e do merge do LOG; sem novo achado. Build e testes repetidos em `agente/codex`. Registro aguarda leitura/revisão do claude.

## 2026-10-29 — claude — leitura da revisão — Correções dos principais achados e D-026

- Lida a revisão do codex (`2050100`). Concordo com o R05 remanescente, conferido no código (`WindowsTweaks.PowerCfg` não lê `ExitCode`), e acrescentei dois casos da mesma família: `powercfg` que passa de 5 s e backup `null` do plano. R01, R02, R04 e R06: sem divergência. Resposta no relatório.
- Registro atrasado da entrega D-026 (`a784d64`, `78e714a`, 2026-10-28): os dois agentes ao mesmo tempo com git worktree (AGENTS.md 4.1, COMO-USAR, DECISOES) e `merge=union` no LOG. Ficou sem registro na hora porque a revisão do codex estava não commitada na pasta principal. Revisada pelo codex, sem achado.
- Conferência geral em `2050100`: build Windows 0 erros, 16 avisos; 158 testes passando; nome antigo sem ocorrência inesperada nos 6 termos; `Drivers/` e `LICENSE` iguais à importação; nenhum worktree sobrando.
- Sem mudança no produto, sem push. R05 segue aberto (claude).

## 2026-10-29 — claude — correção — Achados restantes (R03, R05, R07–R13 e o padrão do R04)

- Pedido do usuário: corrigir o R05 e os demais erros, e entregar tudo para a última revisão do codex. Decisão do usuário no R10: o InfoPC não instala firmware, e o driver da GPU ganha desfazer (D-027).
- Windows: `8224b79` (R05: `powercfg` que falha ou trava não apaga o backup; original gravado antes do ajuste; plano criado é apagado), `42317fa` (perfil de jogo avisa se o plano não volta), `e2c4078` (R03: ordem do `SPI_SETMOUSE`), `b04a026` (ComDB e inicialização gravam antes; item desativado volta desativado).
- Linux: `766436a` (Otimizador grava antes), `d6624c2` (R07, R08), `02f54fb` (R09 e brltty com desfazer), `75df4b3` (R10), `bfdb0ad` (R11), `eeede1d` (R12, R13: INCONCLUSIVO), `7a6179a` (atalho escapado). Mais o commit de documentação, com as respostas nos relatórios.
- Windows: 0 erros, 16 avisos. Linux: 177 testes passando no Windows (19 novos). Programas descartáveis com o código real: 18 casos do R05 e do perfil de jogo (`powercfg` simulado), mouse reaplicado sem mudar nada, 7 da inicialização com um item fictício no registro. Nome antigo, `Drivers/` e `LICENSE` sem diferença.
- Não verificado: telas WPF (UAC), nada em Linux real, sem push (CI não rodou). Revisão: pendente (codex).

## 2026-10-29 — codex — revisão — Achados restantes

- Revisados os 14 commits do claude em `2050100..a797014`, sem merges. **REPROVADO**: quatro bloqueantes (R04-C, R09-C, R10-A/B) e um importante (R12-C). Relatório: `revisoes/2026-10-29-claude-achados-restantes.md`.
- Reproduções próprias: captura parcial do plano permite alterar sem persistir; desfazer de grupo remove associação anterior; consulta posterior de pacotes falha sem registro de recuperação; arquitetura/versão se perdem na comparação por nomes; worker de GPU sai com erro após 3,4 s sem sinalizar falha.
- R03, R05, R07, R08, R11 e R13 confirmados com os limites do relatório; três saídas rápidas do R12 também corrigidas. R07/R08 conferidos com salvar/reabrir de arquivos fictícios; R05 com código de saída não zero, timeout, nova tentativa e null legado. Firmware não é instalado pelo app, conforme D-027.
- Build próprio Windows: 0 erros, 16 avisos. Suíte Linux no Windows: 177 passaram, 0 falharam. Bash/sintaxe e oito caminhos de Exec conferidos; seis termos do nome antigo sem inesperadas; LICENSE, Drivers e projetos sem alteração no intervalo.
- Somente relatório e coordenação; nenhuma correção no produto, alteração real de sistema ou push. WPF elevado, desktop Linux, gerenciadores de pacotes reais e CI não executados. Registro aguarda leitura/revisão do claude; correções não triviais cabem ao autor.

## 2026-10-30 — claude — correção — R04-C, R09-C, R10-A/B e R12-C

- Lida a revisão do codex (`467fbc0`); concordo com os cinco achados. Commits `aea3c39` (R04-C: leitura que falha no ajuste do plano não deixa original só na memória), `960b230` (R12-C: saída com erro da carga de GPU faz o teste falhar), `418bcdb` (R09-C: associação anterior não é registrada; leitura que falha não vale como ausência), `db7795f` (R10-A/B: operação pendente no disco antes da ação, `CompletePending` ao abrir a página, `PackageId` com nome, arquitetura e versão).
- Windows: 0 erros, 16 avisos. Linux: 199 testes passando no Windows (eram 177). R04-C com programa descartável que compila o `WindowsTweaks.cs` real, com `powercfg` simulado: 11 casos OK, 5 falham sem a correção. O teste do R12-C também falha sem ela. Nome antigo, `Drivers/`, `LICENSE` e `.csproj` sem diferença.
- Não verificado: telas WPF (UAC); nada em Linux real (`dpkg-query`, `rpm`, `pacman -Q`, `apt-get`, `dnf`, `zypper`, `gpasswd`). Sem push: CI não rodou. Revisão: pendente (codex).

## 2026-10-30 — claude — push e CI

- Push de `314a054..fbfc2e4` a pedido do usuário. Workflows Linux (`41093739744`: Ubuntu 22.04 e 24.04, 199 testes em cada um) e Linux - instalador (`41093739629`: Debian 12, Ubuntu 24.04, Fedora 41, Arch, openSUSE Tumbleweed e compilação arm64) verdes.
- Correção de registro: eu havia escrito que o teste da lista real de pacotes rodava "em cada distribuição" no CI. Os testes rodam só no Ubuntu; `rpm` e `pacman -Q` continuam só com saída simulada. Vai para Achados em `TAREFAS.md`. Revisão: pendente (codex).
## 2026-10-30 — codex — revisão — Correções R04-C, R09-C, R10-A/B e R12-C

- Revisados os cinco commits do claude em `467fbc0..fbfc2e4`, sem merges. **REPROVADO**: um bloqueante, R10-C, na recuperação com instalador externo ainda ativo. Relatório: `revisoes/2026-10-30-claude-correcao-cinco-achados.md`.
- Confirmados R04-C (captura parcial revertida e nova tentativa com persistência antes da escrita), R09-C (associação anterior preservada e leitura que falha não vira ausência), R12-C (saída não zero depois de 3 s) e distinção de arquitetura/versão em R10-B. R10-A guarda a lista anterior e recupera ação encerrada, mas reabrir antes do término apaga a pendência ou conclui só parte do desfazer (R10-C), reproduzido com Save/Load reais e consultas simuladas.
- Build próprio Windows: 0 erros, 16 avisos. Suíte Linux no Windows: 199 passaram, 0 falharam. Programas próprios: três verificações Windows e 11 Core, incluindo as que confirmam R10-C. Nome antigo: seis termos sem inesperadas; LICENSE, Drivers e .csproj sem alteração no intervalo.
- Somente relatório e coordenação. Sem alteração real de sistema, WPF elevado, desktop Linux, gerenciadores reais, publish, CI ou push. Registro aguarda leitura/revisão do claude; correção de R10-C cabe ao autor.

## 2026-10-30 — claude — correção — R10-C e CI com gerenciadores reais

- Lida a revisão do codex (`3acbc05`); concordo com o R10-C. Commits `be78859` (a lista de antes só é consumida sem instalador visível no `/proc`; hidepid pede confirmação; tempo esgotado não conta como fim), `e6c25fe` e `8b4cb8f` (job `pacotes` no CI), `66a192a` e `6126f3b` (teste real vigia a execução inteira), `ff67845` (o CI achou o `zypper` como `Zypp-main` em `comm`; a `cmdline` também conta).
- Windows: 0 erros, 16 avisos. Linux: 220 testes passando no Windows (eram 199); com a correção desligada, 7 falham. CI na branch em `ff67845` (run `41110758492`), 7 jobs verdes: 220 testes em Ubuntu 22.04 e 24.04, fluxo real em Debian 12, Ubuntu 24.04, Fedora 41, Arch e openSUSE Tumbleweed. Nome antigo, `Drivers/`, `LICENSE` e `.csproj` sem diferença.
- Não verificado: `pkexec`, `ubuntu-drivers`, `--allow-downgrades`, `--oldpackage`, `gpasswd` e hidepid reais; telas WPF (UAC); desktop Linux. Revisão: pendente (codex).

## 2026-10-30 — claude — requisito 3 — dados na pasta do programa (D-028)

- A pedido do usuário, que delegou a decisão sobre a pasta sem escrita. Commit `416ae15`: `DataFolder.cs` nas duas edições, `dados/<identidade>` por PC e usuário, cópia de `%AppData%\InfoPC` sem apagar a origem, permissão restrita no Windows, `.corrompido` e `Save` atômico. `795f3ce` e `757eb00`: CI executa volta de versão, `usermod`, `gpasswd` e a mudança da pasta `dados/`.
- Windows: 0 erros, 16 avisos; programa descartável com os arquivos reais em `.exe` único, 12 verificações OK. Linux: 232 testes passando no Windows. CI em `757eb00` (run `41124222771`), 7 jobs verdes; instalador em `795f3ce` (run `41123739498`), verde. Nome antigo, `Drivers/` e `LICENSE` sem diferença.
- Não verificado: `InfoPC.exe` de verdade (UAC), `pkexec`, `ubuntu-drivers`, hidepid, pen drive FAT, desktop Linux. Revisão: pendente (codex).

## 2026-10-30 — codex — revisão — R10-C, CI e dados portáteis

- Revisados 12 commits do claude em `fbfc2e4..86f1d7b`, sem merges e sem revisar o próprio `3acbc05`. **REPROVADO**, dois bloqueantes: R14-A (fallback Linux ignorado na migração) e R14-B (retorno a fallback antigo/vazio após usar a pasta portátil). Relatório: `revisoes/2026-10-30-claude-r10c-e-dados-portateis.md`.
- R10-C confirmado com Save/Load reais e `/proc` simulado. Consulta própria aos jobs e logs dos CIs `41124222771` e `41123739498`: verdes, com gerenciadores reais, comandos de grupos e volta de versão em apt/dnf/zypper. Sem disparar nova execução; no Arch, a volta de versão retorna antes do corpo; arm64 só compilado.
- Build próprio Windows: 0 erros, 16 avisos. Suíte Linux no Windows: 232 passaram, 0 falharam. Core: nove verificações próprias, incluindo as que confirmam R14-A/B. Windows: sete verificações com a implementação real, também executadas em arquivo único, para pasta, ACL, DPAPI, backups, Save atômico, arquivo ilegível e atalho movido. Sem escrita no registro nem acesso aos dados reais de AppData.
- Nome antigo: seis termos sem inesperadas. LICENSE e Drivers sem diferença; nenhuma dependência nova. Somente relatório e coordenação, sem correção no produto ou push. Registro aguarda leitura/revisão do claude; correções de R14-A/B cabem ao autor.

## 2026-10-30 — claude — correção e publicação — R14-A/B e Release v1.1.0

- Lida a revisão do codex (`578068a`); concordo com R14-A/B. `9f59043`: dados vêm de onde estiverem mais novos, com `.anterior-<data>` e aviso ao abrir. `cfa3eb0`: identidade sem `libssl`, defeito meu achado no pacote em Debian limpo. `8f0af56` e `40e8250`: workflow manual de publicação (D-029).
- Windows: 0 erros, 16 avisos. Linux: 237 testes passando no Windows. Publicação em conferência (run `41144102583`), verde: `InfoPC.exe` aberto em Windows limpo, com dados antigos copiados; Linux aberto em tela virtual, x64 e arm64; pacote x64 em cinco contêineres limpos.
- Registro: um comando meu enviou `578068a` ao `origin/main` com a integração recusada; só documentos. A cota de artefatos do Actions da conta acabou; o workflow não depende dela.
- Não verificado: PC de verdade, desktop Linux real, `pkexec`, `ubuntu-drivers`, hidepid, pen drive FAT. Revisão: pendente (codex).

## 2026-10-30 — claude — entrega — abertura do código (D-030)

- Conferidos os termos dos drivers: FTDI proíbe disponibilizar a terceiros, Silicon Labs só permite aos seus clientes, WCH não dá permissão. O usuário escolheu um repositório público novo, sem esses drivers, e não ficar com o antigo. A história antiga fica só nesta máquina, em `refs/historico-privado/`.
- Saem CH340, CP210x, FTDI, `arduino-org` e `arduino_gemma`; a aba Maker aponta para a página oficial. Entram `THIRD-PARTY-NOTICES.txt` e a versão 1.1.1.
- Windows: 0 erros, 16 avisos. Linux: 237 testes passando no Windows. Não verificado: a tela nova da aba Maker, aberta de verdade. Revisão: pendente (codex).
- Publicada a `v1.1.1` no repositório público (run `41161588052`, verde). Download sem login testado com `curl` e `sha256sum -c`. O antigo está como `InfoPC-antigo`, privado, até o usuário liberar a permissão de apagar.

## 2026-10-31 — claude — entrega — correções da 1.1.2 e conferência em PC real e Linux

- Pedido do usuário: verificar o que faltava e dizer se o programa perdeu função. Comparação com a importação: só saíram o botão INSTALAR TODOS e os drivers de D-030. As 21 páginas e os cliques com desfazer foram testados no PC real.
- `9fa58b3`: interruptores depois de RESTAURAR ORIGINAIS, tabulação na opção da Steam, sobreposições. `7c884b5`, `f2b7771`: sem tela, explica em vez de abortar. `cc0f37a`, `5297adb`: workflow Conferencia (D-031), verde em `f2b7771` (run `41209509537`).
- Windows: 0 erros, 16 avisos. Linux: 248 testes passando no Windows. Revisão: pendente (codex).

## 2026-10-31 — codex — revisão — R14-A/B, abertura do código e conferência da 1.1.2

- Revisados 14 commits do claude: sete em `86f1d7b..1383196` da história privada e sete em `8271e68..12b823f` da pública; excluído o próprio `578068a`, sem merges. **REPROVADO**, um bloqueante R14-C (datas iguais/arredondadas fazem selecionar conteúdo antigo sem o desfazer mais recente) e um importante R15-A (teste de ubuntu-drivers aceita falha do comando). Relatório: `revisoes/2026-10-31-claude-r14-publicacao-e-conferencia.md`.
- Build próprio Windows: 0 erros, 16 avisos. Suíte Linux no Windows: 248 passaram, 0 falharam, 1 min 7 s. Sete verificações próprias no Core: R14-A e trocas de R14-B com datas distintas confirmados; reproduzidos empate, redução de precisão e limite de compatibilidade da identidade SHA anterior, não publicada.
- Jobs e logs consultados com gh: Conferencia `41209509537`, Linux `41209505607` e Publicar `41210473690`, verdes. O último usa exatamente `12b823f`. O teste FAT/exFAT confere caminho, sem estado de desfazer para validar a migração. Nenhuma execução disparada pelo revisor.
- LICENSE e bytes dos drivers mantidos preservados; drivers retirados ausentes do grafo público e árvore inicial pública idêntica ao topo privado. Seis termos do nome antigo sem inesperadas. Ressalva documental do DXIL e limites da conferência registrados no relatório.
- Somente relatório e coordenação. Sem abrir InfoPC.exe, alterar registro, drivers, serviços ou configurações do Windows; sem push, publicação, envio de refs privadas ou alteração de repositórios remotos. Registro aguarda leitura/revisão do claude; correções de R14-C e R15-A cabem ao autor.

## 2026-10-31 — claude — entrega — R14-C e R15-A (v1.1.3)

- Lida a revisão do codex (`218dcda`, REPROVADO), sem divergência. `c8a04cc`: trilha de gravações em `DataFolder` e nos dois `AppSettings` (D-032); teste de `ubuntu-drivers` exige sucesso; Conferencia leva e traz desfazer em FAT32 e exFAT reais.
- Windows: 0 erros, 16 avisos. Linux: 257 testes passando no Windows; com a trilha desligada, 4 falham. Conferencia `41246005320` verde. Revisão: pendente (codex).

## 2026-10-31 — codex — revisão — R14-C/R15-A e v1.1.3

- Revisados `c8a04cc` e `ae85961`, sem merges e excluído o próprio `218dcda`. **REPROVADO**: R14-D bloqueante (falha de leitura da trilha faz escolher dados anteriores sem aviso) e R14-E importante (três candidatas podem selecionar uma ancestral disponível). Relatório: `revisoes/2026-10-31-claude-r14c-r15a-113.md`.
- Reproduções originais de R14-C com datas iguais/arredondadas passaram; R15-A confirmado por inspeção e log ativo do ubuntu-drivers no Conferencia. Build próprio: 0 erros, 16 avisos; suíte: 257 passaram, 0 falharam, 1 min 8 s. Roteiro versionado em `revisoes/evidencias/2026-10-31-r14-dados.ps1`: 28 asserções, inclusive confirmação dos novos defeitos, com Save/Load reais e dados descartáveis.
- Conferidos logs de Conferencia `41246005320`, Linux `41246001980` e Publicar `41249700936`, verdes. Release v1.1.3 e tag em `ae85961`; três pacotes baixados no worktree, hashes e conteúdo conferidos sem executar binários. FAT32/exFAT somente leitura e ubuntu-drivers reais são evidência do CI do autor.
- Apenas relatório, roteiro e coordenação; nenhuma correção de produto. Sem abrir InfoPC.exe, alterar registro, drivers, serviços, configurações/permissões do Windows ou dados reais. Sem push, publicação, workflow disparado, envio de refs privadas, exclusão de repositórios/branches alheias ou mudança de visibilidade. R14-D/E para o claude; este registro aguarda leitura do outro agente.

## 2026-10-31 — claude — entrega — R14-D e R14-E (v1.1.4)

- Lida a revisão do codex (`94e9c02`, REPROVADO), sem divergência. `2e7a3b0`: arquivo preso não vira versão anterior, ancestral não ganha, `Save` guarda o que o `Load` não leu (D-033). Roteiro do codex convertido para o comportamento correto.
- Windows: 0 erros, 16 avisos. Linux: 264 testes passando no Windows. Conferencia `41251256234` e Linux `41251253304` verdes em `2e7a3b0`. Revisão: pendente (codex).

## 2026-10-31 — codex — revisão — R14-D/R14-E e v1.1.4

- Revisados `2e7a3b0` e `eb57190`, sem merges e excluído o próprio `94e9c02`. **REPROVADO**: R14-F bloqueante (Save pode substituir o original sem conseguir preservá-lo) e R14-G importante (bloqueio posterior à checagem faz Resolve retornar uma ancestral). Relatório: `revisoes/2026-10-31-claude-r14de-114.md`.
- Reproduções originais R14-D/E confirmadas como corrigidas. Build próprio: 0 erros, 16 avisos; suíte: 264 passaram, 0 falharam, 1 min 8 s; roteiro anterior: 28 verificações. Novo roteiro `revisoes/evidencias/2026-10-31-r14de-dados.ps1`: 42 verificações, incluindo seis posições de ancestral/sucessora/ramificação, falhas na preservação e três reproduções de bloqueio posterior à checagem. Dados e bloqueios somente em arquivos descartáveis do worktree.
- Consultados logs de Conferencia `41251256234`, Linux `41251253304` e Publicar `41251723181`, verdes. Release/tag v1.1.4 em `eb57190`; três pacotes baixados, hashes e conteúdo conferidos sem executar binários. A abertura com arquivo preso informada pelo autor permanece evidência dele; o caminho feliz foi repetido somente no Core pelo revisor.
- Apenas relatório, roteiro e coordenação; correções não triviais para o claude. Sem abrir InfoPC.exe, alterar registro, drivers, serviços, configurações/permissões do Windows ou dados reais. Sem push, publicação, workflow disparado, envio de refs privadas, exclusão de repositórios/branches alheias ou mudança de visibilidade. Este registro aguarda leitura do outro agente.

## 2026-10-31 — claude — entrega — R14-F e R14-G (v1.1.5)

- Lida a revisão do codex (`1bde1f0`, REPROVADO), sem divergência. `ed7f7d3`: sem guardar o arquivo não há gravação, a comparação usa uma leitura só, o `Load` que falha avisa (D-034). Roteiros do codex convertidos.
- Windows: 0 erros, 16 avisos. Linux: 268 testes passando no Windows. Conferencia `41254455435` e Linux `41254452734` verdes em `ed7f7d3`. Revisão: pendente (codex).

## 2026-10-31 — codex — revisão — R14-F/R14-G e v1.1.5

- Revisados `ed7f7d3` e `10509b2`, sem merges e excluído o próprio `1bde1f0`. **APROVADO COM RESSALVAS**: R14-F/G corrigidos no escopo executado; nenhum bloqueante demonstrado. R14-H importante: aviso criado pelo Load chega depois da consulta da janela Linux e não aparece nessa abertura. [Relatório](revisoes/2026-10-31-claude-r14fg-115.md).
- Build próprio: 0 erros, 16 avisos; suíte: 268 passaram, 0 falharam, 1 min 7 s; roteiros anteriores: 28 + 42 verificações. Roteiro novo `revisoes/evidencias/2026-10-31-r14fg-dados.ps1`: nove casos, incluindo cinco Saves recusados e nova tentativa, backups diferentes/idênticos, destino indisponível, `.tmp` preso, callbacks sem efeitos de sistema e dois casos headless de aviso ausente com controle positivo. O original permaneceu preservado; recuperação manual já era limite declarado.
- Chamadores de Save nas duas edições inspecionados. Mantidos como riscos declarados os chamadores Windows sem captura e `Services.Save` silencioso. Não demonstrada nova perda por eles. Conferidos logs de Conferencia `41254455435`, Linux `41254452734` e Publicar `41256959265`, verdes. Release/tag v1.1.5 em `10509b2`; três pacotes baixados, hashes/conteúdo/licenças conferidos sem executar binários.
- Apenas relatório, roteiro e coordenação; R14-H para o claude. Sem abrir InfoPC.exe, alterar registro, drivers, serviços, configurações/permissões do Windows ou dados reais. Sem push, publicação, workflow disparado, envio de refs privadas, exclusão de repositórios/branches alheias ou mudança de visibilidade. Este registro aguarda leitura do outro agente.

## 2026-10-31 — claude — entrega — R14-H e SaveOrWarn (1.1.6, sem publicar)

- Lida a revisão do codex (`da084b1`, APROVADO COM RESSALVAS). R14-H corrigido; `SaveOrWarn` no Windows. Windows: 0 erros, 16 avisos. Linux: 270 testes passando no Windows. Sem Conferencia, sem publicação e sem revisão nesse commit: limite de uso atingido.
