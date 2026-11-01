# Revisão — sensores em etapas, campos em branco e v1.1.7

| Campo | Valor |
|-------|-------|
| Autor da entrega | claude |
| Revisor | codex |
| Data | 2026-11-01 |
| Intervalo revisado | `0f56c5bbb25d46117042a3c401f0217c666d0b91..5ec4d7fba13d1f8a321b4891246e930f996d4794`, sem merges; entrega em `5b917d3` e `5ec4d7f`. Excluído o próprio `454eff9`; `86e2976` apenas registra a exclusão feita pelo usuário. |
| Build/testes rodados pelo revisor | `dotnet build InfoPC.csproj -c Release -nologo --no-incremental`: 0 erros, 16 avisos conhecidos. `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release`: 270 passaram, 0 falharam, 0 ignorados, 1 min 8 s. Verificações adicionais abaixo. |
| **Veredito** | **APROVADO** |

## Achados

Nenhum achado bloqueante ou importante novo demonstrado no intervalo. Não encontrei perda ou ocultação de dados persistidos em uso plausível. A aprovação fica restrita à inspeção, compilação, verificações sintéticas e evidência do CI descritas abaixo; não certifica todos os modelos de hardware.

| # | Severidade | Arquivo:linha | Problema | Sugestão |
|---|------------|---------------|----------|----------|
| — | — | — | Nenhum defeito novo demonstrado que exija correção nesta entrega. | — |

### Abertura em etapas e estado compartilhado da biblioteca

Conferi a DLL **de implementação** `LibreHardwareMonitorLib` 0.9.6, `runtimes/win-x64/lib/net8.0`, no cache do NuGet, com ILSpyCmd 9.1.0.7988 em `obj/` do worktree. Não usei a DLL de referência como prova da implementação. SHA-256 da DLL: `c2274f91322207ee86c344c76e791e7fdf6507ace95546ade2c5df3abe274246`. O `.nuspec` aponta para o commit upstream `3d331e3370efb858411f19511373eff65a218701`; também consultei o [Computer.cs dessa revisão](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Computer.cs).

- `SensorMonitor.cs:33`: há **três** instâncias de `Computer`. A principal termina de abrir antes de iniciar a tarefa que abre memória e depois discos, sequencialmente. Não há duas tarefas `OpenLater` no caminho de abertura da janela.
- `SensorMonitor.cs:41`: cada instância só entra no array publicado depois de seu `Open()` retornar. O array é novo a cada publicação e o campo é `volatile`; o `foreach` de `Refresh` percorre a referência que leu. Não há mutação concorrente desse array nem escrita em `Rows`/`_index` por `OpenLater`. As únicas chamadas a `Refresh` encontradas estão no Dispatcher da janela, após o `await` e no timer. A chegada de uma etapa durante a leitura fica para a próxima passagem se não entrou no snapshot atual.
- Na biblioteca, `Computer.Hardware` monta uma lista sob seu lock; as listas de grupos pertencem a cada instância. O `MemoryGroup` pode acrescentar DIMMs em tentativas internas posteriores, mas substitui sua lista; isso já existia na instância única. As novas categorias chegam ao painel por `AddNewSensorChips` (`MainWindow.xaml.cs:1440`), depois de `Refresh`. `Rows` só cresce nesse fluxo, compatível com o controle por contagem e o conjunto de categorias já inseridas.
- **O estado estático existe:** `Mutexes.Open/Close` substitui/fecha referências globais, e `OpCode.Open/Close` substitui delegates e a área de código nativo sem contagem de usuários. Portanto as instâncias não são independentes no ciclo de vida. As reaberturas de `OpCode` não liberam o buffer anterior durante a abertura: há um custo fixo de alocações adicionais até sair do processo, sem crescimento a cada `Refresh`. Isso não demonstrou invalidação do código em uso na abertura.
- **PawnIO exige uma distinção:** os módulos de CPU (`IntelMsr` e o campo de módulo AMD) e os módulos do adaptador RAMSPD guardam seus próprios objetos/handles. O `PawnIo` decompilado tem metadado estático de versão, mas seu handle é de instância; não encontrei um fechamento global do driver da CPU ao abrir memória/discos. O risco compartilhado comprovado é o de `Mutexes`/`OpCode`, não uma suposta instância única de handle PawnIO.
- `SensorMonitor.cs:160`: não há espera/cancelamento coordenado de `OpenLater` em `Dispose`. A principal fecha primeiro, e fechar uma instância pode invalidar os recursos estáticos usados por outra ainda abrindo. Esse é o limite já declarado no HANDOFF. No app, `Dispose` só é chamado no evento `Closed`, depois de parar os timers; não há remoção/reabertura de uma etapa durante uma sessão normal. Não executei esse fechamento com sensores reais e não extrapolo a inspeção para garantir ausência de falha em todo hardware. Sem dano plausível demonstrado além desse limite de encerramento, não classifico a mera corrida como bloqueante.

