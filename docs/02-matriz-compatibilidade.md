# 02 — Matriz de compatibilidade (estado inicial: lacuna)

## Como ler este documento

A matriz pedida tem o formato:

> função do SDK → modelos compatíveis → modo on-line/off-line → parâmetros → retorno →
> timeout → efeito físico → pré-condições → risco → teste correspondente

**Nenhuma linha pode ser preenchida com honestidade hoje**, porque os SDKs não estão
disponíveis e o acesso às fontes públicas da Topdata está bloqueado neste ambiente
(ver "Aviso de procedência" no [README](../README.md)).

O que existe aqui é o **arcabouço da matriz**, já com as funções nomeadas no briefing do
cliente, cada uma marcada `A_VERIFICAR_NO_WRAPPER`. Os campos de parâmetro, retorno e
timeout estão **deliberadamente vazios** — preenchê-los por dedução seria exatamente a
invenção que a regra proíbe.

Arquivos legíveis por máquina (fonte da verdade, versionada):

- [`compatibility-matrix/funcoes-easyinner.csv`](compatibility-matrix/funcoes-easyinner.csv)
- [`compatibility-matrix/origens-evento.csv`](compatibility-matrix/origens-evento.csv)
- [`compatibility-matrix/modelos.csv`](compatibility-matrix/modelos.csv)

Na Fase 1 esses CSVs passam a ser **consumidos por teste automatizado**: um teste de
contrato falha se o código chamar uma função nativa que não esteja na matriz, ou usar uma
origem de evento sem registro. A matriz deixa de ser documentação e vira trava.

## Modelos declarados no briefing

`BRIEFING_NAO_VERIFICADO` — produtos listados para o SDK Inner Acesso:
Catraca Fit 4, Revolution 4, Box 4, PNE 4, Coletor Inner Acesso 2, Inner Acesso, Coletor Urna.

Notas de linha, também não verificadas:

- Linha 3 / Inner Acesso é **legada**; Linha 4 / Controle Catraca tem recursos mais novos e
  maior flexibilidade de leitura.
- A linha **Catracas Easy** não é coberta da mesma forma pelo SDK Inner; usa o SDK do
  Leitor Facial.
- Catraca com leitor facial integrado à placa Inner pode exigir **os dois SDKs**: EasyInner
  para giro/configuração, SDK Facial para pessoas/faces.

### Coletor Urna 4 — especificação declarada

| Atributo | Valor declarado | Selo | Implicação de projeto |
|---|---|---|---|
| Modos | on-line e off-line | `BRIEFING_NAO_VERIFICADO` | — |
| Rede | Ethernet 10/100 | `BRIEFING_NAO_VERIFICADO` | Dimensionamento de switch/VLAN |
| Lista de usuários | até **15.000** | `BRIEFING_NAO_VERIFICADO` | **Não comporta 50.000 pessoas.** Ver [ADR-0017](ADR/ADR-0017-niveis-de-degradacao.md) |
| Tabelas de horário | 100 | `BRIEFING_NAO_VERIFICADO` | Limita granularidade de regras off-line |
| Memória de registros | 30.000 | `BRIEFING_NAO_VERIFICADO` | Alarme de coleta obrigatório em ~70% e ~85% |
| Urna física | média de **750 cartões** | `BRIEFING_NAO_VERIFICADO` | Em evento de 50.000 com recolhimento, exige **esvaziamento operacional recorrente** — item crítico de operação, não de software |

> **Consequência operacional que precisa estar no plano do evento:** 50.000 cartões
> recolhidos ÷ 750 por urna ≈ **67 esvaziamentos** distribuídos entre os gates coletores.
> Isso é logística de pessoal com cadeia de custódia, e o software precisa alertar, contar
> e registrar cada troca — mas não pode resolver sozinho.

## Módulos funcionais e funções nomeadas no briefing

Todas `A_VERIFICAR_NO_WRAPPER`. A ausência de assinatura aqui é intencional.

### Comunicação e diagnóstico
`DefinirTipoConexao`, `AbrirPortaComunicacao`, `FecharPortaComunicacao`,
`TestarConexaoInner`, `PingOnline`, `ReceberVersaoFirmware` (e variantes 6xx/complementares),
leitura de versão/modelo biométrico, leitura e envio de relógio, recebimento das configurações.

