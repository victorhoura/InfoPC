# ARQUITETURA

> Mapa compartilhado pelos dois agentes. Mantenha curto, correto e atualizado. Escrito em 2026-10-24 a partir do código importado (RESET EXPLODE v0.16) e do README do original; atualizado na renomeação para InfoPC 1.0.

## Visão geral

App WPF de uma janela (`MainWindow`) com menu lateral por categorias. Cada área tem, em geral, um arquivo parcial `MainWindow.<Área>.cs` com a interface e uma ou mais classes de lógica (`*Collector`, `*Bench`, `*Manager`...). As áreas sem arquivo parcial próprio ficam em `MainWindow.xaml.cs`. Ao abrir, a janela aparece logo e o hardware é lido em paralelo, em segundo plano. Fontes de dados: sensores pela LibreHardwareMonitorLib (atualiza a cada 1 s), informações pelo WMI, GPU por NVML/DXGI/D3D12 e FPS por ETW (TraceEvent). Janelas extras: overlay de jogos (`OverlayWindow`) e avisos de dispositivo (`ToastWindow`).

## Stack

- C# com `Nullable`, `ImplicitUsings` e `AllowUnsafeBlocks`; WPF; .NET 8 (`net8.0-windows`); x64.
- Sempre otimizado (`<Optimize>true</Optimize>` também em Debug): benchmarks e stress tests dependem do JIT otimizado.
- Nesta máquina: .NET SDK 10.0.400 (compila o alvo .NET 8) e runtime .NET 8 Desktop 8.0.19.
- Pacotes NuGet e licenças: `docs/UPSTREAM.md`, seção 7.

## Comandos

| Ação            | Comando | Resultado em 2026-10-24 |
|-----------------|---------|-------------------------|
| Compilar        | `dotnet build InfoPC.csproj -c Release -nologo` | 0 erros, 16 avisos (nulabilidade, já existentes no original) |
| Testes          | não há testes automatizados no original | — |
| Executar        | `bin\Release\net8.0-windows\InfoPC.exe` (pede administrador) | não executado pelos agentes (UAC): teste manual do usuário |
| Pacote portátil | `dotnet publish InfoPC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist -nologo` | `dist\InfoPC.exe` 1.0.0.0, arquivo único de ~197 MB |

### Edição Linux (`linux/`, detalhes em `docs/LINUX.md`)

| Ação            | Comando | Resultado |
|-----------------|---------|-----------|
| Publicar        | Actions, workflow **Publicar**, ou `gh workflow run publicar.yml -f versao=<versão> -f publicar=true` | gera, abre e testa os pacotes das duas edições e publica a Release (D-029). Com `publicar=false`, deixa um rascunho com as telas |
| Conferência     | Actions, workflow **Conferencia** (`.github/workflows/conferencia.yml`), só manual | em Linux de verdade: `pkexec` e polkit, instalar e desfazer pacote, `ubuntu-drivers`, hidepid, pen drive FAT32 e exFAT, janela em GNOME e KDE, sem tela, e arm64 em outras distribuições |
| Testes          | `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` | 270 testes passando no Windows (cultura pt-BR) em 2026-10-31; o CI cobre Ubuntu 22.04 e 24.04, e o fluxo real de pacotes e grupos em cinco distribuições |
| Executar        | `dotnet run --project linux/src/InfoPC.Linux` (janela) ou `... -- --info` (modo texto) | a janela só foi desenhada sem tela (testes); o modo texto roda no CI |
| Binário único   | `dotnet publish linux/src/InfoPC.Linux/InfoPC.Linux.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o out` | `out/InfoPC` |
| Instalar/remover| `bash linux/instalar.sh` / `bash linux/desinstalar.sh` | testado no CI em Debian 12, Ubuntu 24.04, Fedora 41, Arch e openSUSE |

