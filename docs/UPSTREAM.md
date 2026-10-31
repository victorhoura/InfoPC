# UPSTREAM — código aberto de origem

## Dados da origem

| Campo                       | Valor |
|-----------------------------|-------|
| Projeto original            | RESET EXPLODE |
| Repositório                 | https://github.com/GustavoReset/ResetExplode |
| Autor / copyright           | Copyright (c) 2026 Gustavo (GustavoReset) |
| Licença                     | MIT (`LICENSE`; adicionada no original na v0.13.0) |
| Commit importado            | `ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6` (2026-10-15), versão 0.16.0 |
| Importação no InfoPC        | 2026-10-24, commit `fc4571c` (119 de 121 arquivos com o mesmo hash git do original; só `.gitignore` e `.gitattributes` foram mesclados) |
| Renomeação                  | InfoPC 1.0.0, 2026-10-24 (seções 4 a 6) |
| Linguagem / framework       | C#, WPF, .NET 8 (`net8.0-windows`) |
| Nome antigo e variações     | Ver seção 4 |

O README do original diz: "este projeto não aceita pull requests. Fique à vontade para fazer um fork e modificar a sua própria cópia (licença MIT: mantenha o aviso de copyright)."

## 1. Licenças: o que vale para o InfoPC

- **Código do original: MIT.** Pode renomear, modificar e distribuir. Obrigatório: manter o `LICENSE` com o aviso de copyright do autor original em toda cópia distribuída. O `publicar.bat` copia o arquivo para o pacote como `LICENSE.txt`.
- **LibreHardwareMonitorLib: MPL-2.0.** Manter o aviso de licença ao distribuir. Não modificamos a biblioteca.
- **`Drivers/`: fora da MIT.** Só `arduino` e `genuino`, GPL da Arduino LLC (`Drivers/LEIA-ME.md`). **Nunca alterar nenhum byte:** a assinatura (`.cat`) cobre o conteúdo exato.
- **Drivers retirados (D-030).** O original traz CH340, CP210x e FTDI, mas o InfoPC não: a licença do FTDI proíbe disponibilizar o software a terceiros (3.1.7) e incorporá-lo a outros programas (3.1.3); a da Silicon Labs, numa cópia antiga do texto, só permite redistribuir aos seus clientes com o `.inf` modificado; a WCH não traz permissão. `arduino-org` e `arduino_gemma` saíram por não trazerem licença escrita.
- **PawnIO:** driver externo, não vem junto. O programa oferece instalar pelo winget.
- **Histórico:** como começamos do zero (D-005), os commits antigos do original, anteriores à licença MIT, não estão neste repositório.

## 2. Estratégia de git: B — importado como commit inicial (D-005)

Histórico limpo, só do InfoPC. O original não é um remoto deste repositório.

Para trazer novidades futuras do original:
1. Clonar o original numa pasta temporária, fora do InfoPC.
2. Ver o que mudou: `git log --oneline ce1d9b3..main`. Gerar o patch: `git diff --binary --full-index ce1d9b3..main > novidades.patch`.
3. No InfoPC: `git apply --3way novidades.patch`. Os arquivos importados têm o mesmo hash git do original, então o `--3way` encontra a versão base; os conflitos aparecem só nos trechos renomeados.
   - Depois da renomeação (D-012), quase todo `.cs` mudou na linha `namespace` e muitos textos mudaram de marca: espere conflitos perto desses trechos e resolva mantendo o nome InfoPC.
   - `ResetExplode.csproj` agora é `InfoPC.csproj`, e o `git apply` não segue renomeações: troque o caminho no patch antes de aplicar.
   - Rode o `scripts/verificar-nome-antigo.ps1` (seção 4) depois de aplicar: código novo do original traz a marca antiga.
4. Tratar como entrega normal (build, commit `[agente] feat: traz novidades do original ate <commit>`, revisão cruzada) e atualizar a tabela acima.

## 3. Ordem de trabalho

1. ✔ Importar e compilar sem alterações (linha de base: 0 erros, 16 avisos; pacote portátil de ~197 MB).
2. Revisão cruzada da importação (pendente: o usuário pediu a renomeação antes).
3. ✔ Renomear para InfoPC 1.0 (seções 4 a 6), em 2026-10-24.
4. Revisão cruzada da renomeação (pendente, junto com o item 2).

