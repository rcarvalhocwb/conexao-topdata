# ADR-0008 — Credencial é sempre string

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Identificadores de cartão têm zeros à esquerda, comprimento variável, Facility Code e às
vezes caracteres não numéricos. Converter para inteiro destrói informação de forma
irreversível: `0001234` vira `1234`, e o cartão passa a ser recusado — ou, pior, a
colidir com outro.

## Decisão

Toda credencial trafega, é persistida e é comparada como **string**. Existe um tipo
`CredentialValue` que encapsula valor bruto, valor normalizado e padrão de normalização;
o `ToString()` devolve **mascarado**. A normalização (padding, truncamento, extração de
FC) é configurável por padrão de leitura e coberta por testes com casos reais.

## Consequências

- Índices de banco sobre texto; custo desprezível no volume previsto.
- Comparação é sempre explícita sobre o valor normalizado, nunca sobre o bruto.
- Importação de planilha exige tratamento cuidadoso: o Excel converte para número e come
  os zeros. O importador detecta e alerta esse caso em vez de aceitar em silêncio.
- Mascaramento no `ToString()` faz o log seguro ser o caminho padrão, não o disciplinado.

## Alternativas recusadas

- **Inteiro de 64 bits.** Perde zeros à esquerda e caracteres; falha silenciosa.
- **String sem normalização.** Joga a inconsistência para dentro da regra de negócio, onde
  reaparece como "às vezes funciona".
