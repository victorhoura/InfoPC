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
- [ ] Revisão cruzada da abertura em etapas dos sensores (D-035), dos campos em branco e da `v1.1.7` (`0f56c5b..main`, só commits do claude) (codex).
- [x] Revisão cruzada dos sensores em etapas (D-035), campos em branco e `v1.1.7` (`0f56c5b..5ec4d7f`, só commits do claude) (codex, 2026-11-01). **APROVADO**: nenhum bloqueante ou importante novo demonstrado; biblioteca de implementação inspecionada, 13 verificações sintéticas e pacotes/CI conferidos. Sem sensores físicos pelo revisor. [Relatório](revisoes/2026-11-01-claude-sensores-117.md).
- [x] Revisão cruzada da correção de R14-H, de `SaveOrWarn` e da `v1.1.6` (`10509b2..0f56c5b`, só commits do claude) (codex, 2026-11-01). **APROVADO**: R14-H confirmado, sem regressão demonstrada na ordem Load/ApplyTheme/Start; nenhum bloqueante ou importante novo. [Relatório](revisoes/2026-11-01-claude-r14h-saveorwarn-116.md).
- [x] Revisão cruzada da correção de R14-F e R14-G e da `v1.1.5` (`eb57190..10509b2`, só commits do claude) (codex, 2026-10-31). **APROVADO COM RESSALVAS**: R14-F/G confirmados, nenhum bloqueante; R14-H importante no aviso Linux. [Relatório](revisoes/2026-10-31-claude-r14fg-115.md).
- [x] Revisão cruzada da correção de R14-D e R14-E e da `v1.1.4` (`ae85961..eb57190`, só commits do claude) (codex, 2026-10-31). **REPROVADO**: reproduções originais corrigidas; R14-F bloqueante e R14-G importante. [Relatório](revisoes/2026-10-31-claude-r14de-114.md).
- [x] Revisão cruzada da correção de R14-C e R15-A e da `v1.1.3` (`218dcda..ae85961`, só commits do claude) (codex, 2026-10-31). **REPROVADO**: reproduções originais corrigidas; R14-D bloqueante e R14-E importante, relatório abaixo.
- [x] Revisão cruzada das correções da 1.1.2 e do workflow Conferencia (`8271e68..12b823f`) (codex, 2026-10-31). R15-A importante no teste de ubuntu-drivers; relatório abaixo.
- [x] Revisão cruzada da abertura do código (D-030): retirada dos drivers, `THIRD-PARTY-NOTICES.txt` e versão 1.1.1 (codex, 2026-10-31). Créditos e drivers mantidos preservados; ressalva documental do DXIL no relatório.
- [x] Revisão cruzada de R14-A/B, da identidade sem `libssl` e do workflow de publicação (`86f1d7b..1383196` da história privada) (codex, 2026-10-31). **REPROVADO**, R14-C bloqueante na seleção dos dados.
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

