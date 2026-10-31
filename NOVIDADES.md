## Novidades da v1.1.4

**Dados e desfazer, nas duas edições**
- Se outro programa estiver com o arquivo de dados preso ao abrir, o InfoPC avisa e não troca nenhuma cópia. Antes, podia carregar a cópia anterior sem avisar.
- Abrir com o arquivo preso e gravar depois não perde mais os dados: a cópia dele é guardada antes.
- Com três cópias dos dados, uma cópia comprovadamente anterior nunca é escolhida, e o aviso diz qual pasta está em uso e onde ficou o resto.

## Novidades da v1.1.3

**Dados e desfazer, nas duas edições**
- Ao trocar entre a pasta do programa e a pasta do usuário, o programa sabe qual cópia dos dados veio depois sem depender da data do arquivo, que num pen drive FAT tem precisão de 2 segundos. Assim, registros de desfazer não somem da tela.
- Se duas cópias tiverem alterações diferentes, nenhuma é sobrescrita em silêncio: o programa avisa e guarda a outra.

## Novidades da v1.1.2

Correções de defeitos achados usando o programa de verdade:

**Windows**
- Otimização: depois de RESTAURAR ORIGINAIS, os interruptores mostram a posição real. O Windows já voltava certo, mas a tela continuava na posição antiga.
- Biblioteca: gravar e restaurar as opções da Steam deixa o arquivo da Steam igual ao de antes. Antes, a tabulação de outra opção (o atalho do overlay) virava o texto `\t`.
- Visão geral, Placa de vídeo, Gargalo e Stress test: botões e cartões não se sobrepõem nem cortam o texto na janela padrão.

**Linux**
- Sem sessão gráfica, em Wayland sem XWayland ou sem biblioteca gráfica, o programa explica o que falta em vez de abortar com core dump.
- Autorização de administrador recusada: a mensagem agora diz isso e mostra o comando para o terminal.

## Novidades da v1.1.1

**Código aberto**
- O código agora é público, e os pacotes baixam sem login. No Linux: `curl -fsSL https://github.com/victorhoura/InfoPC/releases/download/v1.1.1/InfoPC-1.1.1-linux-x64.tar.gz | tar xz && ./InfoPC/InfoPC`
- Avisos das bibliotecas de terceiros em `THIRD-PARTY-NOTICES.txt`, junto com o programa.

**Maker, no Windows**
- Os drivers CH340, CP210x e FTDI não vêm mais dentro do programa, porque os termos dos fabricantes não permitem publicá-los. A aba mostra o botão PÁGINA OFICIAL de cada um. O driver Arduino continua incluído para as placas de VID 2341; `arduino-org` e `gemma` saíram por não trazerem licença.

## Novidades da v1.1

**Pacotes prontos**
- Windows e Linux (x64 e arm64) na página Releases do GitHub, sem compilar. No Linux, um comando baixa e abre o programa.

**Edição Linux**
- Nova edição nativa para Linux (x86_64 e arm64), em `linux/`, com as mesmas áreas do menu da edição Windows.
- Instalação em um comando (`linux/instalar.sh`): compila o programa a partir do clone, instala na pasta do usuário e cria o comando `infopc`
  e o atalho do menu. Só pede `sudo` para os pacotes de apoio, e só se você aceitar. `linux/desinstalar.sh` remove tudo.
- Abre como usuário comum; a senha de administrador (`pkexec`) é pedida só na ação que precisa de root.
- Configurações e histórico na pasta `dados/` ao lado do programa.
- Modo texto para terminal e SSH: `infopc --info`, `--sensores`, `--gpu`, `--discos`, `--energia`, `--relatorio`, `--json`, `--diagnostico`.
- Testes automáticos e CI em Linux real. Detalhes e limites em `docs/LINUX.md`.

