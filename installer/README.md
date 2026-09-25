# Instalação de desenvolvimento

Sobe o ambiente completo numa máquina Windows de bancada. **Não é o instalador de
produção** — esse vem na Fase 5, com pacote assinado, rollback e verificação de
integridade das DLLs.

## O que é instalado

| Componente | Arquitetura | Papel |
|---|---|---|
| `Edge.Worker.X86` | **x86** | Único que carrega a `EasyInner.dll` |
| `Edge.Supervisor` | x64 | Supervisiona os workers e atende o IPC |
| `Desktop.App` | x64 | Painel do operador |

## Passos

```powershell
# 1. Publica os três componentes em artifacts\
.\installer\publicar.ps1

# 2. Confere os pré-requisitos da máquina
.\installer\verificar-ambiente.ps1

# 3. Gera o token de sessão
.\installer\instalar-dev.ps1

# 4. Descreve os grupos
copy installer\workers.exemplo.json artifacts\Edge.Supervisor\workers.json
# edite: nome, porta e equipamentos de cada grupo
```

O passo 3 gera um **token de sessão** e o grava em
`%LOCALAPPDATA%\ConexaoTopdata\token`, com permissão só para o usuário atual. O serviço
recusa a conexão sem ele, e recusa **subir** sem ele — a ACL do named pipe protege por
identidade de usuário, não por processo, então qualquer coisa rodando na mesma conta
alcançaria o serviço.

O serviço também recusa subir com configuração inválida: porta repetida entre grupos,
equipamento em dois grupos ou executável inexistente. Reclamar na partida é muito melhor
que descobrir com a fila formada.

## O que roda hoje, e o que não roda

Sendo direto, porque a diferença decide se vale agendar bancada:

| Componente | Sobe? | O que acontece |
|---|---|---|
| `Edge.Supervisor` | **Sim** | Lê `workers.json`, sobe os workers com a base local, supervisiona com reinício e quarentena, sincroniza com a nuvem e atende o painel pelo IPC (ADR-0024) |
| `Desktop.App` | **Sim** | Painel do evento com oito telas, lendo o serviço |
| `Edge.Worker.X86` | **Tenta** | Confere os pré-requisitos e chama a DLL de verdade. É aqui que o ensaio **HIL-STACK-01** responde: código 0 se a porta abriu, 2 se a DLL não carregou ou devolveu GPF |

O caminho painel → serviço → supervisão funciona ponta a ponta, e desde 24/09 o worker tem
adapter real: as assinaturas vieram do SDK 6.0.2.0. O que falta é a primeira conversa com
hardware.

Rodar o worker sozinho **é** o ensaio HIL-STACK-01:

```powershell
$env:EDGE_TOKEN = Get-Content "$env:LOCALAPPDATA\ConexaoTopdata\token"
.\artifacts\Edge.Worker.X86\Edge.Worker.X86.exe --porta 3570
```

| Saída | O que significa |
|---|---|
| código 0, "Porta aberta" | A DLL carregou num processo .NET 10 de 32 bits. O plano B some |
| código 2, retorno 8 (GPF) | Ambiente: rode `verificar-ambiente.ps1` |
| código 2, DLL não encontrada | Registre as DLLs, ou mova só este hospedeiro para .NET Framework 4.8 (docs/12) |

**Nenhuma catraca foi acionada por este sistema até hoje.**

## Instalador MSI — o caminho de produção

A cada commit, a integração contínua constrói o **MSI** e publica como artefato
`instalador-msi` (e o `pacote-da-bancada`, que é outra coisa: ver docs/21).

### Antes de instalar

No computador do evento, instale:

| O quê | Para quê |
|---|---|
| SDK Inner Acesso da Topdata | traz a `EasyInner.dll`, que **não** vem com este instalador |
| .NET Framework 3.5 (recursos do Windows) | sem ele a DLL devolve retorno 8 |
| .NET 10 Runtime **x86** | o programa das catracas é de 32 bits por causa da DLL |
| ASP.NET Core Runtime 10 **x64** | o serviço |
| .NET Desktop Runtime 10 **x64** | o painel e o assistente |

