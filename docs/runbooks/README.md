# Runbooks

Procedimentos operacionais. Um runbook é escrito para ser executado **às 19h de um sábado,
por alguém cansado, com fila na porta**: passos numerados, sem ambiguidade, com o critério
de sucesso explícito em cada passo.

| ID | Situação | Fase | Estado |
|---|---|---|---|
| `RB-01` | Worker não inicia / retorno 8 | 2 | Escrito: [RB-01](RB-01-worker-nao-inicia.md) |
| `RB-02` | Porta ocupada ou firewall bloqueando | 2 | Escrito: [RB-02](RB-02-porta-ou-firewall.md) |
| `RB-03` | Memória de bilhetes perto do limite | 2 | Pendente: ainda não há alerta de memória de bilhetes |
| `RB-04` | Urna cheia durante o evento | 3 | Pendente: a urna ainda não tem comportamento ensaiado (T11) |
| `RB-05` | Cartão preso no coletor | 3 | Pendente: o recolhimento de cartão não está documentado (T11) |
| `RB-06` | Catraca sem comunicação | 2 | Escrito: [RB-06](RB-06-catraca-sem-comunicacao.md) |
| `RB-07` | Queda total do Edge durante o evento | 3 | Pendente: depende da decisão de contingência (T08) |
| `RB-08` | Disco cheio | 4 | Pendente: falta o alerta de disco (A10) |
| `RB-09` | Banco corrompido — restauração | 5 | Escrito: [RB-09](RB-09-restauracao-do-banco.md) |
| `RB-10` | Evacuação / liberação geral | 3 | Pendente: depende da decisão de fail-safe (T07) |
| `RB-11` | Backlog de sincronização após retorno da internet | 4 | Pendente: falta a tela de reprocessamento da fila (A09) |
| `RB-12` | Pacote de diagnóstico para abrir chamado | 2 | Pendente: falta o botão de exportar o pacote de diagnóstico (A07) |

Os runbooks são escritos junto com a fase que os torna possíveis — um runbook sobre um
comportamento ainda não implementado é ficção. O template está em
[`TEMPLATE.md`](TEMPLATE.md).
