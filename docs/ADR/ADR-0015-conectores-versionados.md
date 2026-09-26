# ADR-0015 — Conectores de nuvem versionados e isolados

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O briefing pede integração com REST, webhooks, WebSocket/MQTT, PostgreSQL, SQL Server,
MySQL/MariaDB, Supabase, filas e CSV/JSON. A tentação é prometer "conecta com qualquer
banco". Conectar um banco em nuvem diretamente à rede das catracas é, além de frágil, um
problema de segurança.

## Decisão

Um SDK de conectores com contrato versionado. Cada conector declara: mapeamento de schema,
validação, transformação, rate limit, retry exponencial com jitter, DLQ, reprocessamento
manual, idempotency key, cursor/checkpoint, compressão em lote, observabilidade e teste de
conexão.

Bancos são alcançados **preferencialmente por API/backend seguro**. Conexão direta a banco
remoto é possível, porém exige rede dedicada e é apresentada ao cliente com suas
limitações escritas. **Nenhum banco em nuvem é exposto à VLAN das catracas.**

## Consequências

- Adicionar conector é implementar um contrato, com sua suíte de testes de contrato — não
  mexer no núcleo.
- Mudança de schema externo quebra o teste de contrato em CI, não a operação do evento.
- CSV/JSON de importação/exportação é garantia de contingência: se toda integração falhar,
  o evento acontece do mesmo jeito.

## Alternativas recusadas

- **Acesso direto a banco a partir do núcleo.** Acopla o hardware ao schema de terceiro e
  coloca latência de rede externa perto do caminho crítico.
- **ORM genérico multi-banco como "conector universal".** Esconde diferenças semânticas e
  produz a promessa falsa que este ADR existe para evitar.
