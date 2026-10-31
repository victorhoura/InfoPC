# Revisão — correções dos achados restantes

| Campo | Valor |
|-------|-------|
| Autor da entrega | claude |
| Revisor | codex |
| Data | 2026-10-29 |
| Intervalo revisado | `2050100..a79701444bb0e282b22e60e7560abffe23e85251` — 14 commits do claude, sem merges |
| Build/testes rodados pelo revisor | Windows: 0 erros, 16 avisos; suíte Linux executada no Windows: 177 passaram; reproduções isoladas abaixo |
| **Veredito** | **REPROVADO** — quatro achados bloqueantes e um importante, em quatro áreas |

R03, R05, R07, R08, R11 e R13 tiveram as correções confirmadas no escopo indicado abaixo. O cenário original de R12, com três saídas rápidas, também foi corrigido. Permanecem falhas no backup parcial do plano de energia, no desfazer de grupo e de pacotes, e na identificação de saídas com erro mais demoradas da carga de GPU. Nenhum arquivo do produto foi corrigido nesta revisão.

## Achados

Severidades conforme o modelo do projeto: **BLOQUEANTE** inclui violação do requisito 4 (alteração com desfazer); **IMPORTANTE** inclui resultado de diagnóstico incorreto.

| # | Severidade | Arquivo:linha | Problema | Sugestão |
|---|------------|---------------|----------|----------|
| R04-C | **BLOQUEANTE** | `WindowsTweaks.cs:172`, `WindowsTweaks.cs:50` | `Remember` pode acrescentar `power` e lançar ao consultar `/list`. `Apply` só calcula e desfaz os acréscimos depois que `Remember` retorna. O backup parcial fica na memória, sem persistir. Num segundo clique, com `/list` funcionando e Alto desempenho já existente, nenhuma chave é nova: `persist` é pulado e `/setactive` roda sem original durável. | Reverter também os acréscimos de uma leitura que falhou, ou usar uma captura temporária que só entre no estado após concluir e persistir. Cobrir falha em `/list` seguida de nova tentativa, sem outra gravação intermediária. |
| R09-C | **BLOQUEANTE** | `linux/src/InfoPC.Core/Tools/Maker.cs:172`, `linux/src/InfoPC.Linux/Pages/MakerPages.cs:56` | `GroupsAdded` verifica somente o registro do InfoPC, não a associação anterior. Se o usuário já estiver em `/etc/group`, mas ainda não tiver renovado a sessão, a página oferece ADICIONAR. O `usermod` não acrescenta nada, mas o grupo é registrado como novo. TIRAR DO GRUPO depois remove uma associação anterior ao InfoPC. | Conferir a associação persistente antes de registrar; quando ela já existir, orientar renovar a sessão e não criar um desfazer que a remova. Falha de leitura também deve ser distinta de ausência. |
| R10-A | **BLOQUEANTE** | `linux/src/InfoPC.Core/Tools/Packages.cs:62` | O estado anterior só existe numa variável local até depois de `action()`. Se a leitura posterior falhar, a alteração já ocorreu e nenhum registro fica em `PackageChanges`, nem há comando de recuperação com os pacotes anteriores. Se a gravação posterior falhar ou o app encerrar, o estado também não foi protegido antes da mudança. O aviso posterior não satisfaz o desfazer exigido; D-027 não abriu exceção. Afeta GPU e remoção do brltty. | Persistir o estado anterior e uma operação pendente antes de executar. Se isso falhar, não alterar; se a leitura posterior falhar/interromper, conservar dados suficientes para completar a recuperação ao reabrir. |
| R10-B | **BLOQUEANTE** | `linux/src/InfoPC.Core/Tools/Packages.cs:32`, `linux/src/InfoPC.Core/Tools/Packages.cs:48` | O inventário usa `${Package}` e um `HashSet<string>`, perdendo arquitetura e versão. Se `libnvidia-gl-550:amd64` já existir e a ação acrescentar a variante i386, as duas linhas viram o mesmo nome: a diferença fica vazia e não há DESFAZER nem aviso. Trocas de versão sob o mesmo nome também ficam invisíveis. O desfazer por nome simples não identifica qual variante devolver/remover. | Guardar identidade completa do pacote (nome, arquitetura e versão) e comparar/restaurar essa identidade. Adequar a validação, que hoje também rejeita `:`. Testar amd64 já presente + i386 novo, além de atualizações sob o mesmo nome. |
| R12-C | **IMPORTANTE** | `linux/src/InfoPC.Core/Bench/GpuBench.cs:71` | O worker ignora `ExitCode`. Se o programa sair com erro após três segundos, `quick` volta a zero, o laço reinicia indefinidamente e `Failed` continua falso. O consumidor não recebe a falha e ainda pode concluir ESTÁVEL. A correção cobre o encerramento do laço por três saídas rápidas, mas deixa de fora a falha mais demorada. | Distinguir o término normal de uma rodada do vkmark de uma saída malsucedida; propagar o código/motivo ao stress e ao verificador. Cobrir uma saída não zero após o limite de três segundos. |

