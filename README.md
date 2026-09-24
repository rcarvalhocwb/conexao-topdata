# Conexão Topdata — Plataforma de Gerenciamento de Catracas, Controladores e Coletores

> **Status atual: Fase 1 concluída — fundação executável, verde contra o simulador.**
> **Nenhuma catraca real foi acionada ainda.** Todo o caminho leitura → decisão →
> liberação → persistência roda ponta a ponta, mas contra o simulador: a ligação com a
> `EasyInner.dll` está deliberadamente **não implementada** (as assinaturas P/Invoke não
> foram deduzidas — ver [`src/Topdata.EasyInner.Adapter/VinculacaoNativaPendente.cs`](src/Topdata.EasyInner.Adapter/VinculacaoNativaPendente.cs)).
> O próximo marco é de bancada, não de código: o ensaio **HIL-STACK-01**
> ([`docs/12`](docs/12-decisao-de-stack.md)).

| Fase | Situação |
|---|---|
| 0 — Descoberta | Concluída |
| 1 — Fundação executável | **Concluída** — 200 testes, 0 falhas; CI em Linux e Windows |
| 2 — Operação Inner on-line/off-line | Bloqueada pelo ensaio HIL-STACK-01 e pelo `EasyInner.cs` |

## Procedência das informações

Este projeto opera sob uma regra dura: **não inventar funções, enums, códigos de retorno,
capacidades ou comportamentos do SDK.**

**Atualização de 24/09/2026 — a documentação oficial foi obtida.** O *Manual de Integração
SDK Inner Acesso* (PDF publicado pela Topdata) e as FAQs do portal do integrador foram
lidos na íntegra. A matriz de compatibilidade deixou de ser uma lacuna: **51 das 58
funções mapeadas têm assinatura, parâmetros e retornos de fonte primária.**

**O SDK do Leitor Facial também foi mapeado** — protocolo WebSocket, Web API HTTP e os
49 comandos das duas superfícies ([`docs/13`](docs/13-sdk-facial.md)).

Continua **não disponível** o pacote de **exemplos de código** (download separado do
portal), que contém o `EasyInner.cs` e o enum `Enumeradores.Retorno`. É o que fecha as
lacunas restantes — ver [`docs/11`](docs/11-capacidades-do-sdk.md), seção 5.
O catálogo com todos os links oficiais e os scripts de download estão em
[`vendor/topdata/`](vendor/topdata/); os binários **não são versionados**, por serem
software proprietário da Topdata e este repositório ser público.

| Selo | Significado | Situação |
|---|---|---|
| `FONTE_PRIMARIA` | Verificado contra manual oficial ou FAQ da Topdata | **51 funções EasyInner + 17 origens + 49 comandos faciais** |
| `FONTE_PRIMARIA_PARCIAL` | A função é nomeada pelo manual, sem assinatura completa | 6 funções |
| `FONTE_PRIMARIA_AMBIGUA` | O manual se contradiz ou o PDF saiu desalinhado | 1 função + tipos de bilhete |
| `A_CONFIRMAR_COM_TOPDATA` | Ausente da documentação; exige fabricante ou bancada | origens 11, 14–17, 19 |
| `DECISAO_ARQUITETURAL` | Decisão nossa, independente do SDK | os 21 ADRs |

Tudo que não estiver em `FONTE_PRIMARIA` nasce **desabilitado por padrão** e só é
habilitado após ensaio de bancada com relatório assinado por modelo e firmware.

## Índice dos documentos

| # | Documento | Conteúdo |
|---|---|---|
| 00 | [Entendimento e escopo](docs/00-entendimento-e-escopo.md) | O que foi entendido, o que está dentro e fora do escopo, restrições inegociáveis |
| 01 | [Perguntas críticas](docs/01-perguntas-criticas.md) | Bloqueantes e não bloqueantes, com impacto de cada resposta |
| 02 | [Matriz de compatibilidade](docs/02-matriz-compatibilidade.md) | Funções, modelos, origens de evento, lacunas |
| 03 | [Arquitetura](docs/03-arquitetura.md) | Componentes, processos, limites, fluxo de dados, níveis de degradação |
| — | [ADRs](docs/ADR/) | 21 decisões arquiteturais registradas |
| 04 | [Workflow CollectCardThenEnter](docs/04-workflow-collect-card-then-enter.md) | Fluxo do coletor/urna liberando ENTRADA |
| 05 | [Modelo de dados](docs/05-modelo-de-dados.md) | Esquema local, outbox, auditoria, idempotência |
| 06 | [Testes e dimensionamento](docs/06-plano-de-testes-e-dimensionamento.md) | Estratégia de testes, simulador, calculadora de capacidade |
| 07 | [Backlog por fases](docs/07-backlog-por-fases.md) | Fases 0–5 com critérios de pronto |
| 08 | [Riscos e validações](docs/08-riscos-e-validacoes-topdata.md) | Registro de riscos e pauta para a Topdata |
| 09 | [Plano de bancada](docs/09-plano-de-bancada.md) | Protocolo de ensaio por modelo/firmware |
| 10 | [Interface](docs/10-interface.md) | Telas obrigatórias, dois níveis de linguagem, acessibilidade |
| 11 | [Capacidades do SDK](docs/11-capacidades-do-sdk.md) | **Inventário completo: 58 funções, o que dá para construir, lacunas** |
| 12 | [Decisão de stack](docs/12-decisao-de-stack.md) | **Qual linguagem, por quê, e o plano B do protocolo sob NDA** |
| 13 | [SDK do Leitor Facial](docs/13-sdk-facial.md) | **WebSocket + Web API, 49 comandos, Catracas Easy** |
| — | [Acervo Topdata](vendor/topdata/) | Catálogo oficial de downloads e scripts de importação |
| — | [Instalador de desenvolvimento](installer/) | Publicação, conferência de pré-requisitos e token de sessão |
| — | [Glossário](docs/GLOSSARIO.md) | Termo técnico → linguagem do operador |
| — | [Runbooks](docs/runbooks/) | Template e índice dos procedimentos de operação |

## Próximo passo

1. Rodar [`vendor/topdata/fetch-sdk.ps1`](vendor/topdata/) numa máquina Windows e abrir o
   `EasyInner.cs` do exemplo C# — fecha as lacunas restantes de assinatura e os códigos
   de retorno.
2. Responder **B2** (inventário do parque), **B4** (fail-safe × fail-secure), **B7**
   (simultaneidade) e **B8** (padrão de cartão) em
   [`docs/01-perguntas-criticas.md`](docs/01-perguntas-criticas.md).
3. Solicitar à Topdata o **NDA do protocolo de baixo nível**
   ([ADR-0021](docs/ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md)) — prazo de
   fabricante é longo, e não bloqueia a Fase 1.
4. Executar o ensaio **HIL-STACK-01** numa máquina Windows: publicar com
   [`installer/publicar.ps1`](installer/) e confirmar que um processo .NET 10 `win-x86`
   carrega a `EasyInner.dll`. É o que decide se o worker continua em .NET 10 ou volta
   para .NET Framework 4.8 atrás do mesmo IPC ([`docs/12`](docs/12-decisao-de-stack.md)).
