# TAREFAS

Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída · responsável entre parênteses.

## Fase 0 — Base do projeto

- [x] Criar a estrutura de colaboração Claude + Codex (claude)
- [x] Confirmar a estrutura (usuário)
- [x] Revisar a estrutura base, junto com a revisão da importação (codex, 2026-10-28; relatório abaixo)
- [x] Os dois agentes ao mesmo tempo com git worktree, D-026: `a784d64`, `78e714a` (claude; revisado por codex em 2026-10-29, sem achado)

## Fase 1 — Importação do código base

- [x] Enviar o link do projeto de código aberto (usuário)
- [x] Conferir a licença e preencher `docs/UPSTREAM.md`: MIT (claude)
- [x] Decidir a estratégia de git: começar do zero, D-005 (usuário)
- [x] Importar o código sem alterações: 119 de 121 arquivos idênticos ao original (claude)
- [x] Mesclar `.gitignore` e `.gitattributes` com os do original (claude)
- [x] Compilar sem alterações: 0 erros, 16 avisos; pacote portátil de ~197 MB (claude)
- [x] Mapear a arquitetura em `docs/ARQUITETURA.md` (claude)
- [x] Listar onde o código viola os requisitos: ver Fase 3 (claude)
- [ ] Revisão cruzada das correções da 1.1.2 e do workflow Conferencia (`8271e68..main`) (codex). Comando no HANDOFF.
- [ ] Revisão cruzada da abertura do código (D-030): retirada dos drivers, `THIRD-PARTY-NOTICES.txt` e versão 1.1.1 (codex). Comando no HANDOFF.
- [ ] Revisão cruzada de R14-A/B, da identidade sem `libssl` e do workflow de publicação (`86f1d7b..` até o último commit de documentação do claude) (codex).
- [x] Revisão cruzada da importação (codex, 2026-10-28; 119/121 arquivos conferidos com o upstream, ressalvas do código no relatório)
- [ ] ~~Abrir o programa original uma vez~~ Substituído pelo teste do InfoPC, na Fase 2: o usuário pediu a renomeação antes

## Fase 2 — Renomeação para InfoPC

- [x] Decidir a versão inicial (1.0.0, D-011) e trocar também o namespace (D-012) (usuário)
- [x] Criar logo e ícone provisórios (D-008, D-016) (claude)
- [x] Renomear seguindo `docs/UPSTREAM.md`, seções 5 e 6 (claude)
- [x] Script sem ocorrências inesperadas para cada termo de `docs/UPSTREAM.md`, seção 4 (claude)
- [x] Compilar (0 erros, 16 avisos) e gerar o pacote portátil `dist\InfoPC.exe` (claude)
- [x] Hospedar no GitHub do usuário, repositório privado (D-014) (claude)
- [x] Abrir o `InfoPC.exe` e conferir que as abas carregam, o logo aparece e o título diz InfoPC: as 21 páginas no PC real, sem conexão externa nem mudança no sistema ao abrir (claude, 2026-10-31).
- [x] Revisão cruzada da importação e da renomeação, até `9b168be` inclusive (codex, 2026-10-28; relatório abaixo)

## Fase 3 — Ajustes aos requisitos (AGENTS.md, seção 2)

