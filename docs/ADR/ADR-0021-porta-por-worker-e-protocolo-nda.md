# ADR-0021 — Uma porta TCP por worker, e a opção do protocolo sob NDA

**Status:** Aceito (porta por worker) · Proposto (NDA) · **Data:** 2026-09-24
**Complementa:** ADR-0005

## Contexto

O manual oficial (seção 6.7) confirma o limite de ~30 equipamentos por instância da DLL —
"em uma única thread de comunicação" — e recomenda múltiplas instâncias da aplicação
integradora. E acrescenta um detalhe que só aparece ali:

> *"Isso pode exigir que cada instância escute em uma porta TCP diferente (ex.: Instância 1
> na porta 3570, Instância 2 na 3571)."*

O mesmo capítulo registra uma alternativa:

> *"a integração direta via protocolo TCP/IP (sem a DLL, conforme documentação de baixo
> nível e solicitação de NDA) pode ser uma alternativa. Essa abordagem permite maior
> controle sobre o gerenciamento de conexões e threads."*

## Decisão

**Parte A — porta por worker (aceito).** Cada worker escuta em sua própria porta TCP. O
mapa `equipamento ↔ worker ↔ porta` é configuração de primeira classe, versionada e
auditada. O assistente de comissionamento informa ao instalador **qual porta** configurar
naquela catraca, e o produto detecta e alerta quando um equipamento conecta na porta de
outro grupo.

**Parte B — protocolo sob NDA (proposto).** Solicitar à Topdata, em paralelo à Fase 1, a
documentação de baixo nível sob NDA. Não é caminho crítico, mas o prazo de fabricante é
longo e a porta precisa estar aberta antes da Fase 2.

## Consequências

### Da parte A

- Realocar uma catraca entre workers **exige reconfigurar a catraca** — não é uma operação
  de software. Isso precisa estar no runbook e no planejamento do evento.
- Balanceamento de carga entre workers deixa de ser dinâmico: é decisão de projeto da
  instalação (mais um motivo para agrupar por afinidade física, ADR-0005).
- O firewall precisa liberar uma faixa de portas, não uma só.
- Porta ocupada é causa documentada de falha em `AbrirPortaComunicacao` — o worker checa
  e reporta o processo ocupante, em vez de devolver "erro".

### Da parte B, se o NDA for concedido

Caem, de uma vez: x86 obrigatório, Windows obrigatório, thread única, teto de ~30
equipamentos, porta por worker, erro 8/GPF e registro de DLL. O gateway passa a ser **um
serviço** com I/O assíncrono, possivelmente em Linux, em Rust ou Go.

A migração é contida por construção: só `Topdata.EasyInner.Adapter` é substituído, atrás
da interface `ITopdataInnerAdapter`. Domínio, interface, banco e testes não mudam.

## Alternativas recusadas

- **Uma porta única com proxy/multiplexador na frente.** As catracas abrem a conexão e a
  DLL espera ser a dona do socket; interpor um proxy quebraria o protocolo proprietário.
- **Esperar o NDA para começar.** A Fase 1 não depende dele; travar o projeto por uma
  resposta de fabricante seria trocar risco técnico por risco de prazo.
