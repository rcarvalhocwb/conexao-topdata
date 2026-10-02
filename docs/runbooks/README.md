# Runbooks

Procedimentos operacionais. Um runbook é escrito para ser executado **às 19h de um sábado,
por alguém cansado, com fila na porta**: passos numerados, sem ambiguidade, com o critério
de sucesso explícito em cada passo.

| ID | Situação | Fase |
|---|---|---|
| `RB-01` | Worker não inicia / retorno 8 | 2 |
| `RB-02` | Porta ocupada ou firewall bloqueando | 2 |
| `RB-03` | Memória de bilhetes perto do limite | 2 |
| `RB-04` | Urna cheia durante o evento | 3 |
| `RB-05` | Cartão preso no coletor | 3 |
| `RB-06` | Catraca sem comunicação | 2 |
| `RB-07` | Queda total do Edge durante o evento | 3 |
| `RB-08` | Disco cheio | 4 |
| `RB-09` | Banco corrompido — restauração | 5 |
| `RB-10` | Evacuação / liberação geral | 3 |
| `RB-11` | Backlog de sincronização após retorno da internet | 4 |
| `RB-12` | Pacote de diagnóstico para abrir chamado | 2 |

Os runbooks são escritos junto com a fase que os torna possíveis — um runbook sobre um
comportamento ainda não implementado é ficção. O template está em
[`TEMPLATE.md`](TEMPLATE.md).