- [x] Configurações e dados na pasta do .exe em vez de `%AppData%\InfoPC` (requisito 3, D-028): R14-A a R14-H confirmados por codex nos cenários dos relatórios, até 2026-11-01. Revisão da 1.1.6 **APROVADO**, sem novo bloqueante. Abertura no PC real conferida pelo claude; não repetida pelo revisor por limite explícito do usuário. Limites D-032 a D-034 mantidos; Linux físico continua na Fase 4.
- [x] Auditoria completa de acesso à internet (requisito 5): resultado em `docs/ARQUITETURA.md`, "Acesso à internet" (claude)
- [x] Conferir que toda alteração no sistema tem "desfazer" (requisito 4): resultado em `docs/ARQUITETURA.md`, "Alterações no sistema e como desfazer" (claude)
- [x] **Requisito 5:** HowManyFPS e versões de driver só depois de um clique, nas duas edições (D-024): `e1e5354`, `9791bd7` (claude)
- [x] **Requisito 4:** correções entregues em `48fe432`, `2f2bbc4`, `764eb5a`, `4f99aaf` (D-025, claude); a revisão encontrou falhas de restauração/persistência. R02–R10 e complemento R04-C confirmados no escopo dos relatórios. Revisões até 2026-11-01 confirmam R14-A a R14-H, sem nova perda bloqueante demonstrada.
- [x] Revisão cruzada da correção de R10-C, do job de pacotes do CI e da pasta de dados (`fbfc2e4..86f1d7b`, só commits do claude e sem merges): **REPROVADO**, dois bloqueantes (R14-A/B); R10-C confirmado, relatório abaixo (codex, 2026-10-30).
- [x] Revisão cruzada das correções R04-C, R09-C, R10-A/B e R12-C (`467fbc0..fbfc2e4`): **REPROVADO**, um bloqueante (R10-C); relatório abaixo (codex, 2026-10-30).
- [x] Revisão cruzada da correção dos achados restantes: R03, R05, R07–R13, o mesmo padrão do R04 e três achados pequenos (`2050100..a797014`): **REPROVADO**, quatro bloqueantes e um importante; relatório abaixo (codex, 2026-10-29).
- [x] Revisão cruzada das correções R01, R02, R04, R05 e R06 (`b7e58dc..e328c68`), incluindo os commits posteriores do D-026 até `78e714a`: **REPROVADO**, R05 parcial; demais confirmados no escopo testado (codex, concluída em 2026-10-29; relatório abaixo).
- [x] Revisão cruzada da Fase 3 junto com todo o histórico até `9b168be`: **REPROVADO** (codex, 2026-10-28).
- [~] FPS estimado sem chave, Drivers com VERIFICAR TUDO, perfil de jogo e RESTAURAR ORIGINAL da Steam: conferidos no PC real (claude, 2026-10-31). Falta o FPS **com** chave, que depende de uma chave da HowManyFPS (usuário).
- [x] Conferir os termos de redistribuição dos drivers de `Drivers/` e as licenças "a conferir": feito, com CH340, CP210x e FTDI retirados (D-030, `docs/UPSTREAM.md`) (claude, 2026-10-30).
- [~] Gerar o pacote final (D-029, D-030): `v1.1.7` publicada como pré-lançamento no repositório público, com `InfoPC.exe` aberto em Windows limpo do CI e usado no PC real (claude, 2026-11-01). Revisão do codex **APROVADO**, com tag, logs e pacotes conferidos sem executar o programa (2026-11-01). Falta o teste em Linux físico (usuário).

## Fase 4 — Edição Linux (D-017 a D-023)

- [x] Correções dos travamentos/congelamentos/fechamento relatados no Linux: v1.1.8 integrada e publicada (codex, 2026-11-01). 277 testes locais e em cada Ubuntu do CI, probe e publicação completos; três pacotes/hashes conferidos. [Diagnóstico, evidências e limites](../docs/LINUX-ESTABILIDADE.md). Obsidian é a etapa final da sessão; revisão cruzada do claude pendente.
- [ ] Repetir o teste da v1.1.8 no Linux que apresentou o problema; se ocorrer novamente, correlacionar `erros.log`, saída do terminal e journal, identificando distribuição/GPU (usuário).
- [ ] Revisão cruzada da estabilidade Linux/v1.1.8 entregue pelo codex (claude).

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

- [ ] Observação sem causa confirmada: segundo o autor, em teste da v1.1.6 uma passada pelas 21 páginas fechou em Sensores com código 0; três outras passadas foram limpas, sem falha nos eventos consultados pelo autor, com o usuário usando o PC. Codex não encontrou caminho de encerramento automático ligado à página por inspeção (2026-11-01); não abriu o programa, por restrição do usuário. Não classificado como defeito bloqueante. [Registro e limites](revisoes/2026-11-01-claude-r14h-saveorwarn-116.md).
- [x] Apagar `victorhoura/InfoPC-antigo` (D-030): feito pelo usuário em 2026-11-01. A história antiga fica só nesta máquina, em `refs/historico-privado/`.
- [ ] PC real: em uso leve, CPU com média de 63,5 °C e pico de 73 °C (carga de 17%, 60 s). Sob carga total, 100 °C no benchmark e GPU com hot spot de 95 °C e `PerfCap` térmico (2026-10-31). Os "92 °C em repouso" dos registros anteriores não eram repouso: havia testes e compilações rodando. É do PC, não do programa: limpeza e pasta térmica (usuário).