## Evidências próprias e reprodução

Os programas descartáveis foram criados em `obj/review/` no worktree do codex. O projeto Windows linkou **`WindowsTweaks.cs` real, sem reescrever seu texto**; somente `Process` e `AppSettings` foram substituídos por objetos mínimos. O programa Core referenciou **`InfoPC.Core.csproj` real**, com `Shell.Override` e `Elevate.Override`; `/etc`, home e configurações apontaram para arquivos fictícios dentro do worktree. Nenhum plano, grupo, pacote ou registro real foi alterado.

### R04-C — leitura parcial seguida de nova tentativa

1. Criar configurações sem `TweakBackup` e obter o ajuste real `WindowsTweaks.All().Single(t => t.Id == "power")`.
2. Simular `/getactivescheme` com um GUID válido e `/list` com `ExitCode=1`.
3. Chamar `Apply`: ele lança; `power` fica na memória e nenhuma gravação é chamada.
4. Fazer `/list` devolver o GUID de Alto desempenho e repetir `Apply`, com `persist` configurado para contar chamadas e lançar `IOException` se chamado.
5. A chamada termina normalmente e executa `/setactive`; `persist` não é chamado.

Saída: `R04 PARTIAL REMEMBER BUG: save calls=0; setactive=True; original only in memory=True`. A leitura do handler `Tweak_Click` confirma que o tratamento da primeira falha só atualiza estado/texto; não há gravação intermediária obrigatória que proteja esse original.

### R09-C — associação já existente

Arquivo fictício inicial: `dialout:x:20:tester,other`. Sessão simulada: `id` sem `dialout`. Configurações sem `GroupsAdded`.

Chamar `AddUserToGroup("dialout", settings, settings.Save, "tester")` e depois `RemoveUserFromGroup` com os mesmos argumentos. O registro real de configurações é salvo na pasta fictícia; o simulador do `gpasswd` remove somente `tester`.

Saída: `R09 EXISTING MEMBER BUG: existing member was recorded and removed: usermod -aG dialout tester; gpasswd -d tester dialout`. O arquivo final é `dialout:x:20:other`. A condição do botão considera os grupos da sessão, mesmo quando o diagnóstico já sabe que falta renovar a sessão.

### R10-A — falha da leitura posterior

Simular a primeira consulta `dpkg-query` com sucesso, a ação elevada com sucesso e a segunda consulta com código 1. Executar o método real `DriverInfo.InstallUbuntuDrivers` e contar chamadas a `persist`.

Saída: `R10 AFTER SNAPSHOT BUG: system changed=True; persisted before=0; undo records=0; warning=Não consegui listar os pacotes depois, então esta ação ficou sem desfazer.` A leitura anterior continua apenas na variável local `before`; não existe operação pendente a recuperar. A suíte entregue pelo autor também confirma explicitamente que uma gravação que falha **depois** permite a instalação e só gera comando manual.

### R10-B — nomes iguais, estados diferentes

Com a consulta usada pelo código, o cenário amd64 antes / amd64+i386 depois devolve:

```text
# antes
install ok installed libnvidia-gl-550
# depois
install ok installed libnvidia-gl-550
install ok installed libnvidia-gl-550
```

O programa real produz `R10 MULTIARCH BUG: added i386 next to amd64; names collapsed; undo records=0; warning=null`. Uma segunda simulação de atualização `550.90 -> 550.120` sob `nvidia-driver-550` também não gerou registro. Essa segunda execução demonstra a perda de informação no rastreador; não é uma instalação real de driver.

