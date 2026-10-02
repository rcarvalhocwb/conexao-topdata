# Glossário — termo técnico → linguagem do operador

A interface tem dois níveis: **modo guiado** (operador) e **modo técnico** (instalação e
suporte). Este glossário é a fonte da tradução. Nenhum termo da coluna da esquerda aparece
sozinho na tela do modo guiado.

| Termo técnico | Como aparece para o operador |
|---|---|
| Edge Gateway | "Computador de controle das catracas" |
| Worker | "Grupo de catracas" |
| `ReceberDadosOnLine` travado | "Catraca 08 parou de responder — reiniciando automaticamente" |
| Modo off-line (T2) | "Catraca operando com a lista local" |
| Sem internet (T1) | "Operando sem internet — nenhuma ação necessária" |
| `OfflineFallback` | "Decisão tomada pela regra local" |
| `AuthorizedWithoutPassage` | "Liberou, mas não confirmou a passagem" |
| Origem 6 | "Giro confirmado" |
| Origem 7 | "Cartão recolhido" |
| Origem 20 | "Urna cheia" |
| Anti-passback | "Impedir a mesma pessoa de entrar duas vezes" |
| Anti-replay | "Ignorar leitura repetida do mesmo cartão" |
| Outbox pendente | "Informações aguardando envio" |
| Dead-letter queue | "Envios que falharam e precisam de revisão" |
| Drift de configuração | "A catraca está com configuração diferente da definida aqui" |
| Drift de relógio | "O relógio da catraca está atrasado/adiantado em X" |
| `Quarantined` | "Equipamento isolado até verificação" |
| `FirmwareMismatch` | "Versão da catraca não compatível — não será configurada" |
| Capability discovery | "Verificando o que este equipamento sabe fazer" |
| Fail-safe / fail-secure | "Em caso de falha, o portão: abre / permanece fechado" |
| Idempotência | *(não aparece — é garantia interna)* |
| `reasonCode` | *(aparece só como detalhe expansível, ao lado da mensagem)* |

## Regra de escrita das mensagens

**Acionável, específica, sem culpa.** Diz o que houve, o que o sistema já está fazendo, e
o que a pessoa precisa fazer (ou que não precisa fazer nada).

> ✅ "Catraca 08 sem comunicação há 22 s — operando com lista local; nenhuma ação imediata"
> ❌ "Erro 1"

O código nativo nunca é a mensagem. Ele aparece em **detalhe expansível**, para o suporte,
junto de `correlationId`, retorno nativo e estado da máquina.