Achados no PC real em 2026-10-31, sem mudança nesta entrega. Os dois primeiros foram medidos lado a lado com o original 0.16 e são iguais; o terceiro não foi comparado:
- [x] Windows: os sensores levavam cerca de 75 s para a primeira leitura neste PC, por causa da detecção de discos (64,7 s). Agora 2,7 s, medidos pelo autor (D-035, claude, 2026-11-01). Revisão do codex aprovada por inspeção/build; sem cronometragem ou sensores reais pelo revisor. [Relatório](revisoes/2026-11-01-claude-sensores-117.md).
- [ ] Windows: nas versões até a 1.1.2, a memória saltava de cerca de 310 MB para 790 MB depois de passar por várias páginas. Nas 1.1.6 e 1.1.7, o mesmo percurso termina em cerca de 320 MB, e parado fica em 240 MB por 200 s (2026-11-01). Não foi uma correção deliberada e a causa não foi identificada: abrir a Integração sozinha não causa, nem as capturas de tela do teste. Vigiar se voltar.
- [x] Windows: clock da CPU em branco na Visão geral, no Overlay e no Benchmark (`Core #` em vez de `CPU Core #`) e "Núcleo mais ocupado" no Gargalo (descartava as linhas `Thread`). Corrigido (claude, 2026-11-01); codex confirmou nomes Intel/AMD na biblioteca e comportamento com dados sintéticos, sem AMD físico, na revisão aprovada da v1.1.7.

Revisão de 2026-10-28: [histórico e Fase 3](revisoes/2026-10-28-claude-historico-e-fase3.md), R01–R13. Correções de comportamento cabem ao autor; mudanças relativas aos requisitos aguardam orientação do usuário (AGENTS.md, seções 2 e 5). A revisão está concluída; a aprovação da entrega depende dos achados.

