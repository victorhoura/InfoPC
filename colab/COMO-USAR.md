# Como trabalhar com o Claude e o Codex no InfoPC

Os dois agentes seguem as mesmas regras, em `AGENTS.md`. Um faz, o outro revisa.

1. **Peça uma tarefa a um deles** (o autor). Ele implementa, testa, faz commit com `[claude]` ou `[codex]` e atualiza `colab/HANDOFF.md`.
2. **Abra o outro e peça: "revise a última entrega".** Ele compila, testa e escreve um relatório com veredito em `colab/revisoes/`.
3. **Se houver achados,** volte ao autor: "corrija os achados da última revisão".
4. **Se os dois discordarem,** você decide. A decisão fica em `docs/DECISOES.md`.

## Frases úteis

| Você diz                                 | O agente faz                                                        |
|------------------------------------------|---------------------------------------------------------------------|
| "Continue a próxima tarefa."             | Lê HANDOFF e TAREFAS e segue a fila.                                |
| "Revise a última entrega."               | Revisão cruzada completa, com relatório e veredito.                 |
| "Corrija os achados da última revisão."  | O autor trata cada achado.                                          |
| "Proposta dupla: <tema>"                 | Diga aos dois. Cada um propõe sem ver o outro; depois compare.      |
| "Envie para o GitHub."                   | `git push` para o repositório público `victorhoura/InfoPC`. Sem esse pedido, os agentes só fazem commit local. |

## Para evitar problemas

- Use **um agente por vez** nesta pasta. Espere o commit de um antes de abrir o outro. A exceção é trabalhar com os dois ao mesmo tempo, cada um no seu worktree (seção abaixo).
- Alterne quem implementa de vez em quando, para que os dois revisem o trabalho um do outro.

## Os dois ao mesmo tempo (git worktree)

Desde 28/10/2026 (D-026), você pode abrir o Claude e o Codex juntos neste projeto.

- Cada um trabalha na própria cópia: `C:\Users\Victor\.worktrees\InfoPC\claude` e `C:\Users\Victor\.worktrees\InfoPC\codex`, cada uma com a sua branch.
- Ao terminar e testar, cada um unifica o trabalho na pasta do projeto e apaga a própria cópia, para não ocupar espaço.
- Depois, cada um revisa o que o outro entregou.
- Você não precisa fazer nada de git.

Dicas:

- **Dê tarefas em áreas diferentes,** para não haver conflito na hora de juntar. Se houver, quem chegou depois resolve na própria cópia.
- **Para ver o que cada um está fazendo,** peça "mostre o estado dos worktrees".
- **A revisão continua igual:** depois que um entregar, peça ao outro "revise a última entrega". Se os dois entregaram, peça a revisão aos dois.
- **Se sobrar uma cópia** (um agente parou no meio), ela aparece no "estado dos worktrees". Peça ao dono dela "conclua o seu worktree".
- **Como abrir cada um:**
  - Codex: abra na pasta do projeto, no modo Local ou no modo Worktree do app.
  - Claude Code: abra normalmente na pasta do projeto. No app desktop, deixe o botão Worktree desligado, porque ele cria a cópia dentro da pasta do projeto.

## Estrutura

```
InfoPC/
├── AGENTS.md                  regras comuns aos dois agentes
├── CLAUDE.md                  ponte para o Claude Code (importa AGENTS.md)
├── README.md                  README do produto
├── LICENSE                    licença MIT do projeto original (manter)
├── *.cs, *.xaml, *.csproj     código do app (C# / WPF)
├── Assets/  Drivers/          imagens e drivers seriais assinados
├── colab/                     coordenação entre os agentes
│   ├── COMO-USAR.md           este guia
│   ├── HANDOFF.md             estado atual e último commit revisado
│   ├── LOG.md                 histórico de entregas
│   ├── TAREFAS.md             fila de tarefas
│   └── revisoes/              relatórios de revisão (_MODELO.md)
├── docs/
│   ├── UPSTREAM.md            origem, licença e plano de renomeação
│   ├── ARQUITETURA.md         mapa do código e comandos
│   └── DECISOES.md            decisões tomadas
└── scripts/
    └── verificar-nome-antigo.ps1
```