O build do Windows (`InfoPC.csproj`) exclui `linux\**` e deve continuar com 0 erros e 16 avisos. O `InfoPC.Core` linka arquivos da raiz (lista em `docs/LINUX.md`, seção 5): mexer neles exige conferir as duas edições.

O `publicar.bat` faz o mesmo publish, mas grava em `..\InfoPC-dist` (fora do repositório), copia `LEIA-ME.txt`, `LICENSE.txt` e `THIRD-PARTY-NOTICES.txt` para lá (as licenças exigem os avisos junto do programa) e antes fecha o programa, se estiver aberto. Quem usar o comando acima direto precisa copiar esses três arquivos à mão.

Logo e ícone provisórios: `scripts/gerar-logo-provisorio.ps1` regrava os quatro arquivos de `Assets/` (D-016).

## Mapa do código (tudo na raiz)

| Área do menu                       | Interface                                   | Lógica |
|------------------------------------|---------------------------------------------|--------|
| Janela, menu, Visão geral          | `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Dash.cs` | `App.xaml.cs` (instância única), `ThemeManager.cs`, `Theme.xaml`, `AppSettings.cs` |
| Sistema, Armazenamento, relatório  | `MainWindow.xaml.cs`                        | `SystemInfoCollector.cs`, `DriveMap.cs`, `InfoNode.cs`, `ReportBuilder.cs` |
| Sensores                           | `MainWindow.xaml.cs`                        | `SensorMonitor.cs`, `SensorRow.cs` |
| Placa de vídeo                     | `MainWindow.xaml.cs`                        | `GpuInfoCollector.cs`, `GpuSpecDb.cs`, `Nvml.cs`, `Dxgi.cs` |
| Discos (S.M.A.R.T.)                | `MainWindow.Disks.cs`                       | `DiskHealth.cs` |
| Energia e Rede                     | `MainWindow.Net.cs`                         | `BatteryInfo.cs`, `NetworkTest.cs` |
| Jogos: biblioteca e inicializador  | `MainWindow.Launcher.cs`                    | `GameLibrary.cs`, `GameLauncher.cs`, `GameRow.cs`, `Vdf.cs` |
| Jogos: FPS estimado e gargalo      | `MainWindow.xaml.cs`                        | `GameCatalog.cs`, `HowManyFpsClient.cs`, `BottleneckAnalyzer.cs` |
| Overlay                            | `MainWindow.Overlay.cs`, `OverlayWindow.xaml(.cs)` | `OverlayMetrics.cs`, `FrameMonitor.cs` |
| Benchmark                          | `MainWindow.xaml.cs`, `MainWindow.Rt.cs`    | `BenchRenderer.cs`, `GpuGameBench.cs`, `GpuRtBench.cs`, `GameSimBench.cs`, `MemoryBench.cs`, `DiskBench.cs` |
| Stress test                        | `MainWindow.xaml.cs`                        | `StressTester.cs`, `GpuBurn.cs` |
| Overclock                          | `MainWindow.Oc.cs`                          | `OcCheck.cs`, `HardwareErrors.cs`, `Nvml.cs` |
| Otimização                         | `MainWindow.xaml.cs`                        | `Optimizer.cs`, `WindowsTweaks.cs` |
| Inicialização                      | `MainWindow.Startup.cs`                     | `StartupManager.cs` |
| Instalar apps                      | `MainWindow.Apps.cs`                        | `AppInstaller.cs` |
| Drivers                            | `MainWindow.Driver.cs`                      | `DriverManager.cs` |
| Iluminação                         | `MainWindow.Integ.cs`                       | `LightBackends.cs`, `CorsairSdk.cs`, `CorsairSdkInstaller.cs` |
| Maker (portas COM, drivers seriais)| `MainWindow.Maker.cs`                       | `UsbCleaner.cs`, `MakerDrivers.cs`, `Drivers/` |
| Avisos de dispositivo              | `ToastWindow.xaml(.cs)`                     | `WM_DEVICECHANGE` em `MainWindow.xaml.cs` |

