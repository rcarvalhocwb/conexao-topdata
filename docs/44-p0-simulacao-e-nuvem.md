# P0-01 e P0-02: simulação separada do hardware e estados reais da nuvem

Status: **APROVADO** nos testes automatizados (Linux). **BLOQUEADO** nos testes físicos (sem catraca/SDK).
**PENDENTE** a validação visual no Windows (WPF não compila nesta máquina Linux; nenhum XAML foi alterado).

## O que estava errado

- **Simulação (P0-01):** `EquipamentosConectados` somava catraca simulada com física; a barra mostrava
  "x/y online" com sinal verde mesmo com só simulada; a linha da simulada aparecia como "Atendendo" igual
  a uma física; com simulação ativa a frase "Nenhuma catraca conectada" podia sair junto do selo.
- **Nuvem (P0-02):** "internet" era inferida de um último sucesso com menos de 2 min. Assim, uma recusa de
  credencial (401/403) aparecia como "sem internet", e um segredo ausente como "sem sincronização", sem
  distinção. Erro de servidor e falta de rede também não se separavam.

## O que mudou

| Área | Mudança |
|---|---|
| `Sync.Core` | `TipoDeFalha` (Nenhuma, Outra, Servidor, Rede, Autenticacao) e `TipoDeFalhaHttp` (classifica status e exceção; o mais grave vence). `RespostaDeItem` e `ResumoDaRodada` carregam o tipo. |
| `Sync.Connectors.Rest` | 401/403 deixam de ser "cartas mortas": ficam na fila como `Autenticacao` (decisão E5-2). Rede e destino são classificados à parte. |
| `Sync.Ingestao` | `ResumoDaIngestao.Causa` expõe a exceção; a classificação fica no supervisor (a regra de arquitetura proíbe a referência). |
| `Edge.Supervisor/EstadoDaNuvem` | Estados medidos pela última **tentativa recente** (janela = máx(2 min, 2 × intervalo)). `Internet`, `Nuvem`, `Sincronizacao`. Segredo ausente é estado próprio. |
| `Edge.Supervisor/SincronizacaoComANuvem` | `EmAndamento` (try/finally), `SegredoAusente`, tipo de falha por etapa, tempo da tentativa. |
| `Contracts` (proto) | `ObterEstadoResponse` ganha `situacao_da_internet` (19), `situacao_da_nuvem` (20), `situacao_da_fila` (21), `catracas_fisicas_conectadas` (22), `catracas_simuladas_ativas` (23). Enums com zero = `NAO_ESPECIFICADO`. `equipamentos_conectados` continua, agora só com físicas. |
| `Desktop.ViewModels` | Textos por categoria ("Internet: fora…", "Nuvem: o destino recusou a credencial…", "Nuvem: segredo ausente…", "Fila: N aguardando envio"). Catraca simulada recebe o prefixo "Simulada · ". Barra mostra "N simulada(s) ativa(s)" quando não há física. |

## Regras que os testes protegem

- Uma catraca conta em uma única categoria: física ou simulada.
- Sem tentativa recente, o estado é "desconhecido", nunca "sem internet".
- Sucesso antigo não prova internet nem a falta dela.
- Credencial recusada prova que a internet funciona (o destino respondeu) e aparece como problema da nuvem.
- Falta de rede aparece como internet indisponível; erro de servidor não aparece como falta de internet.
- Segredo ausente é diferente de "não configurada".
- Falha na fila continua "falha" mesmo com itens pendentes, até um sucesso.

## Evidência (Linux, Release e Debug)

| Suíte | Resultado | Linha de base |
|---|---|---|
| Unit | 927 aprovados, 0 falhas | 925 |
| Integration | 797 aprovados, 0 falhas, 4 ignorados (já existiam) | 781 + 4 ignorados |
| Contract | 59 aprovados, 0 falhas | 59 |

Testes novos: `tests/Integration/EstadoDaNuvemTests.cs` (estados por tentativa, janela, fila, recuperação) e
`tests/Integration/SituacaoPorCategoriaTests.cs` (simulada fora da contagem física, prefixo, credencial
recusada, segredo ausente).

Testes atualizados por mudança intencional: `SessaoDoServicoTests` (contagem separada; prefixo "Simulada · "),
`TelasTests` ("Nuvem: não configurada nesta máquina" no lugar de "sem sincronização"),
`ConectorRestTests` (401/403 na fila).

## Riscos e lacunas (PENDENTE / BLOQUEADO)

- **BLOQUEADO:** ensaio com catraca física e SDK (G1d). Nada desta mudança foi testado com hardware.
- **PENDENTE:** validação visual das novas frases e do prefixo no WPF (Windows). O CI roda a suíte, não os XAML.
- Com simulação e física ao mesmo tempo, o "x/y" usa o total de catracas cadastradas, que inclui as simuladas.
  Separar esse total fica para a próxima rodada de configuração (P0-03).
- A Fila e a Nuvem são medidas pela última tentativa. Se o laço parar sem tentar, a tela cai em "sem resposta
  recente" em vez de "falha": é o comportamento honesto, mas o operador precisa saber disso.