- [~] Configurações e dados na pasta do .exe em vez de `%AppData%\InfoPC` (requisito 3, D-028): `416ae15` (claude), revisão concluída pelo codex em 2026-10-30. R14-A/B pendentes: dados do fallback Linux ignorados na migração e retorno a fallback antigo/vazio depois de gravar na pasta portátil. Teste manual do `InfoPC.exe` ainda pendente (usuário; pede administrador).
- [x] Auditoria completa de acesso à internet (requisito 5): resultado em `docs/ARQUITETURA.md`, "Acesso à internet" (claude)
- [x] Conferir que toda alteração no sistema tem "desfazer" (requisito 4): resultado em `docs/ARQUITETURA.md`, "Alterações no sistema e como desfazer" (claude)
- [x] **Requisito 5:** HowManyFPS e versões de driver só depois de um clique, nas duas edições (D-024): `e1e5354`, `9791bd7` (claude)
- [x] **Requisito 4:** correções entregues em `48fe432`, `2f2bbc4`, `764eb5a`, `4f99aaf` (D-025, claude); a revisão encontrou falhas de restauração/persistência. Revisado até `86f1d7b`: R02–R10 e complemento R04-C confirmados no escopo dos relatórios; a nova migração de dados ainda tem R14-A/B pendentes (codex, 2026-10-30).
- [x] Revisão cruzada da correção de R10-C, do job de pacotes do CI e da pasta de dados (`fbfc2e4..86f1d7b`, só commits do claude e sem merges): **REPROVADO**, dois bloqueantes (R14-A/B); R10-C confirmado, relatório abaixo (codex, 2026-10-30).
- [x] Revisão cruzada das correções R04-C, R09-C, R10-A/B e R12-C (`467fbc0..fbfc2e4`): **REPROVADO**, um bloqueante (R10-C); relatório abaixo (codex, 2026-10-30).
- [x] Revisão cruzada da correção dos achados restantes: R03, R05, R07–R13, o mesmo padrão do R04 e três achados pequenos (`2050100..a797014`): **REPROVADO**, quatro bloqueantes e um importante; relatório abaixo (codex, 2026-10-29).
- [x] Revisão cruzada das correções R01, R02, R04, R05 e R06 (`b7e58dc..e328c68`), incluindo os commits posteriores do D-026 até `78e714a`: **REPROVADO**, R05 parcial; demais confirmados no escopo testado (codex, concluída em 2026-10-29; relatório abaixo).
- [x] Revisão cruzada da Fase 3 junto com todo o histórico até `9b168be`: **REPROVADO** (codex, 2026-10-28).
- [~] FPS estimado sem chave, Drivers com VERIFICAR TUDO, perfil de jogo e RESTAURAR ORIGINAL da Steam: conferidos no PC real (claude, 2026-10-31). Falta o FPS **com** chave, que depende de uma chave da HowManyFPS (usuário).
- [x] Conferir os termos de redistribuição dos drivers de `Drivers/` e as licenças "a conferir": feito, com CH340, CP210x e FTDI retirados (D-030, `docs/UPSTREAM.md`) (claude, 2026-10-30).
- [~] Gerar o pacote final (D-029, D-030): `v1.1.2` publicada como pré-lançamento no repositório público, com `InfoPC.exe` aberto em Windows limpo do CI e usado no PC real (claude, 2026-10-31).

## Fase 4 — Edição Linux (D-017 a D-023)

- [x] Decisões D-017 a D-023 e esqueleto `linux/` (Core, app Avalonia, testes); build do Windows intacto (claude)
- [x] Separar modelos portáteis dos arquivos do Windows (DiskHealthCard, BottleneckModel, rodapé do relatório) (claude)
- [x] Núcleo Linux: sistema, CPU, memória, DMI, sensores (hwmon, RAPL), GPU (NVML, amdgpu, Intel), discos e S.M.A.R.T., energia, rede (claude)
- [x] Modo texto: `InfoPC --relatorio`, `--json`, `--sensores`, `--versao` (claude)
- [x] Interface Avalonia: todas as páginas do menu, tema escuro/claro, avisos de dispositivo (claude)
- [x] Jogos, benchmarks, stress test, overclock, otimização, inicialização, apps, drivers, iluminação, Maker (claude)
- [x] `linux/instalar.sh`, `linux/desinstalar.sh`, atalho `.desktop` (claude)
- [x] CI em Ubuntu, Debian, Fedora, Arch e openSUSE, verde (claude)
- [x] `docs/LINUX.md`, README, NOVIDADES, ARQUITETURA, licenças em UPSTREAM; versão 1.1.0 no Windows (claude)
- [~] Teste em Linux: `pkexec`, `ubuntu-drivers`, hidepid, FAT32, exFAT e janela em GNOME e KDE conferidos em máquina virtual (D-031). Falta hardware físico: sensores, S.M.A.R.T. e GPU de verdade (usuário).
- [x] Revisão cruzada da edição Linux até `9b168be`: **REPROVADO**, correções pendentes abaixo (codex, 2026-10-28).

## Achados (fora do escopo da tarefa em que foram vistos)

Achados no PC real em 2026-10-31, sem mudança nesta entrega. Os dois primeiros foram medidos lado a lado com o original 0.16 e são iguais; o terceiro não foi comparado:
- [ ] Windows: os sensores levam cerca de 75 s para a primeira leitura neste PC (73,5 s no original, 74,8 s no InfoPC). Até lá, a Visão geral diz "sem leitura da CPU" e Sensores fica em "Detectando o hardware...".
- [ ] Windows: a memória salta de cerca de 280 MB para 760 MB depois de abrir Drivers ou Integração.
- [ ] Windows: "Clock médio" do Benchmark e "Núcleo mais ocupado" do Gargalo ficam em branco neste PC (i5-10400).

Revisão de 2026-10-28: [histórico e Fase 3](revisoes/2026-10-28-claude-historico-e-fase3.md), R01–R13. Correções de comportamento cabem ao autor; mudanças relativas aos requisitos aguardam orientação do usuário (AGENTS.md, seções 2 e 5). A revisão está concluída; a aprovação da entrega depende dos achados.

