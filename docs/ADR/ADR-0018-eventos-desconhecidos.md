# ADR-0018 — Eventos desconhecidos são preservados, nunca descartados

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

As origens de evento conhecidas no briefing deixam lacunas (11, 14–17, 19, e qualquer
valor futuro). Firmwares novos introduzem eventos. Um `switch` com `default: ignore` é a
forma mais eficiente de tornar um problema de campo invisível — e o campo, aqui, é um
evento com 50.000 pessoas.

## Decisão

- Tipo `EventOrigin` com `FromRaw(int) → Known(origin) | Unknown(raw)`; `switch` exaustivo
  garantido por compilação.
- Todo evento é persistido **íntegro** em `raw_event` (payload original preservado) antes
  de qualquer interpretação.
- Origem desconhecida: registrada, contada em `unknown_origin_total{device,firmware,raw}`,
  exibida com destaque no monitor ao vivo e incluída no pacote de diagnóstico.
- Retorno nativo desconhecido recebe o mesmo tratamento.
- **Nunca** descartada, nunca convertida em "erro genérico".

## Consequências

- Um firmware novo produz um alerta compreensível — "Catraca 12 enviou um evento que este
  sistema ainda não conhece (código 14)" — em vez de silêncio, e vira insumo direto para a
  matriz de compatibilidade e para a pauta com a Topdata.
- Custo de armazenamento do payload bruto: irrelevante diante do valor diagnóstico.
- A matriz de compatibilidade cresce por evidência de campo, não só por documentação.

## Alternativas recusadas

- **Ignorar o desconhecido.** Transforma defeito em mistério.
- **Falhar ao receber desconhecido.** Um evento inesperado derrubaria a operação — o
  oposto da robustez pretendida.
