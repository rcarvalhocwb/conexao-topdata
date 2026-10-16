# 61 — Urna, leitores e teclado: análise da bancada e o que mudou

> Bancada do dono do produto, 15–16/10/2026, Catraca Fit (Inner 1, linha 19, FW 6.42.1), urna e leitor
> da tampa frontal:
>
> 1. "Não consegui desativar leitores que não quero utilizar. O leitor da tampa frontal (leitor 1) quero
>    que seja somente para cartões administrativos ou de funcionários; a urna é para os cartões do
>    evento (os 2.243)."
> 2. "Ao colocar o cartão [na urna] ele não ativa o relé que abre a passagem para o cartão cair: a
>    pessoa passa e leva o cartão embora. A passagem deveria ser liberada após o cartão cair na urna."
> 3. "O teclado eu poderia ter a opção de desativar ou utilizar somente para uma situação específica."

## 1. Fontes consultadas

| Fonte | O que ela diz | Selo |
|---|---|---|
| Manual de Integração SDK Inner Acesso (PDF do portal do integrador), §5.3 "Catraca com Urna Coletora" e fluxo 8 "Acesso Autorizado com Urna e Monitoração de Depósito" | Validou → aciona o relé da urna (`AcionarRele2`) → espera a **origem 7** (cartão recolhido) → **só então** `LiberarCatraca` → origem 6 (giro). Origem 5 = não depositou; origem 20 = urna cheia; nos dois casos, `NaoRegistrarAcesso` e mensagem de negado | FONTE_PRIMARIA |
| SDK EasyInner 6.0.2.0, exemplo oficial em **Java** (`OnlineController.java`) | `PASSO_ESTADO_ENVIA_MSG_URNA`: mensagem "DEPOSITE O CARTAO" e `AcionarRele2`; `PASSO_ESTADO_MONITORA_URNA`: origem 7 → liberar; 5 → negado; 20 → urna cheia. Com **dois leitores**, só a leitura do **leitor 2** vai para a urna; o leitor 1 libera direto | FONTE_PRIMARIA |
| SDK EasyInner 6.0.2.0, exemplo oficial em **C#** (`FrmOnlineController.cs`) | Catraca com urna: `ConfigurarAcionamento2(ACIONA_REGISTRO_SAIDA, 5)` (urna na saída), leitor 2 só saída; mesmos estados `ENVIAR_MSG_URNA` → `MONITORA_URNA` → `LIBERAR_CATRACA` | FONTE_PRIMARIA |
| SDK 6.0.2.0, `Enumeradores.cs` dos exemplos | `EnviarFormasEntradasOnLine` (formas de entrada on-line): 0 nenhuma · 1 teclado · 2 leitor 1 · 3 leitor 2 · 4 teclado+L1 · 5 teclado+L2 · 6 L1+L2 · **7 teclado+L1+L2** · 10–14 e 100–105 com biometria | FONTE_PRIMARIA (fecha a T26) |
| Manual do Web Server da Catraca 4, §7.1.3 e §7.1.6 "Possui urna" | Leitores 1 e 2 configuráveis como Desabilitado/Entrada/Saída; "Acionamento 2 é utilizado quando a catraca utilizar urna"; com urna, "o acesso somente será liberado se ocorrer o recolhimento do cartão, que é realizado através da configuração do Acionamento 2"; teclado tem opção "Habilitado" | FONTE_PRIMARIA |
| **Manual de Desenvolvimento EasyInner** (178 páginas, dentro do SDK em `vendor/topdata`) | `ConfigurarAcionamento1/2`: 0 não utilizado · 1 entrada/saída · 2 entrada (catraca entrada) · 3 saída (catraca saída) · 4 sirene · 5 revista · 6–9 catraca liberada. `AcionarRele2`: "Este comando **não deve ser utilizado nas catracas**". `DesabilitarRele2` existe. `HabilitarTeclado` só vale no off-line | FONTE_PRIMARIA (contradiz o exemplo Java, ver §7) |
| **Diagrama oficial "Catraca Com Urna"** (no SDK) | Validar Acesso → Valida Urna Cheia → Envia Msg Urna → Monitora Urna (origem 7) → Liberar Catraca → Monitora Giro | FONTE_PRIMARIA |
| **Middleware que operou em 2025** (`Middleware_topdata_supabase`, TopFit4, urna na **entrada**) | Leitor 1 e 2 = 1; Acionamento 1 = (1, 15); **Acionamento 2 = (3, 15)**; forma de entrada on-line **6** (sem teclado); `AcionarRele2` → "DEPOSITE O CARTAO" → espera a origem 7 por **7 s** → `DesabilitarRele2` + `LiberarCatracaEntrada`; depois do giro, `DesabilitarRele2` de novo e rearme. Checklist da equipe: "se o Relé2 ficar acionado, a urna fica aberta e o próximo cartão não é lido" | EVIDENCIA_DE_CAMPO (funcionou num evento real) |
| Suporte Topdata, "catraca Revolution com urna… mecanismo não volta" | Após a leitura liberada, o **solenoide da guia** abre; o cartão cai, passa pelo **sensor 3** (óptico) e a placa fecha a guia | FONTE_PRIMARIA |