O contrato de `${binary:Package}` inclui o qualificador de arquitetura quando necessário, enquanto `Version` é outro campo; ambos são omitidos pela consulta atual. Isso foi conferido no [manual do dpkg-query](https://manpages.debian.org/bookworm/dpkg/dpkg-query.1.en.html). A inferência de impacto é a partir dessa representação e da comparação por conjuntos do código, reproduzida com as saídas acima.

### R12-C — falha depois de três segundos

O próprio executável descartável tem um modo filho: aguarda 3,4 s, escreve `exit=42` num arquivo fictício e termina com código 42. O pai passa seu caminho a `GpuStress.Start`, espera duas saídas registradas e lê as propriedades. Em `finally`, chama `Stop`, encerrando a última instância.

Saída: `R12 SLOW EXIT BUG: worker exited 42 twice; Running=True; Failed=False; Error=null`. A saída do worker não é examinada; somente a duração decide se as tentativas contam como rápidas. O stress e `OcPage` agora observam `Failed`, mas o valor não fica verdadeiro neste caminho.

## Correções confirmadas e limites

| Item | Resultado da conferência |
|------|--------------------------|
| R03 | Código envia limiar 1, limiar 2, aceleração; usa só `SPIF_SENDCHANGE`, sem `SPIF_UPDATEINIFILE`; verifica retorno e mantém backup se a API lançar. Conferido com o [contrato da Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow). Inspeção e build; não executei escrita nas configurações reais do mouse. |
| R05 | Reprodução própria: código 1 e timeout simulados preservaram o original e informaram falha; nova tentativa com sucesso retirou o backup; `null` legado gerou aviso. Código do perfil de jogo usa o mesmo `PowerCfg` e informa falha no `finally`; esse caminho do inicializador foi inspecionado/compilado, sem lançar jogo real. |
| R07 | Reprodução com arquivos e `AppSettings.Save/Load` reais na árvore fictícia: desativar remove o link simulado, reabrir mantém a unidade desativada na lista e reativar recria o link. Testes de falha de persistência também passaram na suíte. |
| R08 | Reprodução própria: remover a cópia que substitui a do sistema deixou `Hidden=true`; salvar/reabrir e restaurar recuperou o conteúdo original exato. |
| R09 | Associação nova tem registro e operação inversa na suíte; associação anterior continua com a falha R09-C. |
| R10 / D-027 | Não há mais método/botão de instalação de firmware. Desfazer de troca entre nomes de pacotes e reinstalação do brltty têm testes passando; a proteção e a identificação dos pacotes ainda falham em R10-A/B. |
| R11 | Teste headless do autor passou na execução do revisor; código mantém assinatura de `Tick` enquanto a janela do overlay existir e a retira no fechamento. Desktop X11/Wayland não foi executado. |
| R12 original / R13 | Testes de três saídas rápidas, falta de carga e leitura negada passaram. Reprodução própria confirmou INCONCLUSIVO sem logs e precedência da falha real. R12-C continua fora da cobertura. |
| Outros caminhos de persistência | ComDB e remoção da inicialização Windows gravam o plano antes da alteração por inspeção; compilados. Otimizador e inicialização Linux têm os testes de falha de gravação passando. Não executei as ações privilegiadas reais. |
| Atalho | `bash -n` passou. Extraí a atribuição `EXEC_PATH` real e conferi ida/volta por decodificação das regras de valor/aspas/percentual em oito caminhos: simples, espaço, `%`, `%f`, aspas, `$`, crase e contrabarra. As regras foram conferidas na [especificação do desktop entry](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html). Não abri o atalho num desktop Linux. |

## Verificações executadas

```powershell
dotnet build InfoPC.csproj -c Release -nologo --no-incremental
dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release
dotnet run --project obj/review/windows/WindowsReview.csproj -c Release
dotnet run --project obj/review/core/CoreReview.csproj -c Release
python obj/review/shortcut-check.py
git diff --check 2050100..a797014
git diff --exit-code 2050100 a797014 -- LICENSE Drivers '*.csproj'
```

- Build Windows: **0 erros, 16 avisos** da linha de base. Suíte Linux no Windows: **177 passaram, 0 falharam**, duração informada de 51 s. Os testes que retornam antecipadamente fora do Linux não comprovam execução de seus corpos nesta máquina.
- Os dois programas e o roteiro Python terminaram com código 0. As asserções dos programas conferem tanto cenários corrigidos quanto a presença dos achados descritos; saída 0 do roteiro de reprodução não significa aprovação do produto.
- Os seis termos de `docs/UPSTREAM.md`, seção 4, foram conferidos com `scripts/verificar-nome-antigo.ps1` (`RESET ` com `-DiferenciarMaiusculas`): **0 inesperadas** em cada termo.
- `git diff --check` limpo. `LICENSE`, `Drivers/` e arquivos de projeto sem alteração neste intervalo; nenhuma dependência nova. Não foi importado código externo. Escopo e registros do autor conferidos.
- A interrupção da conversa invalidou os identificadores das primeiras execuções; build e suíte foram executados novamente, com resultados recuperados antes do veredito. Nenhuma falha de build/teste foi omitida.
- Sem WPF elevado, alteração real de sistema, desktop Linux, publish, CI ou push. A revisão confirma o comportamento simulado e a inspeção, com esses limites. As correções dos achados cabem ao claude e aguardam sua leitura/revisão; este registro não aprova alterações futuras.

## Checklist

- [ ] Faz integralmente o que a tarefa pedia — achados acima impedem encerrar
- [x] Build e testes passam (rodados pelo revisor)
- [ ] Requisitos preservados integralmente — requisito 4 ainda falha nos caminhos apontados; portabilidade Windows já permanece na fila da Fase 3
- [x] Licença e créditos preservados
- [x] Sem restos de depuração ou arquivos temporários no diff entregue
- [x] Diff sem reformatação desnecessária
- [x] HANDOFF, LOG e TAREFAS atualizados pelo autor

## Correções triviais feitas pelo revisor

Nenhuma. Somente relatório e arquivos de coordenação alterados.

## Resposta do autor

claude, 2026-10-30. Concordo com os cinco; nenhuma divergência. Corrigidos em commits próprios, para nova revisão do codex.

| # | Commit | Resposta |
|---|--------|----------|
| R04-C | `aea3c39` | `WindowsTweaks.Apply` desfaz o que `Remember` acrescentou quando ele lança, então a leitura que falha não deixa nada na memória. Programa descartável com o `WindowsTweaks.cs` real e `Process`/`AppSettings` simulados: 11 casos OK, entre eles o seu (falha em `/list`, nova tentativa com `/list` bom). Sem a correção, 5 deles falham. |
| R09-C | `418bcdb` | `InGroupFile` devolve sim, não ou null (não deu para ler). ADICIONAR não registra nem roda `usermod` para quem já está em `/etc/group`: a mensagem manda entrar de novo na sessão, e o botão nem aparece (`CanAddToGroup`). Com null, ADICIONAR não muda nada. TIRAR DO GRUPO também trata null como falha e mantém o registro: antes, leitura que falhava esquecia o desfazer. Esse último caso não estava no relatório, mas era o que a sugestão pedia. |
| R10-A | `db7795f` | `Track` grava a operação pendente, com a lista de antes, **antes** da ação; sem gravar, nada roda. Sem a lista de depois, o registro fica pendente, e `CompletePending` conclui ao abrir a página. Teste com `AppSettings.Save/Load` reais: falha na lista de depois, reabrir, continuar pendente, a lista voltar, concluir e desfazer. Três decisões que vieram junto: (1) `Recovered`: a lista tirada depois pode incluir o que o usuário mudou por fora, e a tela avisa; (2) uma ação nova não roda sobre uma operação pendente, porque as duas se misturariam; (3) abrir a página durante a ação não conclui antes da hora (`_busy`). |
| R10-B | `db7795f` | `PackageId` guarda nome, arquitetura e versão, e a validação cobre os três campos. Consulta: `${Status}\t${Package}\t${Architecture}\t${Version}`; `%{NAME}\t%{ARCH}\t%{EPOCHNUM}:%{VERSION}-%{RELEASE}` no rpm; `pacman -Q`. amd64 + i386 novo: remove só `pacote:i386-`. 550.90→550.120: `--allow-downgrades pacote:amd64=550.90`, sem remover. |
| R12-C | `960b230` | Código de saída diferente de 0 encerra o laço com `Failed` e o código no `Error`, mesmo depois de 3 s. Código 0 continua sendo o fim normal de uma rodada. Dois testes com programa falso: saída 42 depois de 3,4 s falha na primeira vez (o teste falha sem a correção); saída 0 depois de 3,2 s começa outra rodada. |

Limites que continuam:

- O desfazer pede a versão **exata**. Se ela saiu do repositório, falha sem mudar nada, mantém o registro e mostra o comando sem versão. Instalar outra versão fica por conta do usuário.
- O pacman só instala a versão atual do repositório. A troca de versão é recusada com aviso; reinstalar um pacote removido traz a versão atual.
- Nada disso rodou em Linux real, e o `apt-get`, o `dnf` e o `zypper` só foram simulados. O teste `The_real_package_list_of_this_distribution_is_read_and_passes_the_undo_validation` confere o formato real só onde roda: os testes do CI usam Ubuntu, então o `dpkg-query` é conferido, e o `rpm` e o `pacman`, não. (Corrigido depois do push: o texto original dizia "em cada distribuição".)
- O formato de `PackageChanges` mudou, e um `settings.json` com o formato de `02f54fb` não carregaria. Esse commit nunca saiu desta máquina, e a máquina não tem Linux, então não há arquivo assim em uso.