A coluna "Interface" foi deduzida pelos nomes dos arquivos e pelo README; confirme no código antes de mexer.

## Onde o programa grava dados

- `dados\<identidade>\` ao lado do `.exe` (`DataFolder`, D-028; sem poder gravar, `%AppData%\InfoPC\`): configurações (chave da API HowManyFPS cifrada com DPAPI, que não abre em outro PC ou usuário), histórico de benchmarks, cache da API (7 dias), backups da Inicialização, DLL do iCUE baixada.
- `%TEMP%\InfoPC\drivers` (instalador baixado da NVIDIA) e `%TEMP%\InfoPC\drivers-incluidos` (drivers seriais extraídos para instalar).
- `Documentos\InfoPC-Drivers\<data>`: backup dos drivers de terceiros, só pelo botão da página Drivers.
- Benchmarks de disco e memória: arquivos temporários de 512 MB a 4 GB na unidade escolhida, apagados no fim.
- Registro, sempre por ação do usuário: `UserGpuPreferences` e `AppCompatFlags\Layers` (perfis de jogo), `GameBar`, `GameDVR`, `GameConfigStore`, `GraphicsDrivers` (HAGS), `COM Name Arbiter` (ComDB), chaves `Run` e `StartupApproved` (Inicialização).
- Steam: `localconfig.vdf` (opções de inicialização, com backup).
- Instalações: drivers seriais por `pnputil`; PawnIO, apps e Arduino IDE pelo winget; instalador de driver NVIDIA.

## Administrador

O `app.manifest` pede `requireAdministrator`: o programa inteiro roda elevado (sensores de baixo nível: MSR, Super I/O). Permitido pelo requisito 2 (D-006).

## Acesso à internet

Auditoria completa da edição Windows em 2026-10-28 (Fase 3): todo `HttpClient`, `Ping`, winget, Windows Update e `Process.Start` de URL, com o que dispara cada um. Não há links em XAML nem chamadas no `App.xaml.cs`. Abrir uma página não conta como acionar o recurso (D-024).

| Recurso                               | Destino | Gatilho |
|---------------------------------------|---------|---------|
| FPS estimado (só com chave salva)     | howmanyfps.com/api (envia CPU e GPU abreviadas, jogo, resolução, upscaling e a chave) | botão BUSCAR FPS REAL ou salvar a chave; depois disso, cada troca de opção na mesma sessão. Antes, só o cache local (`HowManyFpsClient.Cached`, 7 dias) |
| Drivers: versões NVIDIA e AMD         | nvidia.com / gfwsl.geforce.com, amd.com | botão VERIFICAR TUDO. Abrir a página lê só o que está instalado (`ScanDriversAsync(online: false)`) |
| Drivers: baixar instalador            | servidor da NVIDIA / AMD | botão, com confirmação |
| Drivers: Windows Update               | API do Windows Update | botões procurar e instalar |
| Iluminação: instalar SDK do iCUE      | api.github.com (`CorsairOfficial/cue-sdk`) | botão, com confirmação |
| Rede                                  | 1.1.1.1, 8.8.8.8, speed.cloudflare.com | botão TESTAR |
| Instalar apps, Arduino IDE, PawnIO    | winget | botão (PawnIO com confirmação) |
| Links de "página oficial"             | sites dos fabricantes, abertos no navegador | botão |
| Local (não sai do PC)                 | Razer Chroma `localhost:54235`, servidor do OpenRGB | página Iluminação |

Edição Linux, conferida junto: a HowManyFPS segue a mesma regra do Windows (BUSCAR FPS REAL na `GamesPage`); o `fwupdmgr refresh` (LVFS) só roda no botão; `ubuntu-drivers devices`, `dpkg -s`, `rpm -q`, `pacman -Qi` e `flatpak info` são locais.

## Alterações no sistema e como desfazer (requisito 4)

Auditoria da edição Windows em 2026-10-28. Todas partem de um clique; nenhuma roda na abertura.

| Alteração | Como desfazer |
|-----------|---------------|
| Otimização: plano de energia, Modo de Jogo, Game DVR, HAGS, jogos em janela, aceleração do mouse (`WindowsTweaks`) | original guardado em `TweakBackup` pelo `Tweak.Remember` e gravado no disco antes de mexer no Windows (`WindowsTweaks.Apply`; se não gravar, nada muda); botão "Restaurar originais" (valor que não existia é apagado; só esquece o backup do ajuste que voltou, o que falhar continua guardado para tentar de novo). O `powercfg` com código de saída diferente de zero ou que passa de 10 s conta como falha. O plano "Alto desempenho" recriado por `-duplicatescheme` fica marcado em `power.criado` e é apagado ao restaurar. O mouse vai ao `SPI_SETMOUSE` na ordem da API (limiar 1, limiar 2, aceleração), sem `SPIF_UPDATEINIFILE` |
| Perfil de jogo: plano de energia | temporário: volta no `finally` quando o jogo fecha ou o acompanhamento é cancelado; se não voltar, a linha de estado avisa. Sem conseguir ler o plano atual, o perfil não troca o plano. O plano criado só para o jogo é apagado no fim |
| Perfil de jogo: GPU de alto desempenho e "otimizações de tela cheia" por .exe (`UserGpuPreferences`, `AppCompatFlags\Layers`) | original do .exe guardado em `ExeBackup` antes da 1ª alteração e gravado no disco antes de mexer no registro (se não gravar, o JOGAR para sem alterar nada); desmarcar e jogar de novo devolve o original. Sem backup, desmarcado não mexe em nada (a preferência pode ser do usuário) |
| Inicialização: desativar ou remover | reativar; os removidos ficam em `StartupRemoved` (atalhos copiados para `Startup\`), gravado antes de remover (`StartupManager.Plan`), e voltam por "Restaurar" com o estado ativado/desativado de antes (`StartupApproved`) |
| Maker: liberar números COM (ComDB) | original em `ComDbBackup`, gravado antes de reescrever (`UsbCleaner.ReadComDb`); botão restaurar |
| Maker: remover registros de USB desconectados | sem desfazer; o Windows recria ao plugar de novo (dito na confirmação) |
| Maker: remover driver serial | exportado antes para `DriversSeriais\`; "Restaurar" reinstala todos |
| Maker: instalar driver serial incluído | aparece na lista de drivers seriais e sai pelo remover (com backup) |
| Drivers: instalador NVIDIA / AMD | ponto de restauração antes, quando a proteção do sistema está ligada |
| Drivers: Windows Update | ponto de restauração antes, quando a proteção do sistema está ligada |
| Steam: opções de inicialização (`localconfig.vdf`) | opções de antes do 1º APLICAR guardadas no perfil por conta (`SteamOriginals`, a chave é o caminho do `localconfig.vdf`) e gravadas no disco antes de a Steam ser alterada (se não gravar, nada muda); botão RESTAURAR ORIGINAL devolve cada original ao mesmo arquivo e só esquece o que conseguiu devolver. O `.infopc.bak` ao lado é o arquivo de antes da 1ª gravação e não é sobrescrito. Igual na edição Linux |
| Apps, Arduino IDE, PawnIO (winget) | desinstalar pelo Windows |
| Otimização: fechar programas, limpar RAM | nada persiste |

`TweakBackup`, `ExeBackup`, `SteamOriginals` (dentro de `GameProfiles`), `ComDbBackup`, `StartupRemoved`, `Startup\` e `DriversSeriais\` ficam na pasta de dados, que é por PC e usuário: o desfazer de um PC não vale em outro. Os dados vêm de onde estiverem mais novos entre a pasta do programa e `%AppData%\InfoPC`, para o desfazer não ficar para trás quando a pasta muda (D-028).

Edição Linux (revisão de 2026-10-28, R07–R10, e o mesmo padrão do R04). Em todas, o desfazer vai para o disco antes da mudança, com `Settings.Save` (que avisa a falha; `S.Save` a engoliria):

| Alteração | Como desfazer |
|-----------|---------------|
| Otimização: perfil de CPU, `vm.swappiness`, `vm.max_map_count`, `split_lock_mitigate` (`LinuxTweaks`) | valem até reiniciar; o original vai para `TweakBackup` (`Tweak.Remember`) e para o disco antes do `sysctl`; sem ler o valor atual, nada muda. "Restaurar originais" |
| Inicialização: desativar um serviço `systemd --user` | o `disable` apaga o link que listava o serviço; ele fica em `SystemdDisabled` e continua na lista, desativado, para religar |
| Inicialização: remover | cópia em `StartupRemoved` e "Restaurar removidos". Remover uma cópia do usuário que substitui uma entrada de `/etc/xdg/autostart` deixa uma cópia desligada (`Hidden=true`), para a do sistema não voltar a valer |
| Maker: ADICIONAR AO GRUPO | o grupo vai para `GroupsAdded`; TIRAR DO GRUPO roda `gpasswd -d` só nesses grupos. Quem já está em `/etc/group` não é registrado (`InGroupFile`): falta só entrar de novo na sessão, e o desfazer não pode tirar uma associação anterior. Sem ler `/etc/group`, nada muda |
| Maker: REMOVER BRLTTY; Drivers: driver da GPU (`ubuntu-drivers autoinstall`) | `Packages.Track` grava a lista de pacotes (`PackageId`: nome, arquitetura e versão) em `PackageChanges` como operação pendente **antes** da ação; sem ler ou gravar, nada roda. Depois, guarda a diferença. Sem a lista de depois (falha ou InfoPC fechado no meio), `CompletePending` conclui ao abrir a página e marca `Recovered`: a tela avisa que a lista pode incluir o que mudou por fora. Só conclui sem instalador rodando (`InstallerActivity`, por `comm` e `cmdline` no `/proc`), porque a lista no meio da instalação parece final. Com hidepid, o usuário confirma pelo botão O INSTALADOR JÁ TERMINOU. Com outro instalador rodando, a ação nem começa. REINSTALAR O BRLTTY e DESFAZER devolvem a versão exata (no apt, uma transação, com `--allow-downgrades` se a versão mudou). Se a versão saiu do repositório, o desfazer falha, mantém o registro e mostra o comando sem versão. O pacman só instala a versão atual |
| Drivers: firmware (`fwupd`) | o InfoPC só verifica; não instala, porque muitos dispositivos não voltam de versão (D-027). A página mostra `sudo fwupdmgr update` |
| Maker: regra udev | botão REMOVER A REGRA UDEV |

## Pontos frágeis

- `Drivers/`: arquivos assinados. Qualquer byte alterado invalida a assinatura. Protegidos por `Drivers/** -text` (`.gitattributes`) e `!Drivers/**` (`.gitignore`), ambos obrigatoriamente na última linha.
- `MainWindow.xaml` (242 KB) e `MainWindow.xaml.cs` (80 KB): enormes. Faça mudanças pequenas e localizadas, senão o diff fica impossível de revisar.
- Código `unsafe` e interop nativo (NVML, DXGI, D3D11/12, ETW): um erro aqui derruba o processo sem mensagem amigável.
- Stress test e Overclock: os limites de temperatura protegem o hardware. Não remova nem afrouxe.
- O autor marcou como "em teste": Iluminação, Drivers e o inicializador de Jogos.
- 16 avisos de nulabilidade no build original (`MainWindow.Oc.cs`, `DiskHealth.cs`): não corrigir de carona.
- Edição Windows: não há testes automatizados (a verificação é build mais teste manual). A edição Linux tem a suíte em `linux/tests`.