O **Manual de Integração** que está em `vendor/topdata` é o mesmo documento do portal do integrador
(mesmo conteúdo, conferido seção por seção). Os arquivos do SDK foram lidos sem executar nada. Decisão
do dono do produto (16/10/2026): os arquivos da Topdata ficam em `vendor/topdata` e o SDK vai direto no
instalador (próxima etapa; hoje o CI ainda recusa a DLL no instalador público, docs/60).

## 2. Diagnóstico

### 2.1 A urna não recolhia: o relé 2 estava desligado e o giro saía na leitura

- `PadroesDeFabrica.TopFit4` mandava `ConfigurarAcionamento2(0, 0)` — relé 2 **não utilizado** — com a
  nota "a função que faz a urna recolher o cartão não está documentada; Etapa A.11, bloqueada por
  bancada".
- O laço tratava a leitura da fenda (origem 3) como qualquer outra: aprovou → `LiberarCatracaEntrada`
  → esperava o giro. A guia da urna nunca abria e o braço liberava com o cartão na mão da pessoa —
  exatamente o que a bancada viu.
- A documentação oficial (§1) define o fluxo e ele é implementável com o que o SDK expõe.

### 2.2 Não dava para desligar leitores: estava escondido ou não existia

- O **leitor da frente** (leitor 1) só aparecia em Parametrização no **modo técnico**, e o painel não
  dizia para que ele serve.
- O **teclado** não tinha opção nenhuma. O `HabilitarTeclado(0)` vai na configuração, mas o rearme on-line
  usa `EnviarFormasEntradasOnLine` com **forma 7 = teclado + leitor 1 + leitor 2**: o teclado continua
  aceitando digitação no modo on-line (é o que a bancada percebeu).
- A regra "cartão do evento só na urna" já existia (`FORA_DA_URNA`), mas não havia "leitor da frente só
  da equipe" nem proteção contra o crachá colocado na urna (que seria recolhido).

## 3. O que mudou (versão desta entrega)

### 3.1 Urna — o fluxo oficial

```
leitura na fenda (origem 3) ─ aprovada ─▶ "Deposite o cartao" ─▶ AcionarRele2 ─▶ espera
                                                                                 │
                     origem 7 (caiu) ─▶ LiberarCatraca ─▶ origem 6 (passou) ─────┤
                     origem 5 (não caiu) ─▶ "Cartao nao caiu. Tente de novo" ────┤ uso devolvido
                     origem 20 (cheia)   ─▶ "Urna cheia. Procure a equipe" ──────┤ uso devolvido
                     prazo de 7 s        ─▶ como a origem 5 ────────────────────┘ uso devolvido

DesabilitarRele2 (fecha a urna) antes de liberar ou negar, e de novo antes de rearmar o leitor.
```

- **Relé 2 ligado por padrão quando a urna está ligada:** função **3** e **15 s**, os valores do
  middleware que operou em 2025 numa TopFit4 com a urna na entrada (§7). Urna desligada nas
  configurações → relé 2 desligado junto (sem leitor da fenda a urna nunca abriria).
- **Fechar a urna:** `DesabilitarRele2` antes de liberar o giro ou negar, e de novo ao rearmar o leitor
  (como o middleware; sem isso a urna fica aberta e o próximo cartão não é lido). Falha nesse comando é
  registrada e o fluxo segue.
