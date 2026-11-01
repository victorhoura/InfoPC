# InfoPC — v1.1

Ferramenta portátil de diagnóstico e ajuste do PC para **Windows** e **Linux**: um único executável, sem instalador de sistema.

> **Créditos:** o InfoPC é baseado no [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), de Gustavo (GustavoReset),
> licença [MIT](LICENSE). O aviso de copyright do autor original fica mantido no arquivo `LICENSE`.
>
> O app roda como administrador (necessário para os sensores de baixo nível).

## Novidades da v1.1
- **Edição Linux** nativa (Avalonia / .NET 8, pasta `linux/`), com as mesmas áreas do menu: Hardware, Jogos, Desempenho, Ferramentas e Maker.
  Lê o hardware pelo que o Linux oferece (`/proc`, `/sys`, hwmon, NVML, `smartctl`, Steam/Lutris/Heroic) e pede a senha (`pkexec`) só quando a ação precisa de root.
  Instalação em um comando, sem root e sem pacote de sistema: veja [docs/LINUX.md](docs/LINUX.md).
- O Windows segue como na v1.0 (só o número da versão mudou).

## Novidades da v1.0
- Primeira versão do InfoPC, com os mesmos recursos do RESET EXPLODE v0.16.
- Nome novo, logo e ícone provisórios, menu com nomes simples (Hardware, Jogos, Desempenho, Ferramentas, Maker)
  e pastas próprias (`dados\` ao lado do `.exe`), sem conflito com o programa original no mesmo PC.

Detalhes em [NOVIDADES.md](NOVIDADES.md).

## Baixar

Pacotes prontos na página **Releases**, um para cada sistema: `.zip` para Windows (`InfoPC.exe`, pede administrador) e `.tar.gz` para Linux, em x64 e arm64.
No terminal do Linux, sem login (em arm64, troque `linux-x64` por `linux-arm64`):

```bash
curl -fsSL https://github.com/victorhoura/InfoPC/releases/download/v1.1.9/InfoPC-1.1.9-linux-x64.tar.gz | tar xz && ./InfoPC/InfoPC
```

## Instalar no Linux

```bash
git clone https://github.com/victorhoura/InfoPC.git ~/InfoPC
bash ~/InfoPC/linux/instalar.sh
infopc            # abre a janela; "infopc --ajuda" lista os comandos de terminal
```

Detalhes, mapa Windows → Linux e limites: [docs/LINUX.md](docs/LINUX.md).

## Recursos (edição Windows)

App WPF (.NET 8) com:
- **Informações do sistema** (categorias + detalhes): SO, placa-mãe/BIOS, CPU, RAM (por módulo), GPU, discos — via WMI.
  Em Armazenamento, cartões por unidade (C:, pendrive, HD externo, rede) com anel de cheio/vazio, tipo e disco físico (`DriveMap.cs`).
- **Avisos de dispositivo** (`ToastWindow.xaml`): ao conectar/remover pendrive, HD externo ou qualquer dispositivo USB (ex.: Arduino),
  aparece um aviso no canto da tela e a lista de unidades se atualiza (via `WM_DEVICECHANGE`, sem ficar consultando).
- **Sensores em tempo real**: temperatura, clock, carga, voltagem, ventoinhas, consumo etc. — via LibreHardwareMonitorLib. Atualiza a cada 1 s, com filtro por tipo.
- **Placa de vídeo** (estilo GPU-Z): chip, arquitetura, processo, die, transistores, shaders/SMs/Tensor/RT, ROPs/TMUs, fillrates, TFLOPS,
  memória (tamanho, tipo, barramento, clock, banda, Resizable BAR), clocks, PCIe (máximo e em uso), limites de energia,
  VBIOS, driver, assinatura WHQL, CUDA, DirectX (feature level), APIs e recursos da arquitetura.
  Sensores ao vivo da GPU + P-state, PerfCap, NVENC/NVDEC. Fontes: NVML (NVIDIA), DXGI/Direct3D 12, WMI/PnP, registro e banco de specs em `GpuSpecDb.cs`.
- **Stress test**: carga de CPU em todos os threads (FPU + inteiros) e/ou RAM (grava e verifica padrões em 40–80% da memória livre).
  Para sozinho quando a temperatura da CPU atinge o limite configurado, quando o tempo acaba ou se aparecer erro de memória.
  Mostra temperatura atual/máxima, carga, consumo, gráfico dos últimos 5 min e um log dos eventos.
- **Stress de GPU** (inspirado no FurMark, `GpuBurn.cs`): torus com pelos renderizado por *shells* em Direct3D 11 (Vortice),
  com overdraw massivo, cadeia de FMAs no pixel shader e alpha blending, sem limite de FPS, numa janela própria.
  Opções: resolução (720p/1080p/1440p/tela cheia), MSAA (off/4x/8x), intensidade (Normal/Alta/Extrema) e limite de temperatura da GPU.
  Mostra FPS, temperatura, consumo e PerfCap; ao final registra frames e FPS médio. ESC ou fechar a janela encerra o teste.
  Medido numa RTX 5070 (1080p, 4x MSAA, Alta): ~99% de uso e 220–232 W de 250 W.
- **Jogos**: FPS esperado em 32 jogos populares para a sua GPU + CPU, por resolução (1080p/1440p/4K), gráficos e upscaling (DLSS/FSR).
  Com chave da [HowManyFPS API](https://howmanyfps.com/developer-api) (pedida em info@howmanyfps.com), o botão BUSCAR FPS REAL mostra FPS médio e 1% low medidos em sessões reais;
  sem chave, usa uma estimativa local aproximada (`GameCatalog.cs`). A chave fica cifrada com DPAPI na pasta de dados e as respostas em cache por 7 dias.
- **Detector de gargalo**: equilíbrio teórico GPU × CPU; leitura ao vivo (carga da GPU, CPU total, núcleo mais ocupado, VRAM, RAM,
  temperaturas, limitador térmico) com veredito; e análise de sessão (1/3/5 min) com % do tempo limitado por GPU, CPU, VRAM, RAM,
  temperatura ou trava de FPS, mais recomendações (`BottleneckAnalyzer.cs`).
- **Overlay de jogos** (estilo RTSS/Afterburner, `OverlayWindow.xaml`): FPS, frametime, 1% low, gráfico de frametime, GPU/CPU/RAM/VRAM.
  Janela transparente, sempre no topo e que ignora cliques; atalho global **Ctrl+Shift+O**. **Itens escolhidos pelo usuário** (`OverlayMetrics.cs`,
  `MainWindow.Overlay.cs`): FPS/frametime/1% low/gráfico, GPU (uso, temperatura, hotspot, clocks do núcleo e da memória, consumo, tensão,
  ventoinha, PerfCap), VRAM, CPU (uso, núcleo mais ocupado, temperatura, clock médio e máximo, consumo), RAM, nome do jogo e hora. Volta para o topo a cada troca de janela ativa
  (jogos em janela sem borda também se marcam como "sempre no topo") e fica no monitor do jogo. Não aparece em tela cheia exclusiva. O FPS de qualquer jogo é medido via ETW
  (eventos Present de DXGI/D3D9, como o PresentMon, `FrameMonitor.cs`), sem injetar nada no jogo.
- **Benchmark** (`BenchRenderer.cs`): benchmark de CPU no princípio do Cinebench, com renderizador próprio. Path tracer monocromático
  (iluminação global, sombras suaves, metal e vidro com refração), 960×540 a 1024 amostras/pixel, imagem em blocos de 32 px
  distribuídos entre threads do centro para fora. Multi-core (todas as threads) e single-core (1 thread, imagem inteira, ~1min40s num 7700X),
  1 passada ou mínimo de 10 min. Nota = amostras/s ÷ 10.000, determinística (sementes fixas por pixel). Histórico dos últimos 10 resultados.
  Referência: Ryzen 7 7700X ≈ 4.500 multi / 530 single.
- **Benchmark · benchmarks de jogo**:
  - *Cena de jogo (GPU)* (`GpuGameBench.cs`): vale em Direct3D 11 com terreno fractal, 40 mil árvores por instancing, sombras 4096² com PCF,
    água, neblina e pós HDR; câmera em trajeto fixo por tempo, 1080p/1440p/4K, 30/60 s. Nota = FPS médio × 10. RTX 5070 em 1080p ≈ 680 FPS.
    Os sensores ficam pausados durante o teste (consultas ao driver geravam engasgos no 1% low).
  - *Ray tracing (GPU)* (`GpuRtBench.cs`, `MainWindow.Rt.cs`): Direct3D 12 com **DXR 1.1** (raios inline num compute shader), usa os núcleos
    de RT (RTX 20+, Radeon RX 6000+, Intel Arc). 4 mil objetos (fosco, metal, espelho, luzes), path tracing com sombra suave, oclusão de
    ambiente, reflexos e iluminação indireta; TLAS refeita a cada frame. Presets Normal/Alta/Extrema (2/4/8 amostras, 2/3/4 rebatidas).
    **Confiabilidade**: aquecimento sem medir + 3 passadas; nota pela mediana (FPS × 10), variação entre passadas (confiável abaixo de 3%),
    tempo de frame pelos timestamps da GPU. Referência: RX 9070, Alta 1440p, modo Padrão = 878 pts (87,8 FPS), variação 0,5%.
  - *Simulação de jogo (CPU)* (`GameSimBench.cs`): 30 mil unidades em frente de batalha; thread principal (grade, lógica, preparo do desenho)
    + workers (física, colisão, IA). Nota = FPS da simulação. Ryzen 7 7700X ≈ 290 FPS.
  - *Memória e carregamento* (`MemoryBench.cs`): latência e banda da RAM; carregamento de mapa (pacote de 512 MB + mil arquivos de 64 KB)
    lido sem cache do Windows, com passada prévia para o antivírus e 8 threads. Arquivos temporários apagados no final.
  - *Disco* (`DiskBench.cs`): leitura e escrita sequencial (1 MB) e aleatória 4K (QD1 e QD32), sem cache do Windows
    (`FILE_FLAG_NO_BUFFERING`), em arquivo temporário de 512 MB / 1 GB / 4 GB na unidade escolhida; apagado no final. Histórico dos últimos resultados.
- **Overclock** (`MainWindow.Oc.cs`, `OcCheck.cs`): ajuste de limite de energia e travamento de clock da GPU NVIDIA (via NVML) com botão de restaurar,
  e **verificação de estabilidade** de overclock (CPU, GPU ou ambos; 5/15/30 min) com limites de segurança de temperatura, leitura de erros
  de hardware (WHEA, reset de driver de vídeo), veredito, histórico e guia de BIOS conforme a placa-mãe e a CPU.
- **Otimização** (`Optimizer.cs`, `WindowsTweaks.cs`): um botão que limpa a memória em espera (como o ISLC) e enxuga a RAM dos
  processos, e fecha os programas marcados (pré-marca só os seguros: OneDrive, Teams, Widgets, Adobe...; programas com janela são
  fechados sem forçar). Ajustes do Windows com interruptor e "Restaurar originais": plano Alto desempenho, Modo de Jogo, desativar
  Game DVR, HAGS, otimizações para jogos em janela, aceleração do mouse. Nada que reduza a segurança (Defender, VBS, UAC, Update).
- **Maker** (Arduino, ESP32 e conversores USB-serial):
  - *Portas COM* (`UsbCleaner.cs`): lista e remove registros de dispositivos USB/seriais ausentes ("fantasmas" de Arduino,
    ESP32, CH340, FTDI, CP210x...) e libera os números COM presos no ComDB, com backup para desfazer. Resolve a IDE do Arduino
    não achar a placa ou a porta ir para COM20+.
  - *Drivers seriais* (`MakerDrivers.cs`, `MainWindow.Maker.cs`): diagnóstico das placas conectadas com **CH340/CH341, CP2102/CP210x,
    FTDI, Prolific PL2303 e Arduino** pelo código do Gerenciador de Dispositivos (sem driver, código 10, 43, FTDI falsificado com PID 0000),
    com orientação para cada caso; **REINSTALAR** (remove e detecta de novo); lista das versões de driver guardadas no Windows com
    **REMOVER** (backup antes, para voltar a uma versão anterior ou desinstalar de vez) e "Restaurar removidos"; **driver Arduino incluído no app** (em `Drivers`, veja `Drivers/LEIA-ME.md`),
    que instala sem internet; e link para a página oficial dos demais fabricantes. **Arduino IDE**: instalar/atualizar pelo winget (traz os drivers das placas oficiais),
    fechar programas que prendem a porta (monitor serial, PuTTY, Cura...) e limpar o cache de índices quando a IDE trava em "Downloading index".
- **Inicialização** (`StartupManager.cs`, `MainWindow.Startup.cs`): lista os programas que abrem com o Windows (chaves Run do usuário e
  de todos os usuários, 32 e 64 bits, e pastas Inicializar) com descrição e fabricante. Interruptor para ativar/desativar igual ao
  Gerenciador de Tarefas (chaves StartupApproved) e botão REMOVER, com cópia na pasta de dados para "Restaurar removidos".
- **Instalar apps** (`AppInstaller.cs`, `MainWindow.Apps.cs`): instala os essenciais pós-formatação (Steam, Google Chrome, Discord,
  Lightshot, 7-Zip, WinRAR, VLC) pelo **winget**, em silêncio e um de cada vez, mostrando o resultado de cada um. Detecta o que já está
  instalado (Aplicativos instalados do Windows). Para incluir outro app, acrescente uma linha em `AppInstaller.Catalog()` com o ID do winget.
- **Saúde dos discos (S.M.A.R.T.)** (`DiskHealth.cs`, `MainWindow.Disks.cs`): página própria com um cartão por disco físico —
  veredito (Bom/Atenção/Ruim) com os motivos, temperatura, horas ligado, ciclos, desgaste do SSD, setores realocados, erros de leitura/escrita
  e previsão de falha. Fontes: Windows Storage (MSFT_PhysicalDisk, MSFT_StorageReliabilityCounter) e MSStorageDriver_FailurePredictStatus.
- **Relatório exportável** (`ReportBuilder.cs`): botão **EXPORTAR RELATÓRIO** em Sistema. Gera um arquivo HTML (com a marca InfoPC) ou JSON
  com as informações do sistema, RAM por módulo, discos e saúde dos discos.
- **RAM detalhada**: resumo (capacidade, canais, tipo DDR4/DDR5/LPDDR5, velocidade) e, por módulo, fabricante, part number, formato,
  voltagem, clock configurado e localização do slot.
- **Energia** (`BatteryInfo.cs`, `MainWindow.Net.cs`): bateria de notebook com desgaste (capacidade atual × de projeto), ciclos de carga, estado,
  autonomia, tensão e potência, com veredito; fonte de energia (tomada/bateria), plano de energia do Windows e consumo dos sensores de CPU e GPU.
  Em PC de mesa mostra só o cartão de energia.
- **Rede** (`NetworkTest.cs`): diagnóstico de lag em ~30 s — latência, jitter e perda de pacotes até o roteador e até a internet (1.1.1.1 e 8.8.8.8),
  tempo de DNS, velocidade de download/upload (servidor público speed.cloudflare.com) e latência sob carga (bufferbloat). O veredito aponta se o
  problema está na rede local (Wi-Fi/cabo/roteador), no provedor ou se a conexão está estável.
- **Integração** (`LightBackends.cs`, `CorsairSdk.cs`, `CorsairSdkInstaller.cs`, `MainWindow.Integ.cs`): controle de iluminação e efeitos
  de todo o PC numa tela só (em teste). Pontes: **Corsair iCUE / iCUE LINK** (iCUE SDK v4; a DLL é baixada pelo programa, com confirmação e
  verificação de assinatura), **OpenRGB** (servidor SDK local; é o caminho para **MSI Mystic Light, Gigabyte RGB Fusion, ASUS Aura**, RAM, Razer,
  Logitech e outras, já que essas marcas não têm SDK público) e **Razer Chroma** (API REST do Synapse). Efeitos: cor fixa, respiração, arco-íris em onda,
  e cores que seguem a **temperatura da CPU/GPU** ou a **carga da CPU**, com brilho e velocidade. Ao devolver ou fechar, cada fabricante reassume as luzes.
  Para MSI/Gigabyte/ASUS: abra o OpenRGB com o servidor ligado e feche MSI Center, Armoury Crate e RGB Fusion.
- **Biblioteca de jogos** (`GameLibrary.cs`, `GameLauncher.cs`, `Vdf.cs`, `MainWindow.Launcher.cs`): inicializador que **detecta os jogos instalados** (Steam, Epic Games, GOG,
  Ubisoft Connect e `.exe` adicionados à mão) e guarda um **perfil por jogo**: prioridade do processo, plano de energia Alto desempenho só durante o jogo,
  GPU dedicada e sem otimizações de tela cheia (por `.exe`, no registro do usuário), **menu de gráficos** (modo de janela, resolução, taxa de atualização e extras
  convertidos em argumentos de inicialização no estilo Source/CS2, Unity ou Unreal) e **programas auxiliares** (ferramentas de terceiros que abrem antes do jogo
  e fecham junto). Em jogos da Steam, um botão grava os argumentos nas **opções de inicialização** (`localconfig.vdf`, com backup, fechando a Steam antes).
  O botão JOGAR aplica o perfil, inicia e desfaz o que for temporário quando o jogo fecha.

## Interface
- Visual preto e branco minimalista em **blocos** (cartões arredondados, inspirado em painéis de dashboard). Um único menu lateral
  com *Visão geral* no topo e categorias recolhíveis (começam fechadas; clique para ver as sub-abas):
  - *Visão geral* (`MainWindow.Dash.cs`): gráfico de área CPU × GPU (temperatura, carga ou consumo; 1/5/15 min),
    desempenho do Benchmark, tabela de componentes, anéis de RAM/VRAM/disco, eventos recentes e saúde do PC.
  - **HARDWARE** · Sistema, Sensores, Placa de vídeo, Discos, Energia, Rede.
  - **JOGOS** · Biblioteca, FPS estimado, Gargalo, Overlay.
  - **DESEMPENHO** · Benchmark, Stress test, Overclock.
  - **FERRAMENTAS** · Otimização, Inicialização, Instalar apps, Drivers, Integração.
  - **MAKER** · Portas COM, Drivers seriais.
- Botão de **modo escuro/claro** no canto superior direito (`ThemeManager.cs`); a escolha fica salva. O overlay de jogos e os avisos de dispositivo ficam sempre escuros.
- Leitura ao vivo de temperatura e carga da CPU e da GPU sempre visível no rodapé do menu.
- Sensores com busca e filtros por tipo (chips); tema em `Theme.xaml`.

## Base utilizada
- Código base: [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode) v0.16 (MIT), de Gustavo (GustavoReset): interface, stress test, gráficos e os demais recursos.
- Sensores: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0) — mantenha o aviso de licença ao distribuir.
- Informações do sistema: WMI do Windows.
- Medição de FPS: [TraceEvent](https://github.com/microsoft/perfview) (MIT), da Microsoft, para ler eventos ETW.
- Renderização do stress de GPU: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT), bindings de Direct3D 11 para .NET. Direct3D 12 e DXC (compilador de shaders da Microsoft) também via Vortice, no teste de ray tracing.

## Requisitos
- Windows 10/11, 64 bits
- Para compilar: .NET SDK 8 ou mais novo (https://dotnet.microsoft.com/download). Para usar o `.exe` portátil, nada.
- Driver **PawnIO** (temperatura/consumo da CPU): `winget install namazso.PawnIO`
  A LibreHardwareMonitorLib 0.9.6+ usa o PawnIO no lugar do antigo WinRing0, que o Windows Defender bloqueia.

## Como rodar
Na pasta do projeto:
```
dotnet run
```
O app pede privilégio de administrador (necessário para sensores de baixo nível).
Para levar a outro PC: dê dois cliques em `publicar.bat`. Ele gera `..\InfoPC-dist\InfoPC.exe` (arquivo único, com o .NET embutido,
sem caminhos do seu usuário) + `LEIA-ME.txt` + `LICENSE.txt`. No outro PC só é preciso instalar o PawnIO. Rode de novo após cada alteração no código.

## Observações
- Se algum sensor não aparecer, depende do suporte da biblioteca à sua placa-mãe.

## Já feito
- ✔ Informações do sistema, sensores, placa de vídeo, jogos, gargalo, overlay
- ✔ Stress test com limite de temperatura (CPU, RAM, GPU)
- ✔ Benchmarks: CPU, GPU, simulação, memória e **disco**
- ✔ Saúde dos discos (S.M.A.R.T.)
- ✔ Relatório exportável (HTML/JSON)
- ✔ Overclock (limite de energia, clock e verificação de estabilidade)
- ✔ Energia (bateria e fonte) e Rede (latência, perda e velocidade)
- ✔ Integração: iluminação Corsair iCUE, OpenRGB (MSI/Gigabyte/ASUS) e Razer Chroma (primeira versão, em teste)
- ✔ Drivers: inventário de drivers, NVIDIA (verifica a versão, baixa, confere a assinatura e abre o instalador), AMD/ASUS/MSI/Gigabyte pela página oficial, Windows Update e backup (`DriverManager.cs`, primeira versão, em teste)
- ✔ Biblioteca de jogos: inicializador com detecção de jogos, perfil por jogo e opções de inicialização da Steam (primeira versão, em teste)
- ✔ Inicialização do Windows: ativar, desativar e remover programas, com desfazer
- ✔ Abertura rápida: janela na hora, hardware carregado em paralelo
- ✔ Instalar apps essenciais pós-formatação pelo winget
- ✔ Maker: portas COM e drivers seriais CH340/CP210x/FTDI/Prolific/Arduino

## Próximas ideias
- Relatório em CSV e com gráficos
- Comparar resultados de benchmark entre PCs / histórico de relatórios
- Leitura direta de SPD / Super I/O
- Integração: perfis salvos, efeitos por jogo, SDKs nativos da MSI/ASUS/Gigabyte se forem liberados
- Alertas de temperatura, bateria e saúde de disco em segundo plano
- Assinatura digital do .exe (sem instalador)

## Licença

MIT — veja o arquivo [LICENSE](LICENSE) (copyright do código original: Gustavo/GustavoReset).
O driver em `Drivers/` é da Arduino LLC, sob GPL, e não faz parte da licença MIT (veja [Drivers/LEIA-ME.md](Drivers/LEIA-ME.md)).
As bibliotecas dos pacotes prontos têm os próprios termos: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
