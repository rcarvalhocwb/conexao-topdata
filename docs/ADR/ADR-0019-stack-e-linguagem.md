# ADR-0019 — Stack e linguagem

**Status:** Aceito · **Data:** 2026-09-24 · **Complementa:** ADR-0012, ADR-0016

## Contexto

Pediu-se a "melhor linguagem", sem obrigação de .NET Framework. O manual oficial
(`FONTE_PRIMARIA`) confirma que a EasyInner.dll é Win32 **x86**, **bloqueante** e **não
thread-safe**, com exemplos oficiais em C#, Java, Delphi e VB6, e assinaturas documentadas
em convenções de marshalling do .NET (`ref byte`, `StringBuilder`).

A restrição recai sobre **um** componente: o worker. O resto do sistema é livre.

## Decisão

- **Worker que carrega a DLL:** C# sobre **.NET 10**, compilado `win-x86`.
- **Todo o resto:** C# / .NET 10, x64; interface em WPF; domínio sem dependência de SO,
  testado em CI Linux.
- **Adapter facial:** C# com WebSocket/JSON, x64 — não precisa ser x86.

Uma stack só, porque duas linguagens custariam dois toolchains e uma fronteira de
serialização a mais, sem resolver o gargalo real (uma DLL bloqueante de 32 bits).

## Consequências

- O `EasyInner.cs` oficial pode ser usado como ponto de partida do interop, em vez de
  deduzir assinaturas.
- `StringBuilder` e `ref byte` mapeiam diretamente — elimina a classe de defeitos de
  marshalling manual (estouro de buffer, corrupção de heap) que Rust, Go ou Python
  introduziriam **neste ponto específico**.
- **Risco aberto:** a DLL exige .NET Framework 3.5, o que sugere assembly mixed-mode.
  Carregá-la de um processo .NET moderno faria dois CLRs conviverem. Suportado pelo
  Windows, **não provado para esta DLL**. Bench `HIL-STACK-01` decide; se falhar, só o
  worker vira .NET Framework 4.8 x86, isolado atrás do IPC.

## Alternativas recusadas (para o worker)

- **Rust:** todo o acesso à DLL seria `unsafe`; a garantia que justifica Rust desaparece e
  o custo de escrever bindings à mão permanece.
- **Go:** `cgo` em x86 no Windows, chamadas bloqueantes prendendo threads do runtime,
  nenhum exemplo oficial.
- **Python:** `ctypes` funciona; empacotar serviço Windows x86 com watchdog dá mais
  trabalho do que o C# entrega pronto.
- **Java / Delphi / VB6:** têm exemplos oficiais, mas somam runtimes na máquina de campo
  (Java) ou têm ferramental de teste e observabilidade muito inferior.

Nenhuma dessas recusas vale para o **resto** do sistema, nem para o gateway futuro sob NDA
(ADR-0021).
