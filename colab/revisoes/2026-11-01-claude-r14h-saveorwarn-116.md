# Revisão — R14-H, SaveOrWarn e v1.1.6

| Campo | Valor |
|---|---|
| Autor da entrega | claude |
| Revisor | codex |
| Data | 2026-11-01 |
| Intervalo revisado | `10509b2..0f56c5b`, somente `cd018a2` e `0f56c5b`, sem merges; diff `da084b1..0f56c5b` exclui o relatório do próprio revisor |
| Build/testes rodados pelo revisor | `dotnet build InfoPC.csproj -c Release -nologo --no-incremental`: **0 erros, 16 avisos**; `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release`: **270 passaram, 0 falharam**, 1 min 9 s; roteiros abaixo: **28 + 42 verificações, 9 + 6 casos** |
| **Veredito** | **APROVADO** |

## Achados

**Nenhum achado bloqueante ou importante demonstrado nesta entrega. R14-H corrigido nos dois cenários reproduzidos.** A aprovação cobre este intervalo e as verificações descritas; as pendências manuais anteriores continuam registradas.

| # | Severidade | Arquivo:linha | Problema | Sugestão |
|---|---|---|---|---|
| — | — | — | Nenhum novo defeito confirmado | — |

Os limites declarados no HANDOFF não foram reclassificados como achados: compatibilidade pela data entre versões antigas, trilha de 256 gravações, gravação ao voltar a versão antiga, divergência repetida com a antiga `dados`, pasta em uso ilegível, recuperação manual do `.corrompido` e vários avisos consecutivos de gravação. Os testes não demonstraram dano adicional ao que o autor descreveu. Não considerei uma corrida hipotética como perda ou ocultação de dados em uso plausível.

## R14-H e ordem de inicialização no Linux

O [roteiro anterior](evidencias/2026-10-31-r14fg-dados.ps1), convertido pelo autor, passou com **um diálogo** tanto para arquivo danificado como única candidata quanto para bloqueio após `Resolve`; o controle com aviso anterior também mostrou um diálogo. O original continuou preservado ao fechar, com recuperação manual conforme o limite declarado. Os dois testes novos de `UiTests` também passaram na suíte própria.

Inspecionei `MainWindow.cs:32`, `Services.cs:86`, `PageBase.cs:16`, `DashboardPage.cs:89`, `Dialogs.cs:33` e todos os chamadores de `LoadSettings`, `StartAsync` e `ApplyTheme`:

- `Opened` carrega antes da consulta a `FolderNotice`; o diálogo é aberto e a detecção de hardware continua sem esperar o usuário fechá-lo. `StartAsync` conserva a chamada a `LoadSettings` para quem não passa pela janela.
- `PageBase.S` consulta o serviço atual. O Dashboard já era construído antes do Load; recebe `Loaded` depois de configurações e hardware estarem disponíveis e atualiza os resultados. A construção das páginas e `ShowLogo` já precediam o Load na versão anterior; essa relação não foi introduzida por este diff.
- Não há consumidor externo de `_started` nem código de aplicação dependente de `ApplyTheme` ocorrer dentro de `StartAsync`. `Ready`, `WhenReady` e `Loaded` continuam associados ao fim da detecção. O tema é aplicado antes de abrir o aviso.

O [roteiro novo](evidencias/2026-11-01-r14h-saveorwarn.ps1) executou o serviço e a janela Avalonia reais, sem tela, com fixture `intel-desktop`:

| Cenário | Resultado próprio |
|---|---|
| Janela aberta do zero, tema claro e tema escuro salvos | Tema, preferências e histórico carregados; sensores da fixture disponíveis; `Loaded` ocorreu uma vez com `Ready=true`; nova chamada a Start não substituiu Settings |
| Load anterior e repetido, alteração ainda não salva, depois Start | Mesmo objeto e alteração em memória preservados; nenhuma escrita extra no arquivo |
| Start chamado sem Load prévio e sem janela | Configurações e tema carregados; detecção concluída |

A suíte de 270 testes também percorreu todas as páginas com as três fixtures. **Não encontrei regressão demonstrável dependente da ordem antiga.**

## SaveOrWarn no Windows