- [x] Os testes do CI rodavam só no Ubuntu: o job `pacotes` de `linux.yml` agora roda lista, recuperação, desfazer, volta de versão (`--allow-downgrades`, `--oldpackage`) e grupos (`usermod`, `gpasswd`) em Debian, Ubuntu, Fedora, Arch e openSUSE (claude, 2026-10-30). Logs de `pkexec` e hidepid reais no Conferencia consultados por codex em 2026-10-31; sucesso de ubuntu-drivers confirmado após a correção de R15-A, no run `41246005320`.

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
- [x] R14-A — **BLOQUEANTE:** fallback Linux ignorado na migração do D-028. Corrigido em `9f59043` (claude); codex confirmou com Save/Load e `AppSettings.ResolveFolder()` reais em diretórios descartáveis, 2026-10-31.
- [x] R14-B — **BLOQUEANTE:** retorno direto a fallback antigo/vazio. Correção de `9f59043` confirmada pelo codex em 2026-10-31 para dados com datas distintas, nos dois sentidos. A falha complementar com datas iguais/arredondadas foi corrigida e revisada em R14-C abaixo; complementos R14-F/G também confirmados na revisão da 1.1.5.
- [x] R14-C — **BLOQUEANTE:** seleção pela data com empate/arredondamento. Corrigido em `c8a04cc` (claude, 2026-10-31; D-032); codex repetiu as duas reproduções originais com Save/Load e confirmou ida e volta sem perder registros. Complemento R14-G confirmado na revisão da 1.1.5. [Revisão da correção](revisoes/2026-10-31-claude-r14c-r15a-113.md).
- [x] R15-A — **IMPORTANTE:** teste de `ubuntu-drivers devices` aceitava erro quando o comando estava instalado. Corrigido em `c8a04cc`; codex confirmou asserção de sucesso/timeout e execução ativa no modo `polkit-sim` do run `41246005320`, em 2026-10-31. Mesmo relatório de R14-C.
- [x] R14-D — **BLOQUEANTE:** falha ao ler a trilha vira ausência de trilha e faz a cópia anterior ganhar sem aviso. [Relatório e roteiro originais](revisoes/2026-10-31-claude-r14c-r15a-113.md). Corrigido em `2e7a3b0` (claude, 2026-10-31; D-033); codex confirmou os bloqueios iniciais nos dois sentidos. O caso novo de bloqueio posterior à checagem está em R14-G.
- [x] R14-E — **IMPORTANTE:** redução sequencial podia escolher uma ancestral entre três candidatas e avisar usando o caminho antigo. Corrigido em `2e7a3b0` (claude, 2026-10-31; D-033); codex confirmou a reprodução original e seis posições de ancestral/sucessora/ramificação, com preservação e caminho ativo correto, 2026-10-31.
- [x] R14-F — **BLOQUEANTE:** Save após Load ilegível podia sobrescrever o original sem preservá-lo após falha. Corrigido em `ed7f7d3` (claude, 2026-10-31; D-034); codex confirmou as reproduções originais, cinco tentativas com arquivo preso, backup anterior diferente/idêntico, destino indisponível e falha posterior no `.tmp`. Core executado; Windows inspecionado/compilado. [Revisão](revisoes/2026-10-31-claude-r14fg-115.md).
- [x] R14-G — **IMPORTANTE:** releitura posterior a `CanRead` fazia `Resolve` devolver fallback ancestral depois de novo bloqueio da portátil. Corrigido em `ed7f7d3` (claude, 2026-10-31; D-034); codex confirmou três reproduções mantendo a portátil posterior, sem aviso indevido de cópia. Mesmo relatório de R14-F.
- [x] R14-H — **IMPORTANTE:** aviso de Load falho não aparecia na primeira abertura Linux. Corrigido em `cd018a2` (claude, 2026-10-31); codex confirmou em 2026-11-01 os dois cenários originais e o controle positivo com um diálogo, mais temas e carregamento único. Original preservado; recuperação manual continua sendo o limite declarado. [Revisão da correção](revisoes/2026-11-01-claude-r14h-saveorwarn-116.md).

- 16 avisos de nulabilidade no build do original (`MainWindow.Oc.cs`, `DiskHealth.cs`). Não corrigir de carona.
- Não há testes automatizados no original (a edição Linux tem a suíte em `linux/tests`).
- Edição Linux, ideias para depois: overlay de FPS próprio (hoje só pelo MangoHud), suporte a Wayland puro no overlay, leitura de FPS sem injetar código, conferir o início de jogos do Heroic e os nomes de pacote do catálogo de apps em cada distribuição.
- ~~Instalador do Linux: o `Exec` do `.desktop` não escapa `%` no caminho de instalação~~ Resolvido em `7a6179a` (claude, 2026-10-29).
- ~~`scripts/verificar-nome-antigo.ps1` não diferencia maiúsculas~~ Resolvido na Fase 2: opção `-DiferenciarMaiusculas`.
- A versão aparece em dois lugares: `<Version>` do `InfoPC.csproj` e o texto `SideVersion` do `MainWindow.xaml` (já era assim no original). Avaliar ler a versão do assembly.
- O registro no Razer Chroma exige um "contato"; ficou "InfoPC" porque o projeto não tem site nem e-mail público.
- ~~`GameLauncher.RunAsync`: quando o processo do jogo não é detectado, a mensagem diz "Os ajustes continuam aplicados"~~ Resolvido em `42317fa` (claude, 2026-10-29).
- ~~O plano "Alto desempenho" criado por `-duplicatescheme` não é apagado ao restaurar~~ Resolvido em `8224b79` (Otimização) e `42317fa` (perfis de jogo) (claude, 2026-10-29).
