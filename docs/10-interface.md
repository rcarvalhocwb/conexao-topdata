# 10 — Interface: sofisticada, em linguagem simples

Dois níveis, escolhidos por perfil e alternáveis:

- **Modo guiado** — operador comum. Nenhum termo do SDK, nenhum código de erro solto.
- **Modo técnico** — instalação e suporte. Mostra retorno nativo, estado da máquina,
  `correlationId`, diff de configuração.

Tradução de termos em [`GLOSSARIO.md`](GLOSSARIO.md).

## Telas obrigatórias

| # | Tela | O que resolve |
|---|---|---|
| 1 | **Dashboard do evento** | Fluxo/minuto, entradas confirmadas, negados, filas estimadas, ocupação, alertas |
| 2 | **Mapa/lista de gates** | Semáforo de saúde, latência, último evento, modo (T0–T3), capacidade da urna |
| 3 | **Assistente "Adicionar equipamento"** | Descoberta de rede, modelo, firmware, capacidades, leitor, direção física, teste de giro, checklist |
| 4 | **Pessoas e credenciais** | Busca instantânea, importação validada, deduplicação |
| 5 | **Regras com simulador** | *"Esta pessoa entraria agora por este gate?"* — com a explicação do resultado |
| 6 | **Monitor ao vivo** | Filtros, proteção de dados (mascaramento por padrão) |
| 7 | **Central de incidentes** | Exceções abertas com ação recomendada em português |
| 8 | **Sincronização** | Filas, conflitos, DLQ, reprocessamento |
| 9 | **Relatórios e exportações** | Tentativas, autorizações, **passagens confirmadas**, negados, **sem giro**, cartões recolhidos, urna cheia, ocupação, saúde, SLA |
| 10 | **Configuração técnica** | Diff, validação, aprovação, rollback |

A tela 5 usa **o mesmo código** que decide na operação, não uma reimplementação — senão
o simulador e a realidade divergem, e o simulador vira armadilha.

A tela 9 traz "autorizados", "passagens confirmadas" e "autorizados sem confirmação" como
colunas **distintas** ([ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md)). A terceira
é uma métrica de saúde da instalação, não um detalhe.

## Acessibilidade — requisito, não enfeite

WCAG 2.1 AA · navegação completa por teclado · alto contraste · paletas seguras para
daltonismo (**cor nunca é o único portador de informação**: o semáforo de saúde tem forma
e texto além da cor) · escalonamento de fonte até 200% sem perda de função · alvos de
toque ≥ 44 px · **1366×768 como resolução mínima suportada** · leitor de tela via UI
Automation.

Interface futurista ilegível é defeito. Em operação crítica, com pouca luz e pressa, o
que importa é contraste, tamanho e hierarquia — não efeito visual.

## Estado sem internet

O aplicativo **abre e mostra o estado local** mesmo sem internet, sem tela de erro, sem
espera. A ausência de nuvem aparece como um indicador discreto com a idade do último
sincronismo — não como bloqueio.