As sete substituições estão em `MainWindow.xaml.cs:500`, `:796`, `:1145`, `:1395`, `MainWindow.Launcher.cs:94`, `MainWindow.Overlay.cs:41` e `MainWindow.Oc.cs:523`. Protegem preferências e históricos; não substituem os callbacks de persistência obrigatória antes de alterações de sistema/Steam. Estes continuam usando `_settings.Save` e seus tratamentos anteriores.

`MainWindow.xaml.cs:1045` captura a exceção do Save, agenda o aviso no Dispatcher e deixa a alteração em memória para uma próxima gravação. As atualizações de tela/overlay e a busca de FPS solicitada pelo clique continuam depois do wrapper, coerentes com a mensagem de validade durante a sessão. O diff não adiciona ação de sistema ou acesso automático à internet.

Para não instanciar a janela WPF, o roteiro novo **extrai o corpo atual de SaveOrWarn sem editá-lo**, substitui Dispatcher/MessageBox por uma fila e um coletor e usa a persistência real do **Core**. Nos dois casos — arquivo preso depois de Load válido e Load já impedido pelo bloqueio — três Saves retornaram sem propagar a falha, três avisos foram enfileirados, o original permaneceu idêntico e a tentativa após liberar o arquivo gravou preferências e histórico. No Load impedido, o original foi preservado em `.corrompido`.

Isso verifica a lógica do wrapper; **não é um clique real no Windows nem execução do AppSettings Windows/DPAPI**. O Save Windows, não alterado no intervalo, foi inspecionado e compilado. Seu inicializador ajusta ACL; não o executei para respeitar a restrição de não alterar permissões/configurações deste PC.

**Evidência do autor, informada pelo usuário:** SALVAR CHAVE na v1.1.6, com `settings.json` preso, mostrou o aviso e o programa continuou aberto. Não repeti nem atribuo esse resultado ao revisor. A possibilidade de vários avisos seguidos já está declarada no HANDOFF e foi observada na simulação; não demonstrei outro dano.

## Observação de encerramento em Sensores

**Relato do autor transmitido pelo usuário:** uma passada pelas 21 páginas terminou em Sensores com código 0; outras três passadas seguidas foram limpas; não havia falha no log de eventos consultado pelo autor; o usuário usava o PC. A causa não foi confirmada. Não abri o programa nem consultei pessoalmente esses eventos.

Por inspeção de `MainWindow.xaml.cs:105`, `:163`, `:252`, `:254`, `:1431`, `SensorMonitor.cs` e da busca por encerramentos em todo o código Windows, **não encontrei um caminho que peça para encerrar o InfoPC ao entrar ou permanecer em Sensores**. A navegação apenas mostra a página; o timer lê sensores em todas as páginas. `Close_Click` fecha a janela por ação do usuário. O `Shutdown` de `App.xaml.cs:31` ocorre no início de uma segunda instância, não na navegação da instância aberta. Os demais Close/Shutdown encontrados fecham overlays, conexões ou objetos de bibliotecas.

`_monitor.Refresh()` no timer e a primeira atualização após `OpenAsync` não têm captura local das falhas da leitura; isso delimita a robustez existente, mas não identifica uma exceção concreta, nem explica o código 0. Não atribuí o episódio a esse caminho, a falha nativa, a temperatura ou a ação do usuário sem evidência. A ocorrência permanece **observação sem reprodução/causa**, registrada em TAREFAS, e não um bloqueante novo.

## Publicação, licenças e requisitos

Consultei metadados, jobs e logs completos, sem disparar workflows:

- [Conferencia 41266843058](https://github.com/victorhoura/InfoPC/actions/runs/41266843058), em `cd018a2`: oito jobs verdes. FAT32/exFAT ativos em **210 ms / 193 ms**; ubuntu-drivers ativo em **3 s**, além de polkit, hidepid, GNOME/KDE e pacotes arm64. Retornos antecipados de testes condicionais não foram contados como execução do recurso.
- [Linux 41266090925](https://github.com/victorhoura/InfoPC/actions/runs/41266090925), em `cd018a2`: sete jobs verdes; **270 testes** em cada Ubuntu 22.04/24.04, incluindo os dois casos novos do aviso; testes de pacotes nas cinco distribuições.
- [Publicar 41267072095](https://github.com/victorhoura/InfoPC/actions/runs/41267072095), em `0f56c5b`: cinco jobs verdes. Windows registra janela `InfoPC`, **184 MB**, e confere a migração; Linux abre nas duas arquiteturas em tela virtual, e x64 roda nos cinco contêineres. Esses são resultados do CI do autor que consultei, não execução local dos pacotes pelo revisor.

A [Release v1.1.6](https://github.com/victorhoura/InfoPC/releases/tag/v1.1.6) está publicada como pré-lançamento, sem rascunho. Tag e alvo conferem com **`0f56c5bbb25d46117042a3c401f0217c666d0b91`**. Baixei os três pacotes e `SHA256SUMS.txt`: os três hashes conferem. O [verificador estático](evidencias/2026-11-01-pacotes-116.py) confirmou, sem extrair/executar, um único executável da arquitetura correta mais `LEIA-ME.txt`, `LICENSE.txt` e `THIRD-PARTY-NOTICES.txt` em cada pacote. Os textos conferem com a árvore revisada, normalizando LF/CRLF.

`git diff --check da084b1 0f56c5b` passou. `Drivers/`, `LICENSE` e `THIRD-PARTY-NOTICES.txt` não mudaram. Nenhuma biblioteca ou código de terceiro novo, instalador, comando de sistema ou elevação nova. A busca dos seis termos de `docs/UPSTREAM.md` retornou zero ocorrências inesperadas. Créditos e a pendência documental anterior do DXIL continuam preservados. Não foi encontrada nova violação dos requisitos portáteis no intervalo.

## Execução reproduzível e limites

No worktree `C:\Users\Victor\.worktrees\InfoPC\codex`, executei:

```powershell
dotnet build InfoPC.csproj -c Release -nologo --no-incremental
dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-10-31-r14-dados.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-10-31-r14de-dados.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-10-31-r14fg-dados.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-11-01-r14h-saveorwarn.ps1
gh release download v1.1.6 --repo victorhoura/InfoPC --dir obj/review/release-116
python colab/revisoes/evidencias/2026-11-01-pacotes-116.py obj/review/release-116
```

A suíte e os roteiros tiveram TEMP/TMP e INFOPC_DADOS apontados para `obj/review` no processo de teste; CONFERENCIA, INFOPC_TEST_REAL_PACKAGES e INFOPC_SHOTS vazios. Os roteiros novos só usam dependências que o projeto já tem. Logs, TRX, projetos gerados e downloads são descartáveis no worktree; os roteiros e este resumo ficam versionados. A suíte tem o aviso preexistente xUnit2031. Testes condicionais de sistema real não executam essas ações neste Windows.

Não validei Linux físico, sensores/GPU/S.M.A.R.T. físicos, cancelamento do diálogo polkit real, FPS com chave, hardware RGB/Arduino ou os botões de alteração de sistema no PC. **Não abri InfoPC.exe nem alterei registro, drivers, serviços, configurações ou permissões do Windows, dados reais de AppData ou Steam.** Arquivos/bloqueios e o atributo de leitura do roteiro anterior ficaram em dados descartáveis do worktree. GitHub somente para leitura/download: sem push, publicação, envio de refs privadas, alteração de visibilidade ou exclusão de repositórios/branches alheias.

## Checklist

- [x] Faz o que a tarefa pedia, sem escopo extra
- [x] Build e testes passam, rodados pelo revisor
- [x] Requisitos portáteis preservados no intervalo e nos cenários verificados
- [x] Licença e créditos preservados
- [x] Sem restos de depuração, código morto novo ou temporários versionados no diff do autor
- [x] Diff localizado, sem reformatação desnecessária
- [x] HANDOFF, LOG e TAREFAS atualizados pelo autor; fechamento desta revisão registrado pelo codex

## Correções triviais feitas pelo revisor

Nenhuma correção de produto. Apenas relatório, roteiros de evidência e coordenação. O registro da revisão aguarda leitura do claude; não é aprovação do próprio trabalho.

## Resposta do autor

Aguardando leitura do claude. Não há achados novos a corrigir nesta revisão.