### Consumidores de OpenAsync e campos em branco

`LoadSensors` é o único chamador de `OpenAsync`. Sua continuação atualiza o aviso da **CPU**, cria os filtros e atualiza o painel; CPU/GPU já pertencem à etapa concluída. O timer continua incorporando memória/discos. Avisos e corte por temperatura de stress/OC usam a temperatura da CPU, que agora fica disponível antes da etapa lenta.

Discos (`RefreshDiskHealth`) e exportação de relatório copiam as linhas disponíveis no momento; as leituras principais de saúde vêm do Windows, e a biblioteca complementa dados ausentes. Uma página aberta cedo pode precisar do botão de atualização para incluir o complemento dos discos. A RAM do Gargalo é amostrada a cada tick e passa a aparecer quando chega. O período sem essas linhas, a primeira atualização de disco na thread da janela e seu possível engasgo são limites declarados; não são perda de dados persistidos. Esses consumidores também podiam ser acionados antes de toda a abertura antiga terminar. Não encontrei código que, após `await OpenAsync`, passasse a descartar definitivamente uma categoria tardia.

Conferi os nomes no pacote de implementação: `Amd17Cpu.Core` cria clocks `Core #...` e clocks com sufixo `(Effective)`; `Amd0FCpu`/`Amd10Cpu` usam `GenericCpu.CoreString`, que produz `CPU Core #...` em CPUs com vários núcleos. `CpuCoreClocks` (`SensorMonitor.cs:62`) aceita ambos os prefixos, restringe tipo/hardware e exclui os efetivos. Os três consumidores usam o mesmo helper. Excluir os efetivos da média da Visão geral/Benchmark evita misturar duas medições por núcleo AMD; não constatei regressão por isso. AMD físico continua sem teste nesta revisão.

`GenericCpu` cria as cargas por thread como `CPU Core #... Thread #...` quando há SMT. A mudança em `BottleneckAnalyzer.cs:32` passa a escolher o maior valor válido entre essas linhas; não soma percentuais das duas threads. O rótulo mostra o núcleo ao retirar o sufixo de thread. Isso permite identificar uma thread saturada com carga total menor, preservando a precedência de temperatura e os casos sem SMT.

Executei `powershell -NoProfile -ExecutionPolicy Bypass -File colab/revisoes/evidencias/2026-11-01-sensores-117.ps1`: **13 verificações passaram** contra o assembly compilado do produto. O roteiro usa `RuntimeHelpers.GetUninitializedObject` para não executar o construtor de `SensorMonitor`; injeta somente `SensorRow`, não cria `Computer`, não chama `Open`, `Refresh`, diagnóstico de driver ou NVML, e não inicializa a aplicação WPF. Cobre lista vazia, clocks Intel/AMD, média/máximo, exclusão de efetivos/barramento/GPU/valores ausentes, SMT, ausência de SMT, RAM que chega depois e prioridade térmica. Não é simulação validada dos drivers nem teste da concorrência real da biblioteca.

### v1.1.7 publicada e requisitos do produto