O assistente confere cada um desses itens na primeira tela e diz o que falta.

### Instalar

Duplo clique no MSI, ou:

```powershell
msiexec /i ConexaoTopdata-0.1.42.msi /qn /l*v instalacao.log
```

Ele instala em `C:\Program Files\Conexao Topdata` e:

- registra o serviço **ConexaoTopdataEdge**, com partida **automática** — mas **não** o
  inicia durante a instalação: se faltasse um runtime, a partida falharia e o Windows
  Installer desfaria tudo;
- cria no menu Iniciar o **Painel do evento** e o **Assistente de configuração**;
- desinstala limpo, parando o serviço antes de remover.

### Configurar — Assistente de configuração

Abra pelo menu Iniciar (ele pede permissão de administrador):

1. **Ambiente** — o que o computador tem e o que falta.
2. **Catracas** — número do Inner e nome de cada uma, e a porta (3570). Em cada catraca,
   aponte o servidor para o IP deste computador e essa porta.
3. **Nuvem** — opcional: endereço, identificador deste computador, formato do cartão,
   intervalo de reuso, só na urna, e o segredo.
4. **Gravar** — confere tudo, grava e reinicia o serviço.

Onde fica cada coisa, em `%ProgramData%\ConexaoTopdata`:

| Arquivo | O que é |
|---|---|
| `workers.json` | a configuração que o assistente gravou |
| `acesso.db` | a base local: ingressos, cartões, tentativas, fila de envio |
| `token` | senha interna entre o painel e o serviço, gerada pelo serviço na primeira subida; só administradores escrevem, usuários leem |
| `segredos\nuvem.segredo` | o segredo da nuvem, cifrado pelo Windows (DPAPI) para esta máquina; só SYSTEM e administradores leem |
| `registros\` | registro diário do serviço e de cada programa de catraca, sem número de cartão |

Reabrir o assistente traz a configuração atual preenchida; o segredo nunca é mostrado, e
deixar o campo em branco mantém o que está no cofre.

### Usar — Painel do evento

Abre pelo menu Iniciar, sem senha: o token é lido de `%ProgramData%`. Recém-instalado e
sem configuração, o cabeçalho diz "Instalação ainda não configurada — abra o Assistente de
configuração".

## Imagens das telas

Numa máquina Windows, com o serviço rodando:

```powershell
& "C:\Program Files\Conexao Topdata\Painel\Desktop.App.exe" --capturar capturas
```

Fotografa as oito telas do painel com os dados reais do serviço e grava
`capturas\relatorio.txt`. Sem serviço, as telas saem com "sem resposta do serviço local".
Renderiza o **conteúdo** da janela, sem a barra de título, e **não funciona na integração
contínua**: o runner não tem sessão gráfica.

## Antes de encostar em hardware

Dois ensaios precisam ser feitos **antes** de conectar uma catraca de verdade:

1. **`HIL-STACK-01`** — um processo .NET moderno compilado `win-x86` consegue carregar a
   `EasyInner.dll`? A DLL exige .NET Framework 3.5, o que sugere assembly *mixed-mode*.
   Se falhar, só o worker vira .NET Framework 4.8, isolado atrás do IPC
   (ver `docs/12-decisao-de-stack.md`).
2. **Obter o `EasyInner.cs`** do pacote de exemplos, que traz as assinaturas P/Invoke
   reais. Elas **não foram deduzidas** de propósito — ver
   `src/Topdata.EasyInner.Adapter/VinculacaoNativaPendente.cs` e
   `vendor/topdata/README.md`.

Enquanto isso, o worker roda contra o simulador e toda a lógica já é exercitada.

## Portas

Cada worker escuta numa **porta TCP própria** (padrão 3570, depois 3571, 3572…), e a
catraca precisa apontar para a porta do worker que a gerencia. Realocar um equipamento
entre workers exige reconfigurar a catraca — não é operação de software.
Ver `docs/ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md`.

O IPC entre painel e serviço **não usa porta TCP**: é named pipe.