- **Espera pelo depósito: 7 s** (`DeviceStateMachine.EsperaPeloDeposito`), o prazo do middleware.
- **Estados novos** na máquina: `AcionarUrna` e `MonitoraUrna`, com os nomes do manual. O giro só sai
  com a origem 7. Uma leitura nova com a urna aberta fica registrada e não é decidida.
- **Desistência devolve o uso:** a catraca não liberou o braço, ninguém passou, o cartão continua com a
  pessoa. A tentativa vira `estornado` (com a causa), o ingresso recebe o uso de volta, o estorno fica
  em `ticket_use_refund` como feito pelo `sistema`, e os avisos daquela leitura que ainda não saíram
  para a nuvem e o provedor saem da fila. O que já saiu fica como foi (o contrato do retorno não tem
  estorno, docs/18 §10).
- **Mensagens no display** (sem acento, até 32 letras): "Deposite o cartao", "Cartao nao caiu. Tente de
  novo", "Urna cheia. Procure a equipe", "Use a urna", "Use o leitor da frente", "Leitor so para equipe",
  "Leitor desligado". Antes toda negação dizia "Acesso nao autorizado".
- **Escape sem versão nova:** chaves técnicas `catraca.funcao_do_rele_da_urna` (0 a 9; **0 volta ao
  comportamento antigo**) e `catraca.tempo_da_urna` (1 a 50 s).

### 3.2 Para que serve cada entrada

Configurações › **Padrão das catracas**:

| Entrada | Opções | Padrão | O que faz |
|---|---|---|---|
| Leitor da frente (tampa) | Equipe e ingressos (QR) · **Só a equipe** · Desligado | Equipe e ingressos | "Só a equipe": ingresso nega com "Leitor so para equipe". "Desligado": nega tudo **e** vai à catraca como leitor 1 desabilitado |
| Urna | Ligada · Desligada (caixa que já existia) | Ligada | Só cartão do evento. **Crachá de pessoa na fenda é recusado** ("Use o leitor da frente") para não ser engolido |
| Teclado | **Desligado** · Só a equipe | Desligado | Desligado: qualquer digitação nega ("Leitor desligado"). Só a equipe: aceita o código digitado de uma pessoa do cadastro; ingresso digitado nega |

A regra "cartão do evento só na urna" (`SomenteNaUrna` do tipo de cartão, padrão dos 2.243 cartões da
carga inicial) continua valendo no modo "Equipe e ingressos": cartão do evento no leitor da frente nega
com "Use a urna".

