# ADR-0001 — Isolar a EasyInner em processo x86 dedicado

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

A `EasyInner.dll` é descrita como nativa, Windows, **32 bits**, síncrona e sequencial
(`BRIEFING_NAO_VERIFICADO`). Um processo .NET só carrega uma DLL x86 se ele próprio for
x86. Além disso, uma falha em código nativo — GPF, corrupção de heap, laço travado — não
é capturável por `try/catch` gerenciado: ela derruba o processo inteiro.

## Decisão

Somente o projeto `Edge.Worker.X86`, compilado explicitamente com
`RuntimeIdentifier=win-x86`, carrega a DLL. A interface gráfica roda x64 em processo
separado e **não referencia**, nem transitivamente, nenhum projeto de interoperabilidade.
Toda interação passa por `ITopdataInnerAdapter`.

## Consequências

- Uma falha nativa mata um worker e até ~20 equipamentos, nunca a operação inteira nem a UI.
- Custo de serialização IPC em cada comando — aceitável: são dezenas de eventos por
  segundo, não centenas de milhares.
- O worker pode ser reiniciado a quente sem fechar a aplicação.
- O teste de arquitetura que proíbe a referência é obrigatório; sem ele, a regra apodrece
  no primeiro prazo apertado.

## Alternativas recusadas

- **Aplicação inteira em x86.** Limita memória, contamina a UI com a fragilidade da DLL e
  torna impossível matar o componente problemático isoladamente.
- **`AppDomain`/`AssemblyLoadContext` isolado.** Não protege contra falha em código nativo
  e não resolve a incompatibilidade de arquitetura.
- **COM surrogate (`dllhost.exe`).** Menos controle sobre lifecycle, watchdog e diagnóstico
  do que um worker próprio.
