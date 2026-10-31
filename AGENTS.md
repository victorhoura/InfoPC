# AGENTS.md — Regras do projeto InfoPC

Fonte única de regras para os agentes de IA deste repositório.

- **Codex** lê este arquivo automaticamente.
- **Claude Code** lê o `CLAUDE.md`, que importa este arquivo.

Se uma regra mudar, mude aqui. Não duplique regras em outros arquivos.

---

## 1. O projeto

- **InfoPC** é um software **portátil** para Windows e Linux: um único executável (.exe no Windows, binário ELF no Linux) que roda direto da pasta, sem instalador de sistema. As duas edições (WPF na raiz, Avalonia em `linux/`) têm as mesmas áreas do menu (D-017).
- **Não existe website** do produto, nem haverá.
- Base: **RESET EXPLODE v0.16** (C# / WPF / .NET 8, licença MIT), importado e renomeado para **InfoPC 1.0**. É uma ferramenta de diagnóstico e ajuste do PC: informações do sistema, sensores, benchmarks, stress test, jogos, otimização, drivers. Origem, licença e renomeação: `docs/UPSTREAM.md`. Mapa do código e comandos: `docs/ARQUITETURA.md`.
- **Fase atual:** edição Linux (Fase 4, `docs/LINUX.md`) entregue como InfoPC 1.1, aguardando o teste manual do usuário em um Linux com desktop. A revisão cruzada de todo o histórico (importação, renomeação e Linux) segue pendente. Fase 3, ajustes aos requisitos do Windows, depois (`colab/TAREFAS.md`).

## 2. Requisitos do produto

1. **Sem instalador.** A entrega é um único executável autossuficiente (.NET embutido), opcionalmente num .zip (Windows) ou .tar.gz (Linux). Nada de MSI, MSIX, NSIS, Inno Setup, .deb, .rpm, Snap, Flatpak ou similares. No Linux, `linux/instalar.sh` só compila e copia o programa para a pasta do usuário, sem root (D-021).
2. **Pode exigir administrador ao abrir**, porque os sensores de baixo nível precisam (D-006). Não acrescente outros motivos de elevação. No Linux o programa abre como usuário comum e pede a senha (`pkexec`) só na ação que precisa de root (D-019).
3. **Portátil de verdade.** Configurações e dados do programa ficam na pasta `dados/` ao lado do executável, numa subpasta por PC e usuário. Sem poder gravar nela, ficam em `%AppData%\InfoPC` ou `~/.local/share/InfoPC` (D-020, D-028).
4. **Alteração no sistema só por ação do usuário e com desfazer.** Registro, drivers, programas de inicialização e ajustes do Windows: sempre por um botão que o usuário clicou, com aviso claro e opção de restaurar. Nunca na abertura do programa.
5. **Internet só quando o usuário aciona um recurso que precisa dela** (busca de driver, teste de rede, winget, API de FPS...). Nada automático: sem telemetria, sem checagem de atualização, sem chamadas ao abrir, sem links para o site do projeto original.
6. **Sem pré-requisitos para instalar**, exceto o driver PawnIO (temperatura e consumo da CPU), que o programa oferece instalar com um clique, como nos itens 4 e 5.
7. **Alvo:** Windows 10 e 11, x64; Linux x64 e arm64 com glibc (Ubuntu e Debian, Fedora, Arch, openSUSE e derivadas) (D-017).

Se o código violar algum destes requisitos, registre em `colab/TAREFAS.md` e pergunte ao usuário antes de mudar.

## 3. Licença e autoria

- Renomear troca a **marca** do produto, nunca a **autoria**. Mantenha `LICENSE` (copyright de Gustavo/GustavoReset, MIT), cabeçalhos de copyright e créditos dos autores originais.
- **O que está em `Drivers/` não se altera, nem um byte (nem fim de linha):** são drivers dos fabricantes, fora da MIT, e a assinatura digital (`.cat`) cobre o conteúdo exato. Única exceção: `Drivers/LEIA-ME.md`, que é documentação.
- **O repositório é público (D-030).** Driver ou outro binário de terceiro só entra se a licença permitir publicar. CH340, CP210x e FTDI não permitem: não os traga de volta.
- **`THIRD-PARTY-NOTICES.txt`** acompanha os pacotes. Biblioteca nova que vá no pacote entra nele e em `docs/UPSTREAM.md`.
- Antes de trazer código ou biblioteca de terceiros, confira a licença e registre em `docs/UPSTREAM.md`.

## 4. Dois agentes, um repositório

| Agente   | Ferramenta   | Prefixo de commit |
|----------|--------------|-------------------|
| `claude` | Claude Code  | `[claude]`        |
| `codex`  | OpenAI Codex | `[codex]`         |

O usuário decide quem implementa cada tarefa. **O outro agente revisa.** Ninguém aprova o próprio trabalho.

1. **Um agente por vez.** No início da sessão, se `git status` mostrar alterações não commitadas que não são suas, pare e pergunte ao usuário.
2. **Toda entrega termina em commit**, com prefixo e tipo. Exemplo: `[codex] fix: corrige leitura da temperatura da CPU`. Tipos: `feat`, `fix`, `refactor`, `docs`, `build`, `test`, `chore`, `revert`.
3. **Histórico é intocável.** Proibido `commit --amend`, `rebase`, `reset --hard` ou `push --force` em commits já entregues. Erro se corrige com commit novo ou `git revert`.
4. **Push só quando o usuário pedir.** O remoto `origin` é o repositório público do usuário no GitHub (D-030). Não crie outros remotos nem mude a visibilidade do repositório.
   - O histórico anterior a D-030 fica só nesta máquina, em `refs/historico-privado/`, com os drivers que não podem ser publicados. Nunca o envie: nada de `git push --mirror`, nem de citar esses commits num push.
5. **Commits pequenos**, um assunto por commit. Não reformate arquivos que você não precisava mudar: ruído no diff esconde erros na revisão.
6. **Escopo fechado.** Problema fora da tarefa vai para a seção "Achados" de `colab/TAREFAS.md`. Não corrija de carona.
7. **Honestidade.** Só diga que funciona o que você executou. Se não conseguiu compilar ou testar, escreva isso no HANDOFF.

### 4.1 Os dois ao mesmo tempo (git worktree, D-026)

Os dois agentes podem trabalhar ao mesmo tempo, cada um no seu git worktree. No fim, o trabalho dos dois é unificado na `main` e cada um revisa o do outro. As regras gerais e o script `wt.ps1` estão em `C:\Users\Victor\.codex\AGENTS.md`; o Claude Code as recebe pelo `C:\Users\Victor\.claude\CLAUDE.md`. Aqui no InfoPC:

- **Onde.** O worktree de cada agente fica em `C:\Users\Victor\.worktrees\InfoPC\<agente>`, na branch `agente/<agente>`.
  - Ele dura só a tarefa. O `wt.ps1 concluir` unifica o trabalho na `main`, por avanço rápido, e apaga a cópia, porque cada cópia ocupa espaço (`bin/` e `obj/` do build).
  - A pasta do projeto fica em `main` e só recebe a unificação.
  - Nunca crie worktree dentro da pasta do projeto: o `InfoPC.csproj` compila todo `.cs` abaixo dele.
  - Antes do `concluir`, encerre o servidor de build do .NET (`dotnet build-server shutdown`), que pode manter a pasta em uso.
- **Regra 1 por worktree.** "Alterações não commitadas que não são suas" se confere no seu worktree.
  - Alteração não commitada na pasta principal é de quem trabalhou fora do worktree. Não a inclua nem mexa nela, e avise o usuário.
- **Compilar e testar** no seu worktree, com os comandos de sempre (`docs/ARQUITETURA.md`).
- **Arquivos de coordenação.** HANDOFF, LOG e TAREFAS vão na sua branch, junto com a entrega. Se o outro agente integrou antes, o `wt.ps1 atualizar` junta tudo:
  - `colab/LOG.md` mescla sozinho (`merge=union` no `.gitattributes`). Confira se as entradas ficaram em ordem de data.
  - `colab/HANDOFF.md`: mantenha as duas entregas que aguardam revisão, a mais nova em cima, e o "Último commit revisado" da revisão mais recente.
  - `colab/TAREFAS.md`: mantenha as marcações dos dois.
- **Revisão.** Depois da unificação, cada agente revisa a entrega do outro na `main`, pelo ciclo da seção 5.
  - O intervalo continua começando no "Último commit revisado".
  - Revise só os commits do outro agente e pule os de merge: `git log --no-merges --oneline <ultimo>..main`.
  - O relatório é uma tarefa como outra qualquer: worktree, commit, `concluir`.
- **Continua um por vez:**
  - testes que alteram o sistema (registro, drivers, configurações do Windows), que valem para as duas cópias;
  - o push.

## 5. Ciclo de trabalho

### Início de sessão

1. Leia `colab/HANDOFF.md`, `colab/TAREFAS.md` e, quando tiver conteúdo, `docs/ARQUITETURA.md`.
2. Rode `git status` e `git log --oneline -15`.
3. Se houver commits do **outro** agente depois do "Último commit revisado" do HANDOFF, avise o usuário e, salvo instrução contrária, revise antes de qualquer trabalho novo.
   - Se esses commits forem só a revisão do **seu** trabalho (`docs(revisao)` / `fix(revisao)`), não escreva um novo relatório: leia a revisão, confira as correções triviais, trate os achados e atualize o "Último commit revisado".

### Fim de entrega (autor)

1. Compile e rode os testes (comandos em `docs/ARQUITETURA.md`). Anote o comando e o resultado.
2. Sobrescreva `colab/HANDOFF.md`: o que mudou, por quê, como verificar, riscos e onde o revisor deve olhar com mais atenção.
3. Acrescente uma entrada no fim de `colab/LOG.md`.
4. Atualize `colab/TAREFAS.md`.
5. Faça o commit (código + arquivos de `colab/`).

### Revisão cruzada (revisor)

Gatilho: o usuário diz "revise a última entrega", ou o passo 3 do início de sessão.

1. **Intervalo:** tudo depois do "Último commit revisado" do HANDOFF.
   `git log --oneline <ultimo>..HEAD` e `git diff <ultimo>..HEAD`.
   Se o campo estiver vazio, revise todo o histórico (`git log --oneline` e `git show <hash>` de cada commit).
2. **Leia o HANDOFF do autor, mas não confie nele:** compile e teste você mesmo.
3. **Verifique no mínimo:** a tarefa foi cumprida; regressões; requisitos da seção 2; licença e créditos; segurança (entrada de dados, execução de comandos, elevação); restos de depuração; escopo extra; nome antigo remanescente (depois da renomeação).
4. **Relatório** em `colab/revisoes/AAAA-MM-DD-<autor>-<assunto>.md`, a partir de `colab/revisoes/_MODELO.md`.
5. **Veredito:** `APROVADO`, `APROVADO COM RESSALVAS` ou `REPROVADO`.
6. **Correções pelo revisor:** só as triviais (erro de digitação, uma linha óbvia), em commit separado `[<revisor>] fix(revisao): ...`. O resto vai para o relatório e o autor corrige. `git revert` só com aprovação do usuário.
7. **Feche a revisão:** atualize no HANDOFF o "Último commit revisado" (hash do último commit que você revisou) e o veredito. Commit: `[<revisor>] docs(revisao): <assunto>`.

### Divergência

O autor responde aos achados na seção "Resposta do autor" do próprio relatório. **Uma rodada só.** Se a divergência continuar, o usuário decide e a decisão vai para `docs/DECISOES.md`.

### Proposta dupla (decisões importantes)

Quando o usuário pedir "proposta dupla: <tema>", cada agente escreve a sua em `colab/propostas/<tema>-<agente>.md` **sem ler a do outro antes**. Depois um deles, ou o usuário, compara as duas. A escolha vai para `docs/DECISOES.md`.

## 6. Convenções

- Documentação e conversa com o usuário: **português do Brasil**.
- Código: siga o estilo do projeto base. Nele, identificadores ficam em inglês e comentários e textos da interface em português.
- Arquivos novos: nomes sem espaços nem acentos, texto em UTF-8.
- Scripts `.ps1`: só caracteres ASCII. O PowerShell 5.1 lê UTF-8 sem BOM como ANSI e estraga acentos.
- Fim de linha é controlado pelo `.gitattributes`. Não converta arquivos manualmente.
- Ambiente: Windows 11. Shell padrão: PowerShell 5.1 (não tem `&&` nem `||`). Git Bash disponível. .NET SDK 10 (compila o alvo .NET 8).
- Edição Linux: não há Linux na máquina de desenvolvimento. A verificação em Linux real é o CI (`.github/workflows/linux.yml`, D-022); no Windows rodam os testes e a interface em render headless. Scripts `.sh` ficam com fim de linha LF (`.gitattributes`) e o código de `linux/` não pode depender de WPF, WMI nem registro.
- Mensagem de commit com várias linhas no PowerShell: grave num arquivo temporário e use `git commit -F <arquivo>`. Passar uma here-string para `git commit -F -` não funciona.
- `sed -i` do Git Bash troca CRLF por LF na cópia de trabalho. Use `sed -b -i`, que preserva o fim de linha. Nunca rode `sed` em `Drivers/`.

## 7. Mapa dos arquivos de coordenação

| Arquivo                             | Para quê                                                                 |
|-------------------------------------|--------------------------------------------------------------------------|
| `colab/COMO-USAR.md`                | Guia para o usuário: como conduzir os dois agentes.                      |
| `colab/HANDOFF.md`                  | Estado atual: última entrega e último commit revisado. Sobrescrito a cada entrega. |
| `colab/LOG.md`                      | Histórico de entregas e revisões. Só acrescentar.                        |
| `colab/TAREFAS.md`                  | Fila de tarefas e achados fora de escopo.                                |
| `colab/revisoes/`                   | Relatórios de revisão cruzada.                                           |
| `colab/propostas/`                  | Propostas duplas (criada quando necessário).                             |
| `docs/UPSTREAM.md`                  | Origem do código, licença e plano de renomeação.                         |
| `docs/LINUX.md`                     | Edição Linux: instalação, mapa de recursos Windows → Linux, privilégios, CI. |
| `linux/`                            | Edição Linux (Core, app Avalonia, testes, instalador).                   |
| `docs/ARQUITETURA.md`               | Como o código funciona; comandos de build, teste e empacotamento.        |
| `docs/DECISOES.md`                  | Decisões tomadas e por quê.                                              |
| `scripts/verificar-nome-antigo.ps1` | Acha restos do nome antigo depois da renomeação.                         |
| `scripts/gerar-logo-provisorio.ps1` | Gera o logo e o ícone provisórios em `Assets/` (D-008, D-016).           |
