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
| R-20 | **`EnviarConfiguracoes` sobrescreve tudo com os defaults da DLL** para campos não setados (confirmado) | **Crítica** | Teste de contrato: todo campo da matriz coberto pelo montador | Configuração **sempre completa e explícita** ([ADR-0020](ADR/ADR-0020-configuracao-sempre-completa.md)) | Reenvio completo | `INT-CFG-01` |
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
| R-60 | ~~SDKs indisponíveis~~ → **manual obtido**; falta o pacote de exemplos | Média | Fase 1 avança com mock/simulador. 51 de 58 funções já têm assinatura de fonte primária |
| R-61 | Documentação ambígua vira suposição | **Alta** | Selos de procedência + tudo desabilitado por padrão |
| R-62 | Suporte do fabricante lento | Média | Pauta consolidada (abaixo), enviada de uma vez, com prazo |
| R-63 | Bancada indisponível | **Alta** | Nenhum modelo vai a produção sem ensaio — prazo cresce, escopo não |
| R-64 | Expectativa de "suporta tudo" | Média | Matriz publicada com lacunas visíveis desde o primeiro dia |
| R-65 | .NET 8 fora de suporte em nov/2026 | Média | [ADR-0016](ADR/ADR-0016-runtime-dotnet.md) propõe .NET 10 LTS |
| R-66 | **A DLL exige .NET Framework 3.5 — pode ser mixed-mode e não carregar em processo .NET moderno** | **Alta** | Bench `HIL-STACK-01` antes de qualquer código. Se falhar, só o worker vira .NET FW 4.8 x86, isolado atrás do IPC ([ADR-0019](ADR/ADR-0019-stack-e-linguagem.md)) |
| R-67 | **Cada worker precisa de porta TCP própria**; mover catraca entre workers exige reconfigurar a catraca | **Alta** | Mapa porta↔worker↔equipamento como configuração versionada; assistente informa a porta ao instalador ([ADR-0021](ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md)) |
| R-68 | **`ColetarBilhete` remove o bilhete da memória ao retornar** — morte do processo entre a remoção e o commit local perde o evento | **Crítica** | Commit local antes de pedir o próximo bilhete; `CHAOS-REC-01` |

---

## Pauta para a Topdata

> **Atualizada em 24/09/2026**, depois da leitura do manual oficial. Os itens respondidos
> foram removidos. **O item 1 desta lista é o mais importante do projeto inteiro.**

| # | Pedido | O que muda |
|---|---|---|
| **1** | **NDA do protocolo TCP/IP de baixo nível** (citado pelo próprio manual, seção 6.7) | Elimina x86, Windows obrigatório, thread única, teto de ~30 equipamentos e porta por worker. Ver [ADR-0021](ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md) |
| 2 | Comportamento de `ReceberDadosOnLine` em timeout e em queda de socket, e o **valor** do retorno "sem eventos" | Estratégia de watchdog; hoje o valor só existe nos exemplos |
| 3 | Valor do retorno `RET_SEM_BILHETES` | Laço de coleta de bilhetes |
| 4 | Origens de evento **11, 14, 15, 16, 17 e 19** — existem? o que significam? | Ausentes da tabela oficial; hoje tratadas como desconhecidas |
| 5 | Tabela de códigos de `Linha` do firmware: o PDF traz 8 códigos (1, 2, 3, 6, 7, 14, 16, 18) para 7 descrições | Capability discovery por firmware |
| 6 | Na urna cheia, o equipamento recusa a leitura sozinho ou depende do software? | Onde o bloqueio é implementado |
| 7 | `ApagarListaAcesso` tem parâmetro `Inner`? Envia automaticamente? (o manual se contradiz) | Laço de atualização de lista |
| 8 | `DefinirQuantidadeDigitosCartao`: 4–16 (seção 4.1.2) ou 1–16 (tabela 4.1.9)? | Validação de parâmetro |
| 9 | Tipos de bilhete `003/004/005/010/012/013` colidem com `3/4/5/10/12/13` — como distinguir? | Parsing de bilhete da expedidora |
| 10 | Matriz oficial de **firmware mínimo** por função | Checagem de compatibilidade |
| 11 | Quais modelos emitem **origem 6**, e sob que condição de sensor | Fecha o passo final do fluxo da urna |
| 12 | Assinaturas de `DefinirTipoConexao`, `LiberarLeitor`, `InserirHorarioAcesso`, `EnviarHorariosAcesso`, `EnviarConfiguracoesFuncoes`, `EnviarDigitalUsuarioBio` | Provavelmente resolvido pelo pacote de exemplos; confirmar |
