# ADR-0004 — IPC local por gRPC sobre named pipes

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Três processos precisam conversar na mesma máquina: UI (x64), Supervisor (x64) e workers
(x86). O canal precisa ser autenticado, não exposto à rede, com contrato versionado e
streaming para eventos ao vivo.

## Decisão

gRPC sobre named pipes, usando o transporte de named pipe do Kestrel (`ListenNamedPipe`,
disponível a partir do .NET 8). ACL do pipe restrita à conta do serviço e ao grupo de
operadores, mais token de sessão por conexão. Contratos `.proto` em `Contracts/`, com
teste de compatibilidade retroativa em CI.

## Consequências

- Nenhuma porta TCP aberta para IPC — reduz superfície e evita conflito de porta com o
  socket dos equipamentos.
- Streaming servidor→cliente elimina polling na UI.
- Contrato versionado permite atualizar UI e serviço em momentos diferentes.
- Amarra a solução ao Windows moderno; irrelevante, já que a DLL já amarra.

## Alternativas recusadas

- **TCP em `localhost`.** Qualquer processo local alcança; exige autenticação própria e
  abre porta à toa.
- **Named pipes com protocolo próprio.** Reimplementar enquadramento, versionamento e
  streaming sem ganho.
- **COM/.NET Remoting.** Legado, ferramental pobre, difícil de testar.
- **Memória compartilhada.** Rápida e desnecessária neste volume; complexidade alta.
