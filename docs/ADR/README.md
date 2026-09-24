# Registros de Decisão Arquitetural (ADR)

Formato: contexto → decisão → consequências → alternativas recusadas → status.
Um ADR não é revisado, é **substituído**: se a decisão muda, cria-se um novo que
declara `Substitui ADR-XXXX`, e o antigo passa a `Substituído`. O histórico fica.

| ADR | Título | Status |
|---|---|---|
| [0001](ADR-0001-isolar-easyinner-em-processo-x86.md) | Isolar a EasyInner em processo x86 dedicado | Aceito |
| [0002](ADR-0002-local-first.md) | Local-first: nuvem fora do caminho crítico | Aceito |
| [0003](ADR-0003-sqlite-wal-outbox.md) | SQLite/WAL com outbox-inbox transacional | Aceito |
| [0004](ADR-0004-ipc-grpc-named-pipes.md) | IPC local por gRPC sobre named pipes | Aceito |
| [0005](ADR-0005-particionamento-por-worker.md) | Particionamento de equipamentos por worker | Aceito |
| [0006](ADR-0006-serializacao-montar-enviar.md) | Serializar montar→enviar por worker | **Confirmado por fonte primária** |
| [0007](ADR-0007-autorizacao-versus-passagem.md) | Autorização e passagem física são eventos distintos | Aceito |
| [0008](ADR-0008-credencial-como-string.md) | Credencial é sempre string | Aceito |
| [0009](ADR-0009-identidade-de-evento.md) | Identidade e ordenação de eventos | Aceito |
| [0010](ADR-0010-capability-discovery.md) | Descoberta de capacidade antes de habilitar recurso | Aceito |
| [0011](ADR-0011-facial-separado.md) | Módulo facial separado do EasyInner | Aceito |
| [0012](ADR-0012-wpf-versus-winui.md) | WPF como camada de apresentação | Aceito |
| [0013](ADR-0013-fail-safe-versus-fail-secure.md) | Fail-safe × fail-secure configurável por gate | Proposto — depende de B4 |
| [0014](ADR-0014-criptografia-e-biometria.md) | Criptografia em repouso e segregação de biometria | Aceito |
| [0015](ADR-0015-conectores-versionados.md) | Conectores de nuvem versionados e isolados | Aceito |
| [0016](ADR-0016-runtime-dotnet.md) | Runtime .NET alvo | Proposto — decisão do cliente |
| [0017](ADR-0017-niveis-de-degradacao.md) | Níveis de degradação T0–T3 | Aceito |
| [0018](ADR-0018-eventos-desconhecidos.md) | Eventos desconhecidos são preservados, nunca descartados | Aceito |
| [0019](ADR-0019-stack-e-linguagem.md) | Stack e linguagem | Aceito |
| [0020](ADR-0020-configuracao-sempre-completa.md) | A configuração enviada é sempre completa e explícita | Aceito |
| [0021](ADR-0021-porta-por-worker-e-protocolo-nda.md) | Uma porta TCP por worker, e o protocolo sob NDA | Aceito / Proposto |
