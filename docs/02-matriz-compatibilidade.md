# 02 — Matriz de compatibilidade (estado inicial: lacuna)

## Como ler este documento

> **Atualização de 24/09/2026.** O *Manual de Integração SDK Inner Acesso* (PDF oficial) e
> as FAQs do portal do integrador foram obtidos e lidos na íntegra. **A matriz deixou de
> ser uma lacuna.**

Estado atual:

| Artefato | Conteúdo | Procedência |
|---|---|---|
| [`funcoes-easyinner.csv`](compatibility-matrix/funcoes-easyinner.csv) | **58 funções** com assinatura, parâmetros, retornos, efeito físico, pré-condições, risco e teste | 51 `FONTE_PRIMARIA`, 6 parciais, 1 ambígua |
| [`origens-evento.csv`](compatibility-matrix/origens-evento.csv) | **17 origens** com o nome de enum oficial + 6 lacunas explícitas | `FONTE_PRIMARIA` |
| [`tipos-bilhete.csv`](compatibility-matrix/tipos-bilhete.csv) | Tipos de bilhete offline e da expedidora | `FONTE_PRIMARIA` |
| [`modelos.csv`](compatibility-matrix/modelos.csv) | Capacidades por modelo | ainda `NAO_ENSAIADO` — fecha só na bancada |

A leitura funcional do que isso permite construir está em
[`11-capacidades-do-sdk.md`](11-capacidades-do-sdk.md).

Na Fase 1 esses CSVs passam a ser **consumidos por teste automatizado**: um teste de
contrato falha se o código chamar uma função nativa que não esteja na matriz, ou usar uma
origem de evento sem registro. A matriz deixa de ser documentação e vira trava.

## Modelos oficialmente suportados

`FONTE_PRIMARIA` — produtos listados pela Topdata para o SDK Inner Acesso:
Catraca Fit 4, Revolution 4, Box 4, PNE 4, Coletor Inner Acesso 2, Inner Acesso, Coletor Urna.

Notas de linha (`FONTE_PRIMARIA`):

- **Linha 3** = Fit 3, Revolution 3, Box 3, placa **"Inner Acesso"** — *não é mais
  fabricada*. Menos flexível: para usar dois leitores, ambos precisam do **mesmo protocolo
  e mesma quantidade de dígitos**.
- **Linha 4** = Fit 4, Revolution 4, Box 4, placa **"Controle Catraca"** — identifica
  automaticamente o protocolo recebido em certas configurações, permitindo leitores
  diferentes lado a lado.
- **LEDs verde e vermelho só existem na Linha 3.** Na Linha 4, sinalize por display e bip.
- Identificação programática: `ReceberVersaoFirmware` devolve o campo `Linha`.
- A linha **Catracas Easy** não é coberta da mesma forma pelo SDK Inner; usa o SDK do
  Leitor Facial.
- Catraca com leitor facial integrado à placa Inner pode exigir **os dois SDKs**: EasyInner
  para giro/configuração, SDK Facial para pessoas/faces.

### Coletor Urna 4 — especificação oficial (`FONTE_PRIMARIA`)

> ⚠️ **Atenção: "Coletor Urna 4" e "Catraca com Urna Coletora" são produtos diferentes.**
> A seção 5.3 do manual do SDK descreve uma **catraca** com urna acoplada — que tem
> mecanismo de giro e emite origem 6. O **Coletor Urna 4** é um **pedestal autônomo** que
> recolhe o cartão e aciona **uma cancela ou porta por contato seco**. Ele **não tem
> mecanismo de giro**, logo **não há origem 6 para confirmar passagem física**.
> Isso tem consequência direta no workflow — ver [`04`](04-workflow-collect-card-then-enter.md).

