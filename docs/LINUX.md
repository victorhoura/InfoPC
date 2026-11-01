# InfoPC para Linux

> Edição **nativa** do InfoPC para Linux (D-017), versão 1.1. Mesmas áreas do menu da edição Windows (Hardware, Jogos, Desempenho,
> Ferramentas, Maker), com a interface em Avalonia e a leitura de hardware feita pelo que o Linux oferece: `/proc`, `/sys`, hwmon,
> NVML, `smartctl`, `dmidecode`, Steam/Lutris/Heroic e o que mais estiver instalado. Baseado no RESET EXPLODE, de Gustavo (GustavoReset), licença MIT.

## 1. Instalar

Distribuições com glibc, em x86_64 ou arm64: Ubuntu, Debian, Mint, Fedora, Arch, Manjaro, openSUSE e derivadas. Alpine (musl) não é suportada.

**Sem compilar:** baixe o pacote pronto da Release, sem login. Em arm64, troque `linux-x64` por `linux-arm64`.

```bash
curl -fsSL https://github.com/victorhoura/InfoPC/releases/download/v1.1.7/InfoPC-1.1.7-linux-x64.tar.gz | tar xz && ./InfoPC/InfoPC
```

A janela usa X11: em sessão Wayland ela abre pelo XWayland, conferido em GNOME e KDE. Sem XWayland ou sem sessão gráfica, o programa explica o que falta, e o modo texto continua funcionando.

O programa fica na pasta `InfoPC`, com os dados em `InfoPC/dados`. Para ter o comando `infopc` e o atalho no menu, use a instalação abaixo, que compila a partir do clone.

```bash
git clone https://github.com/victorhoura/InfoPC.git ~/InfoPC
bash ~/InfoPC/linux/instalar.sh
```

O instalador, em ordem:

1. confere sistema e arquitetura (`x86_64` → `linux-x64`, `aarch64` → `linux-arm64`);
2. junta numa **única pergunta** os pacotes do sistema que faltam (curl/tar e bibliotecas do .NET; bibliotecas gráficas, `smartmontools`,
   `pciutils`, `usbutils`, `lm-sensors`, `dmidecode`, `xdg-utils`, `polkit`) e os instala com `sudo`. **É o único passo com `sudo`**;
   `--sem-pacotes` pula os recomendados;
3. instala o **SDK do .NET 8 na sua conta** (`~/.dotnet`, script oficial `dot.net/v1/dotnet-install.sh`) se não houver um SDK 8 ou mais novo;
4. compila um **único executável autossuficiente** (`dotnet publish -r linux-<arch> --self-contained -p:PublishSingleFile=true`);
5. copia para `~/.local/opt/InfoPC`, cria o comando `~/.local/bin/infopc` e o atalho `InfoPC` no menu de aplicativos.

Nada é instalado no sistema além dos pacotes da etapa 2: sem `.deb`/`.rpm`, Snap, Flatpak ou AppImage (requisito 1). A primeira compilação baixa
~150 MB de pacotes NuGet e leva alguns minutos. Rodar de novo atualiza o programa e **mantém** a pasta `dados/`.

| Opção | Efeito |
|-------|--------|
| `--yes` | não pergunta nada |
| `--prefix PASTA` | outra pasta de instalação |
| `--sem-atalho` | não cria o comando nem o atalho do menu |
| `--sem-pacotes` | não instala pacotes recomendados |
| `--so-compilar PASTA` | só compila o executável para `PASTA` |

Remover: `bash ~/InfoPC/linux/desinstalar.sh` (mantém `dados/`) ou `... --tudo` (apaga também configurações e histórico).
O .NET em `~/.dotnet` e os pacotes do sistema não são removidos. O desinstalador só mexe numa pasta que tenha o `InfoPC` ou a
marca `.instalado-pelo-infopc` (gravada pelo instalador) e nada além dos arquivos do programa e de `dados/`, inclusive ocultos;
apaga item por item e só remove a pasta se ela ficar vazia.

Atualizar: `git -C ~/InfoPC pull` e `bash ~/InfoPC/linux/instalar.sh`.

## 2. Usar

- `infopc` abre a janela. Menu lateral com os mesmos grupos da edição Windows; tema escuro/claro; visão geral com gráficos dos últimos minutos.
- Modo texto, também por SSH ou em script (não abre janela):

| Comando | Mostra |
|---------|--------|
| `infopc --info` | sistema, processador, memória, vídeo e discos |
| `infopc --sensores [--seguir]` | temperatura, carga, clock, consumo, ventoinhas |
| `infopc --gpu` | detalhes das placas de vídeo |
| `infopc --discos [--smart]` | discos; `--smart` lê o S.M.A.R.T. (pede a senha) |
| `infopc --energia` | bateria, fonte, perfil de energia |
| `infopc --relatorio [arquivo]` / `--json [arquivo]` | relatório HTML / JSON |
| `infopc --diagnostico` | o que foi encontrado e o que falta instalar para ler tudo |
| `infopc --versao`, `--ajuda` | versão, ajuda |

## 3. Privilégios, dados e internet

