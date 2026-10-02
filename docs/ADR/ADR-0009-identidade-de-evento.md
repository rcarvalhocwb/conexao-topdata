# ADR-0009 — Identidade e ordenação de eventos

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Eventos vêm de dezenas de equipamentos com relógios que divergem, chegam fora de ordem,
são reenviados após reconexão e precisam ser reconciliados com bilhetes coletados da
memória do equipamento — sem duplicar e sem perder.

## Decisão

Todo evento carrega:

- `eventId`: **UUIDv7** (ordenável por tempo, gerado na borda);
- `siteId`, `deviceId`, `bootId` (identifica o ciclo de vida do equipamento desde o último
  boot), e `deviceSeq` (sequência monotônica por dispositivo dentro do `bootId`);
- **três** carimbos de tempo: `deviceTime`, `receivedTime`, `serverTime`.

Chave de deduplicação: `(deviceId, bootId, deviceSeq)`, com índice único. A ordenação para
exibição e relatório usa `receivedTime`; `deviceTime` é preservado e o **drift é medido e
alertado**, nunca corrigido silenciosamente.

## Consequências

- Reenvio após reconexão e reconciliação de bilhetes off-line são naturalmente idempotentes.
- Relógio errado no equipamento vira alerta visível e métrica, não corrupção de ordem.
- `bootId` precisa ser derivado de algo observável do equipamento ou atribuído pela borda
  ao detectar reinício — o mecanismo exato depende do que o SDK expõe
  (`A_VERIFICAR_NO_WRAPPER`); há fallback pela borda.

## Alternativas recusadas

- **UUIDv4.** Não ordenável; fragmenta índice e piora inserção em lote.
- **Chave por `(deviceId, deviceTime)`.** Relógio que volta atrás gera colisão real.
- **Autoincremento local.** Não sobrevive a múltiplos workers nem à reconciliação.