**Para o caso da bancada** ("frente só para administrativos e funcionários, urna para os 2.243, teclado
desligado"): leitor da frente = **Só a equipe**, urna = **Ligada**, teclado = **Desligado**. Depois
**Aplicar agora nas catracas**.

### 3.3 Teclado e leitores no nível da catraca

O rearme on-line (`EnviarFormasEntradasOnLine`) agora manda a forma **calculada pelas entradas
ligadas** (tabela oficial, §1): leitor 1, leitor 2 e teclado. Padrão (frente ligada, urna ligada,
teclado desligado) = forma **6**, a mesma do middleware de 2025: a catraca **nem aceita a digitação**.
Teclado "Só a equipe" → 7; frente desligada → 3 (só a urna). A negação pelo software continua valendo
por cima. Quem precisar fixar a forma à mão ainda tem a chave `catraca.enviar_formas_de_entrada` = 1 e
"Rearme do leitor" na Parametrização (modo técnico).

## 4. Testes

- `UrnaRecolheAntesDeLiberarTests` (laço + decisor + base de verdade, simulador): cartão cai → só então
  libera; não cai → não libera, uso devolvido, sem aviso na fila; urna muda → prazo nega e devolve;
  urna cheia → nega, avisa e devolve; cartão do evento na frente → "Use a urna"; relé da urna em 0 →
  comportamento antigo.
- `UsoDasEntradasTests`: padrão; frente só da equipe; frente desligada (inclusive leitor 1 = 0 na
  catraca); teclado só da equipe; crachá na urna; valor ilegível volta ao padrão.
- Ajustados os testes que fixavam o relé 2 em 0 (padrão de fábrica, versão da configuração, sequência
  oficial, cobertura dos campos, mapa de giro ponta a ponta).

## 5. O que só a bancada confirma

| # | Pergunta | Como ver | Se falhar |
|---|---|---|---|
| U0 | **Antes de tudo:** a pasta do programa das catracas tem **EasyInner.dll e Inner.dll**? Sem a Inner.dll a porta não abre (retorno 11) e o painel diz "falta a Inner.dll". Copiar pelo Assistente (Localizar EasyInner.dll, pasta "Porta Fixa" do SDK) | Painel: a catraca aparece ao conectar; registro: "porta aberta" | Se o painel mostrar a causa, copiar a DLL que ela nomear |
| U1 | Com o relé 2 em **(3, 15 s)**, `AcionarRele2` abre a guia e o cartão cai? | Cartão do evento na fenda: o display pede o depósito, ouve-se o solenoide, o cartão cai, o braço libera | Trocar `catraca.funcao_do_rele_da_urna` para **2** e depois **1**; anotar qual abre |
| U2 | A origem 7 chega depois que o cartão cai? | Registro do worker: "cartão recolhido pela urna (origem 7) — liberando o giro" | Se não chegar, o prazo nega e devolve; conferir o jumper ST3 e o cabo do sensor 3 (artigo do suporte, §1) |
| U3 | Sem depositar (puxar o cartão de volta): vem a origem 5 e nada libera? | Display "Cartao nao caiu…", painel com a tentativa estornada | — |
| U4 | Crachá na fenda é recusado sem abrir a urna? | "Use o leitor da frente" | — |
| U5 | Frente "Só a equipe": cartão do evento na tampa nega; crachá passa | Painel ao vivo | — |
| U6 | Teclado desligado: a catraca não aceita digitação | Teclas sem eco nem leitura no registro | Se aceitar, o software ainda nega com "Leitor desligado"; anotar |
| U8 | Depois de um ciclo de urna, o cartão seguinte é lido? | Segunda pessoa na fenda logo depois | Se não ler, o `DesabilitarRele2` não fechou: anotar o registro "urna fechada" |
| U7 | A catraca libera sozinha depois que o cartão cai, além do nosso `LiberarCatraca`? | Dois giros possíveis com um cartão? | Se o firmware já liberar, o nosso comando é redundante e inofensivo; anotar para a Topdata |

## 6. Limites conhecidos

- **Uso devolvido que já subiu:** se a nuvem ou o provedor já receberam o uso antes da desistência
  (janela de segundos), lá ele fica. A tentativa local diz "estornado" com a causa.
- **Cartão caiu e o aviso se perdeu** (queda de rede no instante): o uso é devolvido e o cartão está na
  urna. A conferência da urna (docs/04 §6) mostra a diferença.
- **`AcionarRele2` em catraca:** o Manual de Desenvolvimento diz para não usar; o exemplo Java e o
  middleware de 2025 usaram e funcionou. Seguimos a evidência de campo; U1 confirma.
- **Origem 20 fora da urna aberta** (catraca em repouso) é registrada como sinal e marca "urna cheia" no
  slot; o alerta no painel é o próximo passo.

## 7. O que veio do middleware que operou em 2025

O dono do produto enviou o middleware antigo ("o que estava funcionando no ano passado"). Ele resolve
as três dúvidas que a documentação deixava abertas, e o produto passou a fazer igual:

| Ponto | Antes desta versão | Middleware de 2025 = agora |
|---|---|---|
| Relé 2 (função, tempo) | (2, 5 s), pelo Web Server | **(3, 15 s)** |
| Fechar a urna | não fechava | `DesabilitarRele2` antes de liberar/negar e depois do giro |
| Espera pelo cartão | tempo do relé 2 + 3 s | **7 s** |
| Forma de entrada on-line | 7 (teclado + leitores) | **6** (leitores, sem teclado) quando o teclado está desligado |
| Sequência | aciona → espera 7 → libera | igual: `AcionarRele2` → "DEPOSITE O CARTAO" → origem 7 → libera |

Uma lição do código antigo: a origem 7 pode chegar logo depois do `AcionarRele2`; o laço já aceita a
origem 7 em qualquer momento da espera. Nada de configuração, chave ou dado do middleware (inclusive a
chave do Supabase) foi copiado para o produto.