- **Abre como usuário comum.** Ao contrário do Windows (D-006), no Linux quase tudo se lê sem root (`/proc`, `/sys`, hwmon, NVML).
- O que precisa de root pede a senha **no momento do clique**, pelo `pkexec` (polkit), com comando fixo e sem texto livre (D-019):
  S.M.A.R.T. (`smartctl`), módulos de RAM (`dmidecode`), ajustes da Otimização (`sysctl`, governador de CPU), instalar apps/drivers/firmware,
  regras udev e grupos do Maker. Sem `pkexec`, o programa mostra o comando para você rodar num terminal.
- **Nada muda no sistema sem clique** e todo ajuste tem "Restaurar originais" (o valor anterior fica guardado em `dados/`).
- **Dados portáteis** (D-020): configurações e histórico em `dados/` ao lado do executável; se a pasta não for gravável, `~/.local/share/InfoPC`.
  `INFOPC_DADOS=<pasta>` força outro lugar. A chave da API de FPS fica em arquivo com permissão `0600`. Relatórios vão para a pasta Documentos.
- **Internet só por ação sua**: API de FPS (com sua chave), teste de rede, instalação de apps/drivers, busca de ids PCI. Sem telemetria, sem checagem de atualização.

## 4. O que foi para onde (Windows → Linux)

| Área | Windows | Linux |
|------|---------|-------|
| Sistema | WMI | `/etc/os-release`, `/proc/cpuinfo`, `/proc/meminfo`, DMI em `/sys/class/dmi`, `dmidecode` (RAM por módulo), `lspci`/`pci.ids` |
| Sensores | LibreHardwareMonitor + PawnIO | hwmon (`k10temp`, `coretemp`, `nct*`, `amdgpu`…), `thermal_zone`, RAPL (consumo da CPU), `/proc/stat`, taxa de disco e rede, bateria |
| Placa de vídeo | NVML, DXGI, D3D12, WMI | NVML (`libnvidia-ml.so.1`), `/sys/class/drm` (amdgpu/i915/xe), ids PCI; banco `GpuSpecDb` compartilhado |
| Discos | WMI + S.M.A.R.T. | `/sys/block` e `/proc/self/mountinfo` (sem depender de `lsblk`), `smartctl -j` via pkexec; avaliador de saúde compartilhado |
| Energia | `BatteryInfo` | `/sys/class/power_supply`, `power-profiles-daemon`/governador |
| Rede | `NetworkTest` | mesmo teste (compartilhado), interfaces por `/sys/class/net` |
| Jogos | Steam, Epic, GOG… | Steam (`libraryfolders.vdf`, `appmanifest`), Lutris, Heroic, programas manuais; Proton/Wine detectados |
| FPS estimado, Gargalo | `GameCatalog`, `BottleneckAnalyzer` | mesmo catálogo/modelo (compartilhados) com leitura ao vivo do hwmon/NVML |
| Overlay | janela WPF + ETW | janela Avalonia transparente com cliques atravessando (X11/XWayland, extensão XShape), com os itens de hardware; o **FPS** vem do **MangoHud**, cuja configuração o InfoPC escreve a partir dos mesmos itens |
| Benchmark | `BenchRenderer`, `GameSimBench` compartilhados | CPU e simulação iguais; disco com `O_DIRECT`; memória/carregamento adaptados; GPU via `vkmark`/`glmark2` se instalados |
| Stress test | `StressTester` | versão adaptada (mesma carga e limites), RAM verificada, parada por temperatura |
| Overclock | NVML + WHEA | `nvidia-smi` (limite de energia, clock), verificação de estabilidade, erros de hardware no log do kernel (`journalctl`/`dmesg`: MCE, reset da GPU, disco, falta de memória), guia de BIOS |
| Otimização | `Optimizer`, `WindowsTweaks` | fechar programas do usuário, `drop_caches` opcional, governador de CPU, `vm.swappiness`, `vm.max_map_count`, `split_lock_mitigate` |
| Inicialização | Run/Registro | `~/.config/autostart` e `/etc/xdg/autostart` (cópia com `Hidden=true`, o do sistema nunca é tocado) e unidades `systemd --user` |
| Instalar apps | winget | `apt`/`dnf`/`pacman`/`zypper` (via pkexec) e Flatpak (sem senha); catálogo fixo de nomes |
| Drivers | `pnputil`, Windows Update | driver do kernel em uso por dispositivo PCI (`/sys/.../driver`), `ubuntu-drivers` (GPU NVIDIA, com DESFAZER), `fwupd`/LVFS (firmware: só verifica; instalar fica no terminal, D-027) |
| Integração | iCUE, Razer | **OpenRGB** (servidor SDK), com dica das regras udev; efeitos compartilhados (`LightEngine`) |
| Maker | COM, ComDB, drivers CH340/FTDI | portas `/dev/tty*` por sysfs, grupo `dialout`/`uucp` (com TIRAR DO GRUPO), regra udev, remoção do `brltty` (com REINSTALAR), `modprobe` de `ch341`/`cp210x`/`ftdi_sio` |
| Avisos de dispositivo | `WM_DEVICECHANGE` | `DeviceWatcher`: compara USB e discos removíveis do sysfs a cada 2 s |