**Dados junto do programa (as duas edições)**
- O Windows deixa de gravar em `%AppData%`: configurações, histórico e backups de desfazer ficam em `dados\` ao lado do `.exe`. O que já estava lá é copiado na primeira vez.
- Cada PC e usuário tem sua própria subpasta. Levar o programa num pen drive não aplica o desfazer de um PC em outro.
- Sem poder gravar na pasta do programa, os dados ficam em `%AppData%\InfoPC` ou `~/.local/share/InfoPC`, e seguem o caminho de volta quando ela aceitar de novo. O programa avisa, e o que havia fica guardado em uma pasta `.anterior`.
- Um arquivo de configurações ilegível fica guardado como `settings.json.corrompido`, em vez de ser sobrescrito.

**Internet só quando você pede (as duas edições)**
- FPS estimado: com a chave da HowManyFPS salva, os dados reais vêm só com o botão BUSCAR FPS REAL. Antes, a página mostra
  a estimativa local e o que já foi consultado nos últimos 7 dias. No Windows, a consulta acontecia ao abrir o programa.
- Drivers (Windows): abrir a página só lê os drivers instalados; a NVIDIA e a AMD são consultadas no VERIFICAR TUDO.

**Desfazer**
- Steam (as duas edições): novo botão RESTAURAR ORIGINAL devolve as opções de inicialização que o jogo tinha antes do InfoPC,
  em cada conta da Steam em que elas foram trocadas. A cópia `localconfig.vdf.infopc.bak` passa a guardar sempre o arquivo de
  antes da primeira alteração.
- Perfis de jogo (Windows): forçar a GPU dedicada ou desativar as otimizações de tela cheia guarda o valor anterior do .exe,
  e desmarcar devolve esse valor. Antes, jogar com essas opções desmarcadas apagava o que você tivesse configurado no Windows.
- O original é gravado antes de a Steam ou o .exe serem alterados. Se as configurações não puderem ser gravadas, nada é alterado
  e o programa mostra o motivo.
- Otimização (Windows): se um ajuste não voltar no "Restaurar originais", o original dele continua guardado para tentar de novo.
  Antes, todos os originais eram apagados mesmo quando a restauração falhava.
- Drivers do Windows Update: ponto de restauração antes de instalar, como já acontecia com o driver da NVIDIA e da AMD.
- Otimização: "Restaurar originais" do mouse volta exatamente ao que era, inclusive quando o valor não existia. A aceleração do
  mouse volta com os valores na ordem certa já na sessão atual (a ordem errada vinha do original).
- Otimização (Windows): se o Windows não conseguir voltar ao plano de energia anterior, o ajuste mostra a falha e o original
  continua guardado. O plano "Alto desempenho" que o InfoPC cria quando ele não existia é apagado ao restaurar.
- Perfis de jogo (Windows): se o plano de energia não voltar ao anterior quando o jogo fecha, a linha de estado avisa.
- Otimização, Inicialização e Maker, nas duas edições: o original é gravado antes de qualquer mudança. Se não der para gravar,
  nada muda.
- Inicialização (Windows): um programa desativado que foi removido volta desativado no "Restaurar removidos".
- Inicialização (Linux): um serviço do systemd desativado continua na lista para ser religado. Remover a cópia que desliga uma
  entrada do sistema não a liga mais de novo.
- Maker (Linux): TIRAR DO GRUPO desfaz o ADICIONAR AO GRUPO, e REINSTALAR O BRLTTY desfaz a remoção do brltty. Quem já estava
  no grupo e só precisa entrar de novo na sessão recebe esse aviso, e o desfazer não tira uma associação que já existia.
- Drivers (Linux): o driver da GPU instalado pelo InfoPC ganha DESFAZER, que remove o que entrou e devolve o que havia antes, na
  mesma versão e arquitetura. A lista de antes vai para o disco antes da instalação, então o desfazer sobrevive a uma falha ou ao fechar o programa no meio.
  Se você reabrir o InfoPC com o instalador ainda rodando, ele espera o instalador terminar para concluir a lista.
  O firmware passa a ser só verificado: instalar não tem como ser desfeito, então a página mostra o comando para o terminal.

**Diagnóstico no Linux**
- Stress e verificador: se o programa de carga da GPU fechar com erro, o teste para e informa o motivo, em vez de reiniciá-lo e poder dar ESTÁVEL.
- O overlay do InfoPC continua atualizando quando você troca de página.
- Stress test e verificador de estabilidade: se o programa de carga da GPU fechar sozinho, o teste para e diz por quê. Antes,
  o verificador podia dizer ESTÁVEL sem a GPU ter tido carga.
- Verificador de estabilidade: sem permissão para ler o log do kernel, o resultado é INCONCLUSIVO, e não mais ESTÁVEL.
- O atalho do menu funciona mesmo com `%`, aspas ou `$` no caminho de instalação.

## Novidades da v1.0

Primeira versão do InfoPC, baseada no RESET EXPLODE v0.16, de Gustavo (GustavoReset), licença MIT.
Os recursos são os mesmos do original. O que muda:

**Nome novo**
- O programa agora se chama InfoPC (`InfoPC.exe`), com logo e ícone provisórios.

**Menu com nomes simples**
- Categorias: Hardware, Jogos, Desempenho, Ferramentas e Maker.
- Páginas e avisos usam os nomes do menu: Benchmark, Otimização, Overclock, Integração, Drivers, Biblioteca.
- O botão da Otimização passa a se chamar OTIMIZAR.

**Pastas próprias**
- Configurações e histórico em `%AppData%\InfoPC`, temporários em `%TEMP%\InfoPC` e backup de drivers em
  `Documentos\InfoPC-Drivers`. O InfoPC não lê as configurações do programa original, e os dois podem rodar no mesmo PC
  (se os dois estiverem abertos, o atalho do overlay, Ctrl+Shift+O, fica com o que abriu primeiro).

**Pacote portátil**
- O `publicar.bat` gera `InfoPC.exe`, `LEIA-ME.txt` e `LICENSE.txt`. Não há publicação automática no GitHub.
