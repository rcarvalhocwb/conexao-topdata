# Conexão Topdata — Plataforma de Gerenciamento de Catracas, Controladores e Coletores

> **Status atual: Fase 0 — Descoberta. Nenhum código de produção foi escrito.**
> Este repositório contém, neste momento, apenas os artefatos de projeto da Fase 0.
> A fundação executável (Fase 1) só será iniciada após aprovação explícita e
> após o fechamento das lacunas listadas em [`docs/01-perguntas-criticas.md`](docs/01-perguntas-criticas.md).

## Aviso de procedência — leia antes de qualquer coisa

Este projeto opera sob uma regra dura: **não inventar funções, enums, códigos de retorno,
capacidades ou comportamentos do SDK.**

No momento da elaboração destes documentos:

- **os arquivos oficiais do SDK não estavam presentes no repositório nem no contexto**
  (Manual de Integração SDK Inner Acesso, SDK EasyInner 6.0.2.0, exemplos C#,
  `EasyInner.cs`, enumerações, SDK do Leitor Facial, manuais de modelo/firmware);
- **o acesso de rede aos domínios `integrador.topdata.com.br`, `suporte.topdata.com.br`
  e `topdata.com.br` está bloqueado** pela política de egresso deste ambiente, portanto
  nenhuma afirmação pôde ser verificada contra a fonte primária.

Consequência: **toda** assertiva técnica sobre o SDK neste repositório carrega um selo de
procedência explícito. Nenhuma delas deve ser tratada como verdade até ser verificada.

| Selo | Significado |
|---|---|
| `FONTE_PRIMARIA` | Verificado contra documento/wrapper oficial. **Nenhuma linha possui este selo hoje.** |
| `BRIEFING_NAO_VERIFICADO` | Afirmado no briefing do cliente; plausível, porém não conferido contra a fonte. |
| `A_VERIFICAR_NO_WRAPPER` | Depende de leitura do `EasyInner.cs` / headers / manual. |
| `A_CONFIRMAR_COM_TOPDATA` | Ambíguo ou ausente na documentação; exige resposta do fabricante ou ensaio de bancada. |
| `DECISAO_ARQUITETURAL` | Decisão nossa, independente do SDK. Não requer confirmação do fabricante. |

Tudo marcado como não verificado nasce **desabilitado por padrão** no produto e só é
habilitado após ensaio de bancada com relatório assinado por modelo e firmware.

## Índice dos documentos da Fase 0

| # | Documento | Conteúdo |
|---|---|---|
| 00 | [Entendimento e escopo](docs/00-entendimento-e-escopo.md) | O que foi entendido, o que está dentro e fora do escopo, restrições inegociáveis |
| 01 | [Perguntas críticas](docs/01-perguntas-criticas.md) | Bloqueantes e não bloqueantes, com impacto de cada resposta |
| 02 | [Matriz de compatibilidade](docs/02-matriz-compatibilidade.md) | Funções, modelos, origens de evento, lacunas |
| 03 | [Arquitetura](docs/03-arquitetura.md) | Componentes, processos, limites, fluxo de dados, níveis de degradação |
| — | [ADRs](docs/ADR/) | 16 decisões arquiteturais registradas |
| 04 | [Workflow CollectCardThenEnter](docs/04-workflow-collect-card-then-enter.md) | Fluxo do coletor/urna liberando ENTRADA |
| 05 | [Modelo de dados](docs/05-modelo-de-dados.md) | Esquema local, outbox, auditoria, idempotência |
| 06 | [Testes e dimensionamento](docs/06-plano-de-testes-e-dimensionamento.md) | Estratégia de testes, simulador, calculadora de capacidade |
| 07 | [Backlog por fases](docs/07-backlog-por-fases.md) | Fases 0–5 com critérios de pronto |
| 08 | [Riscos e validações](docs/08-riscos-e-validacoes-topdata.md) | Registro de riscos e pauta para a Topdata |
| 09 | [Plano de bancada](docs/09-plano-de-bancada.md) | Protocolo de ensaio por modelo/firmware |
| 10 | [Interface](docs/10-interface.md) | Telas obrigatórias, dois níveis de linguagem, acessibilidade |
| — | [Glossário](docs/GLOSSARIO.md) | Termo técnico → linguagem do operador |
| — | [Runbooks](docs/runbooks/) | Template e índice dos procedimentos de operação |

## Próximo passo

Responder às perguntas **B1–B9** de [`docs/01-perguntas-criticas.md`](docs/01-perguntas-criticas.md)
e disponibilizar os SDKs no repositório (ver `docs/09-plano-de-bancada.md`, seção "Entrada necessária").