**Não se aplica no Linux:** ETW/PresentMon, Direct3D 11/12 e DXR (os testes de GPU próprios, FurMark-like e ray tracing, não existem: usa-se `vkmark`/`glmark2`),
PawnIO (o kernel já expõe temperatura e RAPL), DPAPI (a chave vai em arquivo `0600`), ComDB e registro, `pnputil`, Windows Update e winget.
Overlay em Wayland puro não é possível (só X11/XWayland).

## 5. Código

```
linux/
  Directory.Build.props            versão 1.1.7, InvariantGlobalization
  instalar.sh  desinstalar.sh      instalação sem root
  src/InfoPC.Core/                 lógica sem interface (net8.0, namespace InfoPC)
  src/InfoPC.Linux/                janela Avalonia (exe InfoPC)
  tests/InfoPC.Tests/              xUnit + Avalonia.Headless; fixtures de /proc e /sys em Fixtures/
```

`InfoPC.Core` **reaproveita da raiz, por link de arquivo, sem copiar** (D-017, D-023): `InfoNode`, `Vdf`, `GameCatalog`, `GpuSpecDb`, `BenchRenderer`,
`GameSimBench`, `ReportBuilder`, `OverlayMetrics`, `HowManyFpsClient`, `GameRow`, `NetworkTest`, `DiskHealthModels`, `BottleneckModel`,
`MemoryBenchRam`, `OpenRgbBackend`, `DataFolder`. Mudar um deles na edição Windows afeta a Linux, por isso o CI do Linux também dispara quando eles mudam.
Esses arquivos só podem ter mudanças neutras (sem `System.Windows`, WMI ou P/Invoke do Windows).

Adaptados a partir do Windows (lógica reescrita, mesma ideia e mesmos limites): `DiskBench`, `StressTester`, `MemoryBenchLoading`, `StartupManager`, `Optimizer`.
O `InfoPC.csproj` da raiz exclui `linux\**` da compilação do Windows.

Pacotes NuGet da edição Linux: Avalonia 11.3 (MIT), Avalonia.Fonts.Inter (fonte Inter, SIL OFL), SkiaSharp (MIT, vem com o Avalonia). Testes: xUnit, Avalonia.Headless.

## 6. Verificação

- **Testes** (`dotnet test linux/tests/InfoPC.Tests -c Release`): 150+ casos com fixtures de `/proc` e `/sys` de um notebook AMD, um desktop Intel e um Raspberry Pi,
  mais testes de interface sem tela (as telas são desenhadas e conferidas) e os que rodam só em Linux (nomes de sysfs com `:`, links simbólicos, `O_DIRECT`).
- **CI** (`.github/workflows/linux.yml`): testes, publicação `linux-x64` em binário único e modo texto no próprio runner, em Ubuntu 22.04 e 24.04.
  No mesmo workflow, o job `pacotes` usa o gerenciador real (apt, dnf, pacman, zypper) em contêineres descartáveis de Debian 12, Ubuntu 24.04, Fedora 41, Arch e openSUSE:
  lê a lista, instala o `tree`, recupera com o instalador rodando, remove e desfaz, volta um pacote a uma versão anterior (`linux/tests/criar-repo-teste.sh`) e usa `usermod` e `gpasswd`. Esses testes só agem com `INFOPC_TEST_REAL_PACKAGES=1`, que só esse job liga.
  `.github/workflows/publicar.yml` (manual, D-029): gera os pacotes, abre os programas de verdade (Windows sem UAC no runner, Linux em tela virtual, x64 e arm64), roda o pacote pronto em contêineres limpos de cinco distribuições e publica a Release.
  `.github/workflows/linux-instalador.yml`: roda o `instalar.sh` como usuário comum em Debian 12, Ubuntu 24.04, Fedora, Arch e openSUSE,
  confere `infopc --versao/--diagnostico/--info/--json` e a desinstalação, e confirma que compila para `linux-arm64`.
- **Não verificado:** a janela em um desktop Linux real (GNOME/KDE, X11 e Wayland), sensores em hardware físico variado, GPU AMD/Intel, MangoHud, OpenRGB e os ajustes via
  `pkexec` em máquina de verdade. O teste manual do usuário é a única forma de cobrir isso.

## 7. Limites conhecidos

- Temperatura/consumo dependem dos drivers do kernel (`k10temp`, `coretemp`, `zenpower` etc.) e das permissões de `/sys/class/powercap` (RAPL pode exigir root em kernels recentes).
- O FPS de jogos só aparece pelo MangoHud (o overlay do InfoPC mostra o hardware, não o FPS: medir de fora exigiria injetar código no jogo).
- Wayland: janela e avisos funcionam; o overlay que atravessa cliques depende de X11/XWayland.
- Heroic: o início do jogo usa o URI `heroic://launch/<runner>/<app>`, ainda não conferido contra todas as versões.
- Nomes de pacote variam entre versões das distribuições; se um não existir, o instalador tenta um por um e avisa o que ficou de fora.
