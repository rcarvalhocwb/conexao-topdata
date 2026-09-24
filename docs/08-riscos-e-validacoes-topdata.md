# 08 — Riscos e validações

Para cada risco: detecção, impacto, comportamento em falha, alerta, recuperação, runbook e
teste. Severidade = probabilidade × impacto na operação de um evento.

## 1. Dependência e ambiente

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-01 | DLL não registrada / dependência ausente | **Alta** | Verificação no startup do worker (presença, versão, hash) | Worker recusa iniciar com mensagem **específica**, não "erro 8" | Instalador reinstala dependências | `REL-01` |
| R-02 | Arquitetura errada (x64 carregando DLL x86) | **Alta** | `BadImageFormatException` no startup | Falha explícita com instrução | Build correto; teste de smoke no instalador | `REL-02` |
| R-03 | "Erro 8" genérico | **Alta** | Retorno nativo mapeado | Diagnóstico com 4 causas prováveis e o que checar | Runbook `RB-01` | `HIL-ERR-01` |
| R-04 | Versões incompatíveis das DLLs | Média | Hash/versão comparados com o esperado | Quarentena do worker | Restaurar versão homologada | `REL-03` |
| R-05 | Antivírus bloqueando ou alterando a DLL | Média | Verificação de integridade no startup | Alerta nomeando o arquivo e o hash esperado | Exceção no antivírus (runbook) | `REL-04` |
| R-06 | Atualização do Windows / reinício inesperado | Média | Serviço com recuperação automática | Retomada pelo estado persistido | — | `CHAOS-KILL-01` |
| R-07 | Equipamento antigo (Linha 3) com comportamento próprio | Média | Capability discovery | Recursos novos desabilitados | Bancada específica | `HIL-CAP-01` |

## 2. Rede e comunicação

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-10 | Porta ocupada / firewall | **Alta** | `AbrirPortaComunicacao` falha | Mensagem com porta e processo ocupante | Runbook `RB-02` | `REL-05` |
| R-11 | IP duplicado / DHCP mudou | **Alta** | Identidade não confere com o IP | Quarentena; alerta de conflito | IP fixo (recomendação N6) | `HIL-NET-01` |
| R-12 | Cabo desconectado, flapping | **Alta** | Silêncio > T6 (22 s) | `Degraded` → T2/T3 conforme política | Reconexão com backoff+jitter | `CHAOS-NET-01` |
| R-13 | `ReceberDadosOnLine` travado | **Alta** | Watchdog sem heartbeat | Worker morto e reiniciado; **grupo isolado** | Circuit breaker | `CHAOS-DEV-01` |
| R-14 | Retorno nativo desconhecido | Média | Mapeamento exaustivo | Preservado e alertado ([ADR-0018](ADR/ADR-0018-eventos-desconhecidos.md)) | Vira item de matriz | `SIM-UNK-01` |

## 3. Configuração e relógio

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-20 | WebServer sobrescreve config do SDK | **Alta** | Leitura periódica + diff | Alerta de drift; **não** reescreve em silêncio | Reaplicar após aprovação | `INT-CFG-01` |
| R-21 | Config cruzada por buffer global | **Crítica** | Auditoria da sequência montar→enviar | Serialização por worker ([ADR-0006](ADR/ADR-0006-serializacao-montar-enviar.md)) | — | `CHAOS-CFG-01` |
| R-22 | Energia cai durante configuração | **Alta** | Envio não confirmado | Rollback para versão anterior | Reaplicar | `CHAOS-PWR-01` |
| R-23 | Relógios divergentes / horário de verão | Média | Drift medido a cada ciclo | Alerta; ordem por `received_time` | Sincronização em manutenção | `CHAOS-CLK-01` |

## 4. Credenciais e leitura

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-30 | Zeros à esquerda perdidos | **Crítica** | Importador detecta conversão numérica | Recusa a importação e explica | String sempre ([ADR-0008](ADR/ADR-0008-credencial-como-string.md)) | `HIL-CARD-02` |
| R-31 | Leitor incompatível / FC / dígitos variáveis | **Alta** | Bancada com cartões reais | Perfil de normalização por instalação | Recomissionar | `HIL-CARD-01..05` |
| R-32 | Lista maior que a capacidade do equipamento | **Alta** | Contagem antes do envio | Recusa e mostra a política de subconjunto | [ADR-0017](ADR/ADR-0017-niveis-de-degradacao.md) | `INT-OFF-02` |
| R-33 | Memória de bilhetes perto de 30.000 | **Alta** | Alarme em 70% e 85% | Coleta forçada | Runbook `RB-03` | `INT-REC-02` |
| R-34 | Urna perto/na capacidade física | **Alta** | Contador + origem 20 | `WorkflowBlocked`; orienta outro portão | Esvaziamento com custódia | `SIM-URNA-06` |

