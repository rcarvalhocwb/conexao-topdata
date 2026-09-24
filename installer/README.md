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
| `Edge.Worker.X86` | **Não** | Confere os pré-requisitos e **sai com código 2**: as assinaturas P/Invoke da `EasyInner.dll` não foram obtidas |

Ou seja: o caminho painel → serviço → supervisão funciona ponta a ponta. O que não existe é
a última perna, a que fala com a catraca. Instalado hoje, o supervisor sobe, tenta o worker,
ele sai na largada, e depois de 5 tentativas o grupo vai para quarentena com o motivo escrito
na tela — que é o comportamento correto, e não um sistema operando.

**Nenhuma catraca foi acionada por este sistema até hoje.**

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
