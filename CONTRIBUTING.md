# Como trabalhamos neste repositório

Várias pessoas trabalham ao mesmo tempo para terminar o Rayzer XAcess. Para ninguém sobrepor o
trabalho do outro e todos irem para o mesmo lugar, vale o fluxo abaixo. A `main` está protegida:
nada entra nela sem PR, CI verde, a **Revisão do Claude** aprovada e a aprovação do dono.

## O objetivo comum

- **O que falta fazer** está nas [issues](../../issues) com o modelo **Tarefa**. Cada uma diz o
  objetivo, a **área** (pastas e arquivos), o que fica fora, o critério de pronto e do que depende.
- **As decisões de produto** estão em `docs/` (as ADRs em `docs/ADR/`; o cadastro de pessoas em
  `docs/43-cadastro-de-pessoas-e-credenciais.md`). Tarefa que contradiz uma decisão volta para
  discussão na issue antes de virar código.
- **Ninguém inventa requisito:** dúvida de comportamento vai como comentário na issue, e a resposta
  fica escrita lá.

## O fluxo de uma tarefa

1. **Assumir.** Escolha uma issue sem responsável, comente "assumo" e peça para ser atribuído.
   Uma tarefa em andamento por pessoa. Antes, confira se nenhuma issue em andamento usa a mesma
   área; se usar, combine na issue.
2. **Branch.** A partir da `main` atualizada: `tarefa/<número>-<resumo>` (ex.: `tarefa/12-visitantes`).
3. **Trabalho.** Mude só a área da issue. Se precisar sair dela, escreva por quê no PR.
4. **Atualizar.** Antes de abrir o PR, traga a `main` para a sua branch (`git merge origin/main`)
   e resolva os conflitos você mesmo. Não reescreva histórico de branch que outra pessoa usa.
5. **PR.** Para a `main`, com o modelo preenchido e `Fecha #<número>`. Rascunho enquanto não terminou.
6. **Validação.** O CI roda os testes (Linux e Windows), o instalador e o autoteste das telas; a
   **Revisão do Claude** confere o PR contra a issue e estas regras e comenta no PR. Corrija o que
   for apontado e empurre de novo: os dois rodam a cada push.
7. **Entrada.** Com tudo verde e a aprovação do dono, o PR entra. A issue fecha sozinha.

## Pontos de encontro (onde as tarefas mais se cruzam)

Estes arquivos são mexidos por quase toda tarefa. As regras evitam conflito e perda de trabalho:

| Arquivo | Regra |
|---|---|
| `src/Access.Infrastructure.SQLite/Migrations/NNN_*.sql` | Nunca altere uma migração que já está na `main`; crie a próxima. O número é reservado na issue ("usa a migração 024"); dois PRs com o mesmo número: o segundo renumera. |
| `src/Contracts/Protos/edge_control.proto` | Só acrescente: RPCs no fim do bloco do assunto, mensagens no fim do arquivo, números de campo novos. Nunca renumere nem apague campo. O teste de contrato reprova código em claro em resposta. |
| `src/Access.Domain/Usuarios/Permissoes.cs` e `InterceptadorDeSessao.cs` | Toda RPC nova entra no mapa de permissões no mesmo PR (o teste reprova RPC sem permissão). Permissão nova também vai em `CodigosDePermissao` (painel) e numa migração para os papéis prontos. |
| `src/Desktop.ViewModels/Telas.cs` (menu), `App.xaml`, `Conversores.cs` | Tela nova: uma linha em cada, e a entrada em `LigacoesDasTelasTests`. |
| `docs/43-*.md` e `installer/README.md` | Atualize só a linha ou seção da sua tarefa. |

## Regras do projeto (a revisão reprova se faltar)

- **SDK da Topdata nunca entra no repositório** (é proprietário e o repositório é público). Nem
  DLL, nem exemplo, nem manual. O CI reprova MSI com a `EasyInner.dll`.
- **Sem segredo e sem dado pessoal real**: chaves, tokens e senhas ficam fora; planilhas e testes
  usam só exemplos fictícios.
- **Tudo com teste.** Comportamento novo tem teste; teste nunca é pulado, apagado ou enfraquecido
  para passar. "Falha intermitente" não é causa: ache a causa.
- **Avisos são erros** (`TreatWarningsAsErrors` e analisadores): o build local tem que sair limpo.
- **Português** em tela, mensagem ao operador, documentação e mensagem de commit.
- **Nada falso:** documentação e tela dizem o que o sistema faz hoje; o que não existe aparece
  como não disponível, com o motivo.

## Rodar localmente

```bash
dotnet build ConexaoTopdata.slnx
dotnet test tests/Unit
dotnet test tests/Integration
dotnet test tests/Contract
```

O painel (WPF) e o instalador só rodam no Windows; os testes acima rodam também no Linux.

## Gerar o instalador à mão

- **Pelo GitHub (sem máquina Windows):** aba **Actions → CI → Run workflow**, escolha a branch e
  rode. Com tudo verde, sai uma pré-release **"Instalador de teste 0.1.N"** em **Releases**, com o
  `RayzerXAcess-Setup-0.1.N.zip`.
- **Numa máquina Windows** (com o .NET SDK do `global.json`):

  ```powershell
  .\installer\gerar-setup.ps1 -Versao 0.1.900 -InstalarWix
  # com o SDK da Topdata embarcado (só onde o SDK está instalado):
  .\installer\gerar-setup.ps1 -Versao 1.0.0 -SdkDir "C:\Topdata\SDK Inner Acesso" -Producao
  ```

  Gera `RayzerXAcess-<versão>.msi` e `RayzerXAcess-Setup.exe` na raiz do repositório. É o mesmo
  script que o CI usa. O instalador gerado com o SDK **não** pode ser publicado no repositório público.

## Para o dono do repositório (configuração única)

1. **Colaboradores:** Settings → Collaborators → **Add people**, com o usuário do GitHub de cada
   um, papel **Write**.
2. **Chave da revisão:** Settings → Secrets and variables → Actions → **New repository secret**,
   nome `ANTHROPIC_API_KEY`, valor gerado em console.anthropic.com. Sem ela, o check **Revisão do
   Claude** falha de propósito.
3. **Proteção da `main`:** Settings → Rules → Rulesets → **New branch ruleset**:
   - Enforcement: **Active**; Target branches: **Default branch**;
   - Bypass list: **Repository admin**, modo **For pull requests only** (o dono pode aprovar os
     próprios PRs, mas nem ele empurra direto na `main`);
   - marque **Restrict deletions** e **Block force pushes**;
   - **Require a pull request before merging**: 1 aprovação, **Require review from Code Owners**,
     **Dismiss stale pull request approvals when new commits are pushed**;
   - **Require status checks to pass**, com **Require branches to be up to date**, e os checks:
     `Build e testes (Linux)`, `Build e testes (Windows)`, `Instalador MSI (Windows)`,
     `Varredura de dado sensível`, `Ensaio de soak`, `Rayzer UI (web)`, `Revisão do Claude`.
4. **Segurança da revisão:** a revisão roda com o arquivo de workflow da própria branch do PR. Um
   colaborador poderia alterá-lo; por isso o CODEOWNERS exige a sua aprovação em todo PR, e PR
   que mexe em `.github/` merece leitura sua linha a linha.
