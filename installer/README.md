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
| `Edge.Supervisor` | **Sim** | Lê `workers.json`, sobe os workers, supervisiona com reinício e quarentena, e atende o painel pelo IPC |
| `Desktop.App` | **Sim** | Conecta ao serviço e mostra o estado real |
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

## Instalador MSI

A cada commit, a integração contínua constrói um **MSI de verdade** e publica como artefato
`instalador-msi`. Baixe do run e instale com duplo clique, ou:

```powershell
msiexec /i ConexaoTopdata-0.1.42.msi /qn /l*v instalacao.log
```

O que ele faz:

- instala os três componentes em `C:\Program Files\Conexao Topdata`;
- registra o serviço **ConexaoTopdataEdge**, com partida **manual**;
- cria o atalho "Painel do evento" no menu Iniciar;
- desinstala limpo, parando o serviço antes de remover.

**Por que partida manual e não automática:** o worker ainda sai com código 2 por falta das
assinaturas da `EasyInner.dll`. Um serviço automático tentaria subir a cada boot, falharia
e encheria o log de eventos do Windows. Quando o adapter nativo existir, isso vira `auto`.

Antes de iniciar o serviço, ainda é preciso o `workers.json` e a variável `EDGE_TOKEN` —
ver as seções acima. O instalador **não** os cria: token gerado por instalador seria igual
em todas as máquinas.

## Imagens das telas

Numa máquina Windows, depois de publicar:

```powershell
.\artifacts\Desktop.App\Desktop.App.exe --capturar capturas
```

Renderiza cinco PNGs a 192 ppp — sem internet, normal, lista local, nenhuma catraca e
serviço caiu — pela árvore visual do próprio XAML, e grava `capturas\relatorio.txt`.
São as telas reais, não uma recriação.

Duas coisas para não se surpreender: renderiza o **conteúdo** da janela, sem a barra de
título (ela é desenhada pelo Windows, não pela aplicação); e **não funciona na integração
contínua** — o runner não tem sessão gráfica e o WPF trava sem mensagem. Foi tentado duas
vezes antes de desistir.

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
