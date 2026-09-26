# ADR-0003 — SQLite/WAL com outbox-inbox transacional

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O sistema precisa sobreviver a queda de energia, `kill -9` e disco cheio sem perder nem
duplicar acessos, e precisa instalar em máquina simples sem exigir servidor de banco.

## Decisão

SQLite em modo WAL, com `foreign_keys=ON`, `synchronous=FULL` nas transações de acesso,
migrações versionadas e verificação periódica de integridade. Evento, decisão, comando e
item de sincronização são gravados na **mesma transação** (transactional outbox). Mensagens
recebidas passam por tabela de inbox com chave de idempotência.

PostgreSQL local é alternativa suportada quando o porte justificar, atrás da mesma
interface de repositório — sem jamais tornar-se pré-requisito de instalação.

## Consequências

- Um acesso confirmado localmente nunca é perdido, mesmo se o processo morrer no
  microssegundo seguinte.
- `synchronous=FULL` custa latência de disco; por isso a meta de p95 é medida com ele
  ligado, e não em benchmark otimista.
- Backup precisa ser consistente com WAL (snapshot via API de backup do SQLite, não `cp`).
- Escritas concorrentes são serializadas — aceitável no volume previsto (dezenas de
  eventos/s), validado em `LOAD-DB-01`.

## Alternativas recusadas

- **Gravar direto na nuvem.** Viola ADR-0002 e perde eventos em queda de link.
- **Arquivo de log append-only próprio.** Reimplementar durabilidade, índices e consulta é
  trabalho já feito e mais bem testado pelo SQLite.
- **LiteDB / embedded NoSQL.** Menor garantia transacional e ferramental de diagnóstico
  muito inferior em campo.
