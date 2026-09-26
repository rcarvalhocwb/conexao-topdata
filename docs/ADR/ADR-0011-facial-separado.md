# ADR-0011 — Módulo facial separado do EasyInner

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

A linha Inner usa EasyInner sobre TCP. A linha facial usa SDK próprio, com WebSocket e
JSON, incluindo keepalive com resposta obrigatória. Um mesmo equipamento pode exigir os
dois: EasyInner para giro e configuração, SDK Facial para pessoas e faces.

## Decisão

Dois adaptadores independentes: `Topdata.EasyInner.Adapter` e `Topdata.Facial.Adapter`,
com contratos, ciclos de vida, modelos de erro e processos distintos. O facial **não**
precisa ser x86 e roda fora do worker da DLL. Onde um dispositivo físico exige ambos, o
domínio o modela como **um** `Device` com **duas** capacidades e duas conexões — nunca
como uma API unificada fictícia.

## Consequências

- Nenhuma abstração "comum" mentirosa escondendo semânticas incompatíveis.
- Duas máquinas de estado para alguns equipamentos; a UI apresenta isso como um único
  equipamento com dois indicadores de saúde.
- O `sendlog` facial alimenta `AccessAuthorized`, jamais `PhysicalPassage` (ADR-0007).
- O keepalive do facial tem timer próprio: ignorá-lo derruba a sessão em silêncio.

## Alternativas recusadas

- **Interface unificada `IDeviceAdapter`.** O menor denominador comum apagaria justamente
  as diferenças que importam, e vazaria em cada caso especial.