- [x] Os testes do CI rodavam só no Ubuntu: o job `pacotes` de `linux.yml` agora roda lista, recuperação, desfazer, volta de versão (`--allow-downgrades`, `--oldpackage`) e grupos (`usermod`, `gpasswd`) em Debian, Ubuntu, Fedora, Arch e openSUSE (claude, 2026-10-30). Continua sem execução real: `pkexec`, `ubuntu-drivers` e hidepid.

Revisão direcionada das correções: [principais achados](revisoes/2026-10-29-claude-correcao-achados-principais.md), correções até `e328c68` e fluxo de worktrees até `78e714a`. Quatro confirmados; R05 continua parcial.

Segunda rodada do autor (claude, 2026-10-29): correções dos achados restantes a pedido do usuário. As respostas estão nos dois relatórios, em "Resposta do autor". Revisão do codex concluída até `a797014`: **REPROVADO**, [relatório dos achados restantes](revisoes/2026-10-29-claude-achados-restantes.md), com R04-C, R09-C, R10-A/B e R12-C. As correções confirmadas e as limitações dos testes estão no relatório.

Terceira rodada do autor (claude, 2026-10-30): R04-C, R09-C, R10-A/B e R12-C corrigidos, sem divergência; respostas no mesmo relatório. Revisão do codex concluída até `fbfc2e4`: **REPROVADO**, [correção dos cinco achados](revisoes/2026-10-30-claude-correcao-cinco-achados.md). R04-C, R09-C, R12-C e identidade de pacotes confirmados; R10-C ainda perde o desfazer ao recuperar com o instalador ativo.

Quarta rodada do autor (claude, 2026-10-30): R10-C corrigido, sem divergência; resposta no mesmo relatório. Revisão do codex concluída até `86f1d7b`: **REPROVADO**, [R10-C e dados portáteis](revisoes/2026-10-30-claude-r10c-e-dados-portateis.md). R10-C confirmado no escopo testado; R14-A/B pendentes na seleção/migração dos dados. CI com gerenciadores reais e instalador consultado pelo revisor, sem nova execução disparada.

Achado fora da revisão (claude, 2026-10-30): com os dados junto do programa, o desfazer de um PC poderia ser aplicado em outro, e o caminho absoluto de `StartupBackup.Backup` quebrava ao mover a pasta. Corrigidos em `416ae15`. O mesmo valia para a pasta `dados/` do Linux desde o D-020.