| Atributo | Valor oficial | Implicação de projeto |
|---|---|---|
| Modos | on-line e off-line | — |
| Rede | Ethernet 10/100 Mbps (TCP/IP), IP fixo ou DHCP | Conexão **iniciada pelo equipamento** |
| Lista de usuários | **15.000**, cartões de **4 a 16 dígitos** | Não comporta 50.000 pessoas — [ADR-0017](ADR/ADR-0017-niveis-de-degradacao.md) |
| Tabelas de horário | 100 | Limita granularidade das regras off-line |
| Memória de registros | **30.000**, não volátil | Alarme de coleta em ~70% e ~85% |
| Urna física | média de **750 cartões** | ~67 esvaziamentos num evento de 50.000 |
| Leitores | Mifare 13,56 MHz **ou** proximidade 125 kHz; AbaTrack, Wiegand e Wiegand com Facility Code; **Wiegand 26, 34 e 37 bits** | Define o perfil de normalização de credencial |
| Acionamento | Saída externa de **contato seco**, relé até 3 A | Controla **cancela ou porta**, não catraca |
| Botoeira | Liberação por botão externo | Precisa entrar na auditoria como liberação manual |
| Sinalização | LEDs verde e vermelho | Sinalização de liberado/bloqueado |
| Display | 2 linhas × 16 colunas, com backlight | Limita o tamanho das mensagens |
| WebServer | HTTP, protegido por senha; importa/exporta lista de acesso e configurações | **Nunca expor à internet**; e lembrar do [ADR-0020](ADR/ADR-0020-configuracao-sempre-completa.md) |
| Firmware | Atualização por TCP/IP **em campo, sem parar o coletor** | Facilita manutenção durante evento |
| Alimentação | 90–230 Vac, 17 W | — |
| Ambiente | **Uso interno**, 0 a 45 °C | Portão externo exige abrigo |

### Funcionalidades nativas do Coletor Urna 4 que o produto deve aproveitar

O equipamento já implementa em firmware, sem depender do software:

- **"Liberação de acesso somente após recolhimento dos cartões"** — exatamente a
  sequência do nosso `CollectCardThenEnter`. O firmware garante a ordem; o software valida
  *quem* pode e registra.
- **Detecção de desistência** de cartão não recolhido pela urna.
- **Detecção de urna cheia** (origem 20).
- Recolhimento automático de cartões de proximidade.

Isso reduz o risco do workflow: a parte mais delicada (não liberar antes de recolher) é
responsabilidade do equipamento, não da nossa máquina de estados. **Ainda assim, a
sequência precisa ser confirmada em bancada** — o produto não pode assumir que o firmware
faz isso em todas as versões.

## Módulos funcionais

> Substituído pelo inventário completo em [`11-capacidades-do-sdk.md`](11-capacidades-do-sdk.md),
> com 58 funções documentadas. O resumo por módulo abaixo permanece como índice rápido.

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

Tabela oficial do manual (`FONTE_PRIMARIA`), com os nomes de enum usados pelo SDK:

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

## Lacunas abertas (resumo — pós leitura do manual)

| Lacuna | Impacto | Fecha com |
|---|---|---|
| Assinaturas de 6 funções (`DefinirTipoConexao`, `LiberarLeitor`, `InserirHorarioAcesso`, `EnviarHorariosAcesso`, `EnviarConfiguracoesFuncoes`, `EnviarDigitalUsuarioBio`) | Essas funções ficam desabilitadas | **Pacote de exemplos C#** do portal |
| Valores de `RET_SEM_EVENTOS` e `RET_SEM_BILHETES` | Laços de polling e coleta usam comparação provisória | **Pacote de exemplos C#** |
| Tabela de códigos de `Linha`: 8 códigos para 7 descrições (PDF desalinhado) | Capability discovery por firmware fica parcial | Exemplos ou suporte |
| Origens 11, 14–17, 19 | Tratadas como desconhecidas (seguro, porém cego) | Pauta Topdata, item 4 |
| Quais modelos emitem origem 6 | Passo final do fluxo da urna | Bancada + pauta, item 11 |
| Capacidade real de lista e de bilhetes **por modelo** | Política de subconjunto do nível T2 | Bancada |
| Tipos de bilhete `003/004/005` colidindo com `3/4/5` | Parsing da expedidora | Pauta, item 9 |
| Faixa de `DefinirQuantidadeDigitosCartao` (4–16 ou 1–16) | Validação de parâmetro | Pauta, item 8 |
| **A DLL carrega em processo .NET moderno?** | Define a stack do worker | Bench `HIL-STACK-01` |