## 5. Fluxo físico

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-40 | Cartão recolhido sem passagem | **Alta** | Origem 7 sem origem 6 em T3 | `AuthorizedWithoutPassage` → reconciliação | Relatório e conferência | `SIM-URNA-02` |
| R-41 | Passagem sem confirmação (modelo sem origem 6) | **Alta** | Capacidade conhecida | Declarado ao operador; decisão de negócio (B5) | — | `HIL-DIR-03` |
| R-42 | Tailgating | Média | Contador de giro × autorizações | Relatório de divergência | Operação/segurança física | `INT-REC-01` |
| R-43 | Giro reverso | Média | Origem 6 em sentido inverso | Não consome ingresso; alerta | Investigação | `SIM-URNA-12` |
| R-44 | Liberação manual / evacuação | **Alta** | Comando de operador ou contato | Registrada com operador; política do gate | Auditoria | `CHAOS-PWR-01` |

## 6. Nuvem, dados e segurança

| ID | Risco | Sev. | Detecção | Comportamento | Recuperação | Teste |
|---|---|---|---|---|---|---|
| R-50 | Internet fora / token expirado / rate limit | Baixa (por projeto) | Conector falha | Outbox acumula; **operação intacta** | Retry + DLQ | `CHAOS-WAN-01` |
| R-51 | Schema externo alterado | Média | Teste de contrato em CI | Conector isolado; demais seguem | Nova versão do conector | `CONTRACT-*` |
| R-52 | Disco cheio | **Alta** | Limiar monitorado | Degradação anunciada; sem corrupção | Rotação/arquivamento | `CHAOS-DISK-01` |
| R-53 | Banco corrompido | **Alta** | `integrity_check` periódico | Modo somente leitura + alerta | Restauração de backup | `REL-06` |
| R-54 | Vazamento de dado sensível em log/exportação | **Crítica** | Varredura automatizada em CI | Redação no serializador | — | `SEC-LOG-01` |
| R-55 | Processo não autorizado acessa a API local | **Alta** | ACL de pipe + token | Conexão recusada e auditada | — | `SEC-IPC-01` |

## 7. Riscos de projeto

| ID | Risco | Sev. | Mitigação |
|---|---|---|---|
| R-60 | **SDKs indisponíveis** (situação atual) | **Crítica** | Fase 1 avança com mock/simulador; Fase 2 bloqueada. Escalar B1 |
| R-61 | Documentação ambígua vira suposição | **Alta** | Selos de procedência + tudo desabilitado por padrão |
| R-62 | Suporte do fabricante lento | Média | Pauta consolidada (abaixo), enviada de uma vez, com prazo |
| R-63 | Bancada indisponível | **Alta** | Nenhum modelo vai a produção sem ensaio — prazo cresce, escopo não |
| R-64 | Expectativa de "suporta tudo" | Média | Matriz publicada com lacunas visíveis desde o primeiro dia |
| R-65 | .NET 8 fora de suporte em nov/2026 | Média | [ADR-0016](ADR/ADR-0016-runtime-dotnet.md) propõe .NET 10 LTS |

---

## Pauta para a Topdata

Enviar de uma vez, com pedido de resposta por escrito. Cada item traz o que muda no
produto conforme a resposta — para que a prioridade fique clara para quem responde.

| # | Pergunta | O que muda |
|---|---|---|
| 1 | O limite de ~30 equipamentos é por instância, por thread ou por processo? | Modelo de particionamento e número de hosts |
| 2 | Os buffers de configuração são globais ao módulo ou por handle? | Se por handle, a janela de configuração de um parque grande cai de horas para minutos ([ADR-0006](ADR/ADR-0006-serializacao-montar-enviar.md)) |
| 3 | A DLL é reentrante entre threads do mesmo processo? | Modelo de threading do worker |
| 4 | Comportamento documentado de `ReceberDadosOnLine` em timeout e queda de socket | Estratégia de watchdog e de recuperação |
| 5 | Lista oficial e completa das origens de evento por firmware (incl. 11, 14–17, 19) | Fecha lacunas da matriz |
| 6 | Quais modelos emitem origem 6, e sob quais condições de sensor | Viabilidade do passo 9 do workflow da urna |
| 7 | Função correta para liberar **entrada** em instalação com urna invertida | Perfil físico e comissionamento |
| 8 | Na urna cheia, o equipamento recusa sozinho ou depende do software? | Onde o bloqueio é implementado |
| 9 | Precedência entre configuração do WebServer e do SDK | Estratégia de detecção de drift |
| 10 | Matriz oficial de firmware mínimo por função | Checagem de compatibilidade |
| 11 | Significado completo dos códigos de retorno, em especial o "erro 8" | Diagnóstico acionável em campo |
| 12 | Interoperabilidade: COM registrada ou exports `stdcall`? | Implementação do `Topdata.EasyInner.Interop` (B3) |