- [x] R01 — **IMPORTANTE:** manter verificações de temperatura, erros e duração do stress Linux ao navegar para outra página. `75c9f2c` (claude; confirmado por codex, 2026-10-28)
- [x] R02 — **BLOQUEANTE:** associar o backup/restauração da Steam à instalação e à conta original, nas duas edições. `ad3c561` (claude; confirmado por codex, 2026-10-28; execução no Core Linux, Windows inspecionado/compilado)
- [x] R03 — **BLOQUEANTE:** corrigir a ordem de `SPI_SETMOUSE` e a persistência dos valores originalmente ausentes ao restaurar o mouse. `e2c4078` (claude; codex confirmou por inspeção/contrato da API e build, 2026-10-29; sem escrita real no mouse).
- [x] R04 — **BLOQUEANTE:** garantir backup durável antes de alterar registro/opções da Steam; não ignorar falhas de gravação. `ad3c561`, `9b81cf3` (claude; confirmado por codex nos cenários do relatório, 2026-10-28)
- [x] R05 — **BLOQUEANTE:** preservar backups dos ajustes do Windows cuja restauração falhou. `8224b79`, `42317fa` (claude; codex confirmou com código de saída não zero, timeout, nova tentativa e aviso do `null` legado, 2026-10-29; perfil de jogo inspecionado/compilado).
- [x] R06 — **BLOQUEANTE:** impedir que `desinstalar.sh --tudo` apague pastas alheias contendo arquivos como `..keep`. `1d95c3e` (claude; confirmado por codex em 12 casos no Git Bash, 2026-10-28; CI Linux pendente)
- [x] R07 — **BLOQUEANTE:** manter serviços systemd desativados na lista, com opção de reativar após atualizar/reabrir. `d6624c2` (claude; codex confirmou com salvar/reabrir e systemctl simulado, 2026-10-29).
- [x] R08 — **IMPORTANTE:** REMOVER uma sobreposição de autostart do usuário não pode reativar a entrada do sistema. `d6624c2` (claude; codex confirmou remoção e restauração do conteúdo após reabrir, 2026-10-29).
- [x] R09 — **BLOQUEANTE:** `02f54fb` oferece desfazer; R09-C (associação anterior removida) corrigido em `418bcdb` (claude; codex confirmou associação anterior preservada e recusa sem leitura, 2026-10-30).
- [x] R10 — **BLOQUEANTE:** firmware conforme D-027 confirmado. Lista anterior persistida e identidade completa confirmadas pelo codex em `db7795f`. R10-C corrigido em `be78859` e `ff67845` (claude; codex confirmou com `Save/Load`, processos simulados e consulta dos logs do CI em cinco distribuições, 2026-10-30). `pkexec`, `ubuntu-drivers` e hidepid reais continuam fora do escopo executado.
- [x] R11 — **IMPORTANTE:** continuar atualizando o overlay Linux quando a página Overlay ficar oculta. `bfdb0ad` (claude; teste headless passou na execução do codex, 2026-10-29; sem desktop Linux).
- [x] R12 — **IMPORTANTE:** três saídas rápidas corrigidas em `eeede1d`; R12-C (saída com erro depois de 3 s) corrigido em `960b230` (claude; codex confirmou na suíte com processos falsos, 2026-10-30).
- [x] R13 — **IMPORTANTE:** não concluir ausência de erros de hardware quando o log do kernel não pôde ser lido. `eeede1d` (claude; codex confirmou INCONCLUSIVO sem log e precedência de falha real, 2026-10-29).
- [x] Mesmo padrão do R04 em outros desfazer: COM/inicialização Windows (`b04a026`) inspecionados/compilados; Otimizador e inicialização Linux (`766436a`, `d6624c2`) com testes passando. R04-C (leitura parcial na Otimização Windows) corrigido em `aea3c39` (claude; codex confirmou captura revertida, nova tentativa com persistência antes da escrita e preservação de backups anteriores, 2026-10-30; sem alterar o plano real).
- [~] R14-A — **BLOQUEANTE:** a migração do D-028 (`416ae15`) só considera a antiga pasta `dados/` do Linux. Dados existentes em `$XDG_DATA_HOME/InfoPC` ou `~/.local/share/InfoPC` ficam ignorados quando a pasta do executável se torna gravável, incluindo registros de desfazer. Incluir essa origem e cobrir migração pelo resolver real (codex, reprodução em 2026-10-30; claude corrige). Corrigido em `9f59043` (claude, 2026-10-30). Aguarda revisão do codex.
- [~] R14-B — **BLOQUEANTE:** `DataFolder.Resolve` volta diretamente ao fallback quando a pasta portátil não pode ser gravada. Depois da migração Windows, isso carrega o `%AppData%` antigo, sem os backups criados mais tarde na pasta portátil; com fallback ausente, carrega configurações vazias. Preservar os dados ativos ao mudar de pasta e avisar quando não for possível, sem assumir um estado antigo como atual (codex, reprodução com arquivos reais e falha de escrita simulada em 2026-10-30; claude corrige). Corrigido em `9f59043` (claude, 2026-10-30). Aguarda revisão do codex.

- 16 avisos de nulabilidade no build do original (`MainWindow.Oc.cs`, `DiskHealth.cs`). Não corrigir de carona.
- Não há testes automatizados no original (a edição Linux tem a suíte em `linux/tests`).
- Edição Linux, ideias para depois: overlay de FPS próprio (hoje só pelo MangoHud), suporte a Wayland puro no overlay, leitura de FPS sem injetar código, conferir o início de jogos do Heroic e os nomes de pacote do catálogo de apps em cada distribuição.
- ~~Instalador do Linux: o `Exec` do `.desktop` não escapa `%` no caminho de instalação~~ Resolvido em `7a6179a` (claude, 2026-10-29).
- ~~`scripts/verificar-nome-antigo.ps1` não diferencia maiúsculas~~ Resolvido na Fase 2: opção `-DiferenciarMaiusculas`.
- A versão aparece em dois lugares: `<Version>` do `InfoPC.csproj` e o texto `SideVersion` do `MainWindow.xaml` (já era assim no original). Avaliar ler a versão do assembly.
- O registro no Razer Chroma exige um "contato"; ficou "InfoPC" porque o projeto não tem site nem e-mail público.
- ~~`GameLauncher.RunAsync`: quando o processo do jogo não é detectado, a mensagem diz "Os ajustes continuam aplicados"~~ Resolvido em `42317fa` (claude, 2026-10-29).
- ~~O plano "Alto desempenho" criado por `-duplicatescheme` não é apagado ao restaurar~~ Resolvido em `8224b79` (Otimização) e `42317fa` (perfis de jogo) (claude, 2026-10-29).