## 4. Variações do nome antigo

Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verificar-nome-antigo.ps1 -Nome "<termo>"` (acrescente `-DiferenciarMaiusculas` onde a tabela indica).

O script considera esperadas as ocorrências em `LICENSE`, `docs/UPSTREAM.md`, `docs/DECISOES.md`, `colab/` e no próprio script, além das linhas de crédito ao original ("baseado no RESET EXPLODE", "RESET EXPLODE v0.16", "[RESET EXPLODE](", "Gustavo (GustavoReset)", "Gustavo/GustavoReset").

| Termo                                 | Na importação           | Depois da renomeação |
|---------------------------------------|-------------------------|----------------------|
| `ResetExplode`                        | 111 linhas, 70 arquivos | 0 inesperadas |
| `RESET EXPLODE`                       | 43 linhas, 20 arquivos  | 0 inesperadas (só créditos) |
| `RESET-EXPLODE`                       | 1 linha (nome do relatório) | 0 |
| `Explode`                             | inclui `GrpExplode` e " EXPLODE" do título | 0 inesperadas (só créditos) |
| `GustavoReset`                        | 3 linhas, 3 arquivos    | 0 inesperadas (`LICENSE` e créditos) |
| `RESET ` com `-DiferenciarMaiusculas` | 146 linhas, 36 arquivos (submarcas) | 0 inesperadas |

Sem `-DiferenciarMaiusculas`, `RESET ` também acha textos comuns como "reset de driver de vídeo"; por isso a opção. O `"RESET"` sozinho (barra de título e `author` do Razer Chroma) foi conferido com `git grep -n "\bRESET\b" -- '*.cs' '*.xaml'`: só sobra a linha de créditos do rodapé do menu.

## 5. Submarcas → nomes simples (D-007, D-015)

| Antes                                  | InfoPC: categoria do menu | Título da página e avisos |
|----------------------------------------|---------------------------|---------------------------|
| RESET EXPLODE (nome do produto)        | —                         | InfoPC |
| RESET EXPLODE (categoria: Sistema, Sensores, Placa de vídeo, Discos, Energia, Rede) | HARDWARE | — |
| RESET JOGOS                            | JOGOS                     | Biblioteca (página do inicializador); "Jogos" nos avisos |
| RESET DESEMPENHO                       | DESEMPENHO                | — |
| RESET FERRAMENTAS                      | FERRAMENTAS               | — |
| RESET MAKER                            | MAKER                     | Maker |
| RESET BENCH                            | —                         | Benchmark (janelas dos testes: "InfoPC · Cena de jogo", "InfoPC · Ray tracing") |
| RESET OTIMIZA                          | —                         | Otimização; o botão virou OTIMIZAR |
| RESET OC                               | —                         | Overclock |
| RESET INTEGRAÇÃO                       | —                         | Integração (a proposta era "Iluminação"; ficou o nome do item do menu, D-015) |
| RESET DRIVER                           | —                         | Drivers |

## 6. Checklist de renomeação (feito em 2026-10-24, commits `77d2a6b`..`df2b27c`)

- [x] `ResetExplode.csproj` → `InfoPC.csproj`; `AssemblyName` e `Product` → `InfoPC`; versão 1.0.0 (D-011).
- [x] Namespace `ResetExplode` → `InfoPC` nos `.cs` e em `x:Class` / `clr-namespace` nos `.xaml` (D-012).
- [x] Mutex `Local\InfoPC.SingleInstance`. `FindWindow` em `App.xaml.cs` procura "InfoPC", que tem de ser igual ao `Title` da janela. O atalho global Ctrl+Shift+O continua igual: se os dois programas estiverem abertos, só o primeiro registra o atalho.
- [x] Título da janela, barra de título ("Info" + "PC"), menu lateral, toasts, overlay; submarcas (seção 5).
- [x] Assets `app.ico`, `logo.png`, `logo-dark.png`, `logo-badge.png` provisórios nos mesmos tamanhos (D-008, D-016).
- [x] Pastas: `%AppData%\InfoPC`, `%TEMP%\InfoPC\...`, `Documentos\InfoPC-Drivers`, arquivos temporários dos benchmarks (`InfoPCBench`), sessão ETW `InfoPC-Frames`, classes de janela, sufixo de backup `.infopc.bak`.
- [x] **`Optimizer.cs`:** o processo protegido agora é `"InfoPC"`, o nome do novo .exe.
- [x] User-Agent `InfoPC` (`CorsairSdkInstaller.cs`, `DriverManager.cs`).
- [x] Razer Chroma (`LightBackends.cs`): título, descrição, autor e contato = InfoPC (sem o GitHub do original). OpenRGB: nome do cliente "InfoPC".
- [x] Relatório HTML com a marca InfoPC (`ReportBuilder.cs`); arquivo `InfoPC-relatorio-<data>`.
- [x] `publicar.bat`: `taskkill` do `InfoPC.exe`, saída `..\InfoPC-dist`, e agora copia também `LICENSE` como `LICENSE.txt`; `.gitignore` (`InfoPC-dist/`).
- [x] `.github/workflows/publicar.yml` removido (D-013).
- [x] `README.md`, `LEIA-ME.txt`, `NOVIDADES.md` reescritos para o InfoPC; sem "Download oficial" nem releases do original.
- [x] `Drivers/LEIA-ME.md`: só o texto. Os demais arquivos de `Drivers/` não mudaram (`git diff --quiet` conferido).
- [x] **Mantido:** `LICENSE` intacto.
- [x] Créditos "InfoPC é baseado no RESET EXPLODE, de Gustavo (GustavoReset), licença MIT." no README, no LEIA-ME.txt, no NOVIDADES.md e no rodapé do menu (texto curto + dica completa).
- [x] Script sem ocorrências inesperadas para cada termo da seção 4.
- [x] Build com 0 erros e 16 avisos (os mesmos da linha de base); pacote portátil gerado.
- [ ] O programa abre e as abas carregam: teste manual do usuário (pede administrador).

## 7. Bibliotecas de terceiros

| Biblioteca (NuGet)                          | Versão | Licença | Observação |
|---------------------------------------------|--------|---------|------------|
| LibreHardwareMonitorLib                     | 0.9.6  | MPL-2.0 | Sensores. Traz HidSharp, BlackSharp.Core, DiskInfoToolkit, RAMSPDToolkit-NDD, Mono.Posix (MPL-2.0, Apache-2.0 e MIT, em `THIRD-PARTY-NOTICES.txt`) |
| Microsoft.Diagnostics.Tracing.TraceEvent    | 3.2.8  | MIT     | ETW (FPS). Traz `msdia140.dll` e `KernelTraceControl.dll` da Microsoft (no pacote MIT; aviso em `THIRD-PARTY-NOTICES.txt`) |
| Vortice.Direct3D11 / Direct3D12 / D3DCompiler / Dxc | 3.8.3 | MIT | Direct3D. `Vortice.Dxc` traz `dxcompiler.dll` e `dxil.dll` da Microsoft (termos em `THIRD-PARTY-NOTICES.txt`; só vão no `.zip` do Windows, não no repositório) |
| System.Management                           | 10.0.2 | MIT     | WMI |
| System.Security.Cryptography.ProtectedData  | 10.0.2 | MIT     | DPAPI (chave da API cifrada) |

Edição Linux (`linux/`, D-017). Versões e licenças conferidas nos `.nuspec` do pacote:

| Biblioteca (NuGet)                          | Versão   | Licença | Observação |
|---------------------------------------------|----------|---------|------------|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia, Avalonia.X11, Avalonia.FreeDesktop, Avalonia.Remote.Protocol | 11.3.22 | MIT | Interface. Os pacotes `Avalonia.Win32`, `Avalonia.Native` e `Avalonia.Angle.Windows.Natives` entram na restauração, mas não no binário Linux |
| Avalonia.Fonts.Inter                        | 11.3.22  | MIT (pacote) | Embute a fonte Inter, licença SIL OFL 1.1: manter o aviso de copyright da fonte se o pacote for redistribuído (aviso em `THIRD-PARTY-NOTICES.txt`) |
| SkiaSharp + SkiaSharp.NativeAssets.Linux    | 2.88.9   | MIT     | Desenho. A biblioteca Skia nativa é BSD-3-Clause (aviso em `THIRD-PARTY-NOTICES.txt`) |
| HarfBuzzSharp + HarfBuzzSharp.NativeAssets.Linux | 8.3.1.1 | MIT | Texto. O HarfBuzz nativo é "Old MIT" (aviso em `THIRD-PARTY-NOTICES.txt`) |
| Tmds.DBus.Protocol, MicroCom.Runtime, System.IO.Pipelines | 0.21.3 / 0.11.0 / 8.0.0 | MIT | Dependências do Avalonia |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Avalonia.Headless.XUnit | 2.9.3 / 3.1.4 / 17.14.1 / 11.3.22 | Apache-2.0 / MIT | Só nos testes; não vão no executável |

O executável do Linux embute o runtime do .NET 8 (MIT). Nenhum código do Windows com licença restrita (`msdia140.dll`, `dxcompiler.dll`...) entra na edição Linux.

Conferido para a abertura do código (D-030). Os termos da Microsoft para o DirectX Shader Compiler permitem distribuir o que está na "lista de distribuíveis" do pacote, lista que não localizei no pacote do NuGet, e pedem que quem distribui proteja a Microsoft contra reclamações sobre o próprio aplicativo.