### Configuração geral
Padrão do cartão; quantidade fixa/variável de dígitos; tipo de leitor; leitores 1 e 2;
dois leitores Wiegand; operação on-line/off-line; acionamentos 1 e 2; botões; sensores;
teclado; mensagens; data/hora em bilhetes; mudança automática on-line/off-line; envio
atômico das configurações.

### Operação em tempo real
`ReceberDadosOnLine` (e variante com letras); origens de evento; formas de entrada;
liberação de leitor; liberação de catraca por sentido / invertida / dois sentidos; relés;
LEDs; backlight; bips; mensagens temporárias e padrão; contador de giro; acesso negado.

### Operação off-line
Lista branca/negra; inclusão e envio de usuários; horários de acesso; horários de sirene;
mensagens off-line; usuários sem digital; coleta de quantidade e de bilhetes; recuperação e
reconciliação.

### Biometria digital
Cadastro, exclusão, consulta, lista, templates, identificação/verificação; ajustes de
qualidade, segurança, sensibilidade e LFD; modelos Bio Light/variável; fluxos
request/response.

### Facial (SDK separado — WebSocket/JSON)
Cadastro/edição/exclusão/listagem paginada de usuários e faces; logs; keepalive com
resposta obrigatória; status; políticas.
**Nunca** atrás da mesma abstração do EasyInner ([ADR-0011](ADR/ADR-0011-facial-separado.md)).

## Origens de evento

Valores declarados no briefing, todos `BRIEFING_NAO_VERIFICADO` e pendentes de confirmação
no wrapper/firmware:

| Valor | Significado declarado | Uso no produto |
|---|---|---|
| 1 | Teclado | Entrada por PIN |
| 2 | Leitor 1 | Leitura de credencial |
| 3 | Leitor 2 | Leitura de credencial (sentido/leitor alternativo) |
| 4 | Sensor de catraca legado/obsoleto | Não usado para confirmar passagem |
| 5 | Fim do tempo de acionamento | Fecha janela de liberação; dispara timeout lógico |
| 6 | **Giro físico confirmado** (modelos compatíveis) | **Único** gatilho de "passagem concluída" |
| 7 | **Cartão recolhido pela urna** | Pré-condição para liberar entrada no `CollectCardThenEnter` |
| 8, 9, 10 | Sensores externos | Configurável por instalação |
| 12 | Sensor biométrico | Telemetria do leitor |
| 13 | Resposta interna biométrica | Fluxo request/response |
| 18 | Template biométrico | Cadastro/identificação |
| 20 | **Urna cheia** | Bloqueia o workflow e alerta a operação |
| 21 | QR Code | Leitura de credencial |

**Valores 11, 14, 15, 16, 17, 19 e qualquer outro não listado não são "inválidos" — são
desconhecidos.** O produto os preserva como `raw_event` íntegro, exibe no monitor ao vivo
com destaque, conta em métrica própria (`unknown_origin_total`) e **nunca descarta**
([ADR-0018](ADR/ADR-0018-eventos-desconhecidos.md)).

Números mágicos são proibidos no código: existe um tipo `EventOrigin` com
`EventOrigin.FromRaw(int) → Known(origin) | Unknown(raw)`, e o `switch` sobre ele é
exaustivo por compilação.

## Lacunas abertas (resumo)

| Lacuna | Impacto | Fecha com |
|---|---|---|
| Assinaturas, parâmetros e retornos de **todas** as funções | Adapter real não pode ser escrito | B1 (SDK no repositório) |
| Timeouts recomendados por função | Máquina de estados usa defaults conservadores provisórios | B1 + bancada |
| Códigos de retorno e seus significados (inclusive o "erro 8") | Tratamento de erro genérico demais | B1 + [Pauta Topdata](08-riscos-e-validacoes-topdata.md) |
| Matriz função × modelo × firmware mínimo | Tudo nasce desabilitado | B1 + B2 + bancada |
| Origens de evento não documentadas | Tratadas como desconhecidas (seguro, porém cego) | Pauta Topdata, item 5 |
| Modelos que emitem origem 6 | `CollectCardThenEnter` pode não fechar o ciclo | B5 + bancada |
| Função correta para liberar entrada em urna invertida | Comissionamento não pode ser automatizado | B6 + Pauta Topdata, item 7 |
| Precedência WebServer × SDK | Risco de drift silencioso de configuração | Pauta Topdata, item 9 |