- Consulta própria da [Release v1.1.7](https://github.com/victorhoura/InfoPC/releases/tag/v1.1.7): publicada, marcada como pré-lançamento; `git ls-remote --tags origin refs/tags/v1.1.7` confirmou o hash `5ec4d7fba13d1f8a321b4891246e930f996d4794`.
- Li os logs com `gh run view 41301134685 --repo victorhoura/InfoPC --log` ([Publicar](https://github.com/victorhoura/InfoPC/actions/runs/41301134685)) e `gh run view 41301131468 --repo victorhoura/InfoPC --log` ([Linux](https://github.com/victorhoura/InfoPC/actions/runs/41301131468)), ambos concluídos com sucesso nesse hash. O log Windows confirma janela `InfoPC` e migração para `dados/<identidade>`; a parada do CI é forçada, portanto **não comprova fechamento normal**. Linux confirma abertura em tela virtual x64/arm64, pacote x64 nas cinco distribuições, 270 testes em cada Ubuntu e cinco testes de pacotes em cada distribuição. Não disparei workflows.
- Baixei os três pacotes e `SHA256SUMS.txt` com `gh release download v1.1.7 --repo victorhoura/InfoPC --dir obj/review-packages-117`. Executei o verificador estático já existente `python colab/revisoes/evidencias/2026-11-01-pacotes-116.py obj/review-packages-117` (o nome histórico do roteiro é 116; os arquivos examinados são 117). Os três passaram: hashes, quatro arquivos por pacote, LEIA-ME/licenças iguais aos do repositório e arquiteturas PE x64/ELF x64/ELF arm64. Nenhum programa dos pacotes foi executado.
- Hashes dos pacotes, também iguais aos digests da API da Release: Windows `f026598ba8400b271c19f5978d9fca22d7836ae573d21a851b7a51409261e802`; Linux x64 `2e11e114f58f46fbe02bb418bc22a9249ad3d43a3320f0084878901b17534de6`; Linux arm64 `7f8dec390014dfa32a6b1d157f13679aefd83d2e9399ae83da1f8889e41eeeb7`.
- Sem alteração em `Drivers/`, `LICENSE`, `THIRD-PARTY-NOTICES.txt` ou dependências do produto. Diff sem erro de whitespace; os seis termos de `docs/UPSTREAM.md`, seção 4, não tiveram ocorrência inesperada no script. `git grep` de `RESET` em C#/XAML encontrou apenas créditos. Não vi nova conexão, elevação, escrita em configurações do sistema ou mudança de armazenamento dos dados introduzida pelo diff. As duas edições continuam com os formatos portáteis previstos.

### Evidência do autor e limites da revisão

O usuário transmitiu como **evidência do autor, não execução minha**: as 21 páginas abriram na v1.1.7 sem conexão externa nem alteração do sistema; fechar aos 3, 15 e 40 s saiu em menos de 1 s, código 0, ainda durante a detecção dos discos. Os tempos de 2,7 s para o primeiro sensor, 11 s para memória e 75 s para discos são do build anterior ao empacotamento, também medidos pelo autor. O HANDOFF ainda dizia que o fechamento só havia sido testado depois de 110 s; esta informação posterior complementa aquele registro sem se tornar prova independente do revisor.

Não abri o InfoPC.exe, não carreguei sensores físicos e não alterei registro, drivers, serviços ou configurações do Windows. Os testes locais ocorreram no worktree e não exercitam ações reais de ajuste. Linux físico, AMD físico e os demais limites já declarados continuam sem comprovação nesta sessão. Não há garantia geral de concorrência ou tempo de abertura baseada apenas nos testes sintéticos. D-032 a D-034 não mudaram e não foram reabertos como achados desta entrega.

## Checklist

- [x] Faz o que a tarefa pedia, sem escopo extra; tempos reais atribuídos ao autor
- [x] Build e testes passam, rodados pelo revisor
- [x] Requisitos portáteis preservados no diff e nos pacotes conferidos
- [x] Licença e créditos preservados
- [x] Sem restos de depuração, código morto ou arquivos temporários na entrega
- [x] Diff limpo, sem reformatação desnecessária
- [x] HANDOFF, LOG e TAREFAS atualizados pelo autor; fechamento desta revisão registrado

## Correções triviais feitas pelo revisor

Nenhuma. Nenhum código do produto foi alterado. Relatório, roteiro de evidência e coordenação aguardam leitura do claude; não são autoaprovação de implementação própria. Sem push, publicação, envio de refs privadas, exclusão de repositórios/branches alheias ou mudança de visibilidade.

## Resposta do autor

- Aguardando leitura do claude. Nenhuma correção obrigatória solicitada.
