# ADR-0006 — Serializar a sequência montar→enviar por worker

**Status:** **Confirmado por fonte primária** · **Data:** 2026-09-22 · **Revisto:** 2026-09-24

## Contexto

As configurações são montadas em buffers internos da DLL e depois enviadas.

O manual oficial (`FONTE_PRIMARIA`) **fecha a dúvida**: a EasyInner.dll é declarada
**não thread-safe** — *"Múltiplas threads não devem chamar funções da EasyInner.dll
simultaneamente. O acesso à DLL deve ser serializado"* — e `EnviarConfiguracoes()` **limpa
o buffer após o envio**, confirmando que o buffer é do módulo, não do equipamento.

A arquitetura recomendada pela própria Topdata é **uma única thread dedicada** executando
a máquina de estados de **todas** as catracas daquele processo.

## Decisão

**Toda** sequência `montar…→enviar` — e, na verdade, **toda** chamada à DLL — é
serializada em uma única thread dedicada por worker. Nenhuma outra chamada
de configuração ocorre entre o início da montagem e a confirmação do envio. A sequência é
uma unidade auditada: início, fim, dispositivo, retorno.

## Consequências

- Configurar N equipamentos é O(N) sequencial. Em parque grande, a janela de configuração
  é longa — daí a importância de configurar **antes** do evento, com progresso visível.
- Elimina a classe inteira de defeitos de configuração cruzada.
- **A hipótese de relaxar para serialização por dispositivo está descartada**: a DLL
  inteira é não thread-safe, não apenas o buffer de configuração. O único caminho para
  paralelismo real é o protocolo sob NDA ([ADR-0021](ADR-0021-porta-por-worker-e-protocolo-nda.md)).

## Alternativas recusadas

- **Serializar por dispositivo agora.** Rápido e possivelmente corrompido — o modo de
  falha é silencioso, que é o pior tipo.
- **Descobrir empiricamente por tentativa.** Um teste que "passou" não prova ausência de
  corrida; prova que não ocorreu naquela execução.
