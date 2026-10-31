# HANDOFF — estado atual

> Sobrescrito a cada entrega. O histórico fica em `LOG.md`.

| Campo                       | Valor                                                       |
|-----------------------------|-------------------------------------------------------------|
| Última entrega por          | claude                                                      |
| Data                        | 2026-10-30                                                  |
| Assunto                     | Abertura do código (D-030): repositório público novo, drivers sem permissão retirados, avisos de terceiros e `v1.1.1`. **Aguarda revisão do codex** |
| Último commit revisado      | `86f1d7b0a6afe50fa757364141102507ec6180f0` (codex), na história antiga: veja abaixo |
| Veredito da última revisão  | **REPROVADO** — [R10-C e dados portáteis](revisoes/2026-10-30-claude-r10c-e-dados-portateis.md): R14-A/B corrigidos, ainda sem revisão |

## História nova: como o revisor enxerga as entregas

O repositório público nasceu de um commit só, e a história antiga não pode ser publicada, porque carrega os drivers retirados. Ela continua nesta máquina:

- `git log refs/historico-privado/main` mostra os commits antigos. Todo hash antigo citado em `colab/` e em `docs/` está lá.
- **Revisar as duas entregas pendentes:** `git log --no-merges --oneline 86f1d7b..refs/historico-privado/main` e `git diff 86f1d7b refs/historico-privado/main`.
- `git diff refs/historico-privado/main main` deve mostrar só os registros feitos depois da abertura.
- Nunca envie esses commits ao GitHub (AGENTS.md, seção 4).

## O que foi feito

O usuário pediu para tornar o código aberto. Ao conferir os termos, vi que a licença do FTDI proíbe disponibilizar o software a terceiros e incorporá-lo a outros programas, e que os outros fabricantes também não dão essa permissão. Ele escolheu um repositório novo sem esses drivers e pediu para não ficar com o antigo duplicado (D-030).

- **Drivers:** saem CH340, CP210x e FTDI, além de `arduino-org` e `arduino_gemma`, que não trazem licença escrita. Ficam `arduino` e `genuino`, GPL, sem nenhum byte alterado.
- **Aba Maker:** o cartão DRIVERS INCLUÍDOS mostra o Arduino e um botão PÁGINA OFICIAL para cada um dos demais, que só abre o navegador com o clique do usuário. Saiu o INSTALAR TODOS, que perdeu o sentido com um driver só.
- **`THIRD-PARTY-NOTICES.txt`:** acompanha os pacotes, com os avisos das bibliotecas e os textos MIT, Apache-2.0, GPL e da Microsoft. O `desinstalar.sh` passou a conhecer o arquivo novo.
- **Versão 1.1.1**, com o download sem login por `curl`.
- **Commits novos usam o e-mail `noreply` do GitHub**, configurado na pasta do repositório.

## Como verificar

- **Windows:** `dotnet build InfoPC.csproj -c Release -nologo --no-incremental` → 0 erros, 16 avisos (linha de base).
- **Linux, rodando no Windows:** `dotnet test linux/tests/InfoPC.Tests/InfoPC.Tests.csproj -c Release` → 237 passando.
- **Recursos embutidos na `InfoPC.dll`, lidos por reflexão:** só `arduino.inf`, `arduino.cat`, `genuino.inf` e `genuino.cat`.
- **Não verificado (honestidade):**
  - **A tela nova da aba Maker não foi aberta:** o XAML compila, mas o CI só abre o programa, sem navegar até ela.
  - **Termos:** o texto da Silicon Labs lido é uma cópia antiga, de outro repositório, porque o site bloqueou o download do pacote atual. Não é parecer jurídico.
  - Continuam valendo os limites da entrega anterior: PC de verdade, janela em GNOME ou KDE, `pkexec`, `ubuntu-drivers`, hidepid e pen drive FAT.

## Riscos e onde olhar com mais atenção

- **`THIRD-PARTY-NOTICES.txt`:** confira se falta alguma biblioteca que vai nos pacotes. A lista veio de `dotnet list package --include-transitive`, nas duas edições.
- **DirectX Shader Compiler:** os termos da Microsoft permitem distribuir o que está na "lista de distribuíveis" do pacote, e não localizei essa lista (`docs/UPSTREAM.md`, seção 7). `dxil.dll` e `dxcompiler.dll` só vão no `.zip` do Windows.
- **`MakerDrivers.Chips`:** o Arduino continua reconhecendo `arduino-org.inf` e `linino-boards_amd64.inf` já instalados no Windows, mas não os instala mais.
- **Relatórios antigos em `colab/`** citam caminhos de `C:\Users\Victor`. Ficaram como estavam, porque só mostram o nome de usuário.
