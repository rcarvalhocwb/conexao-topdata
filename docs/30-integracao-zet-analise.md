# 30 — A Zet de verdade: análise dos webhooks e da planilha de vendas (edição 2025)

> Material recebido em 29/09/2026:
> - o README da integração atual (Zet → Cloudflare → Supabase);
> - o backup dos webhooks (27.641 entregas);
> - a planilha `data.xlsx`.
>
> **Os arquivos têm nome, CPF, e-mail e telefone de cerca de 27 mil compradores.** Eles
> **não** entram neste repositório (que é público) e só foram lidos para contagens
> agregadas. Este documento também não reproduz o endereço interno do Supabase citado no
> README. Guarde esses arquivos com acesso restrito e prazo de descarte (LGPD).

## 1. Como a Zet conversa hoje

```
Compre no Zet ──POST JSON──► Cloudflare Worker (assina HMAC, repassa o IP)
                               └──► Supabase Edge Function ──► webhook_logs (bruto)
                                                               └─► zet_sales_master, pedidos, painéis
```

A catraca **não recebe nada** desse caminho hoje. Duas ações chegam:
- `CP`: compra paga;
- `ES`: estorno.

Cada compra traz os ingressos em `eventTicketCodes[]`, e cada ingresso tem:
- voucher (o QR);
- data do evento;
- sessão;
- tipo (`description`).

## 2. O que os dados mostram

| Achado | Número (edição 2025) | O que significa para a catraca |
|---|---|---|
| Entregas | 27.398 compras, 243 estornos | — |
| Ingressos distintos | 79.522 (84.155 com reenvios) | A base local precisa de ~80 mil QRs por edição |
| **Comprados no mesmo dia da sessão** | **48.261 (61%)** | O QR on-line precisa chegar à catraca **durante** o evento, em minutos. Não dá para "carregar antes e pronto" |
| Datas do evento | 52 dias (15/11/2025 a 04/01/2026) | Cada ingresso vale para **um** dia |
| Pico por dia | 4.215 ingressos (06/12) | Dimensionamento (B7) |
| Pico por sessão | 1.519 ingressos (18/12, 20h15) | ~1.500 pessoas na mesma janela de horário |
| Sessões | 18h15, 19h15, 20h15, 21h15, 22h15 (e variações) | Se a sessão restringe a entrada é decisão da organização (§5) |
| Voucher | 13 dígitos; em 79.521 de 79.522, "1" + nº do pedido + 6 dígitos; nenhum começa com zero | Formato estável e verificável; zeros à esquerda não são problema **nesta** fonte |
| Reenvios da mesma compra | 1.254 pedidos | Ingestão idempotente pelo id do ingresso (já temos) |
| Estornos | 879 vouchers; 183 sem compra recebida antes | Estorno que chega antes da compra precisa barrar também (testado) |
| Campo `used` da Zet | 99,9% "NAO" | A Zet não sabe quem entrou: **a fonte da verdade do uso é a catraca** |
| Tipos (`description`) | ~20 grafias (Inteira, Meia-entrada, Crianças de 06 a 12, Idosos, Solidário + 1kg, Doador/ID Jovem…, Professores/Saúde…, PCD/Autista, Acompanhante PCD, Clube Gazeta, testes) | Grafias diferentes para o mesmo tipo: o relatório por tipo precisa de um mapeamento (fase 3) |
| Assinatura HMAC | 27.368 "bypassed", 1 válida | Na prática, nenhuma entrega é autenticada hoje |
| Falhas do processamento atual | 1.257 duplicatas puladas, 67 erros de conexão, 37 "CPF nulo", 25 de validação | Exigir dado pessoal derruba ingresso válido: o nosso tradutor não lê dado pessoal |

### A planilha `data.xlsx` não é a lista de cartões

É o **relatório de vendas da Zet**: 27.451 pedidos com comprador, valores e forma de
pagamento. **Não tem voucher nem cartão**, então não alimenta a catraca. Cruzada com os
webhooks, ela mede a cobertura:

| Forma de pagamento | Pedidos na planilha | Sem webhook |
|---|---|---|
| PIX | 19.534 | 287 |
| Crédito | 6.719 | 117 |
| **Débito na maquininha** | 563 | **563 (todos)** |
| **Crédito na maquininha** | 353 | **353 (todos)** |
| **PIX na maquininha** | 245 | **245 (todos)** |
| **Cortesia** | 37 | **37 (todos)** |
| **Total** | 27.451 | **1.602 (5,8%)** |

**As vendas na bilheteria física da Zet (maquininha) e as cortesias nunca chegam por
webhook.** Quem comprar assim terá um QR que a catraca não conhece, a menos que exista
outra fonte. Das vendas on-line, 404 (1,5%) também se perderam.

## 3. O que foi construído agora

`TradutorDaZet` (`src/Sync.Connectors.Rest`) lê o webhook real da Zet, sem precisar de
contrato novo:
- **Compra e estorno:**
  - a compra cria um ingresso por voucher;
  - o estorno cancela pela mesma referência (o `id` do ingresso, igual nas duas);
  - cancelamento é definitivo: a compra reenviada depois não reativa.
- **Evento:** só entra o evento configurado (538 em 2025; muda a cada edição). Outros
  eventos da conta, inclusive os de teste, são ignorados.
- **Voucher:** tem de vir como texto. Número JSON é recusado, porque destruiria zeros à
  esquerda.
- **Validade:** o **dia de operação** da data do ingresso, no horário de Brasília, das 06:00
  do dia às 06:00 do seguinte. A hora de corte é configurável (pendente E9 do docs/29).
- **Categoria:** a `description` da Zet, com os espaços normalizados. A sessão e o setor
  ("Ingresso") não restringem nada.
- **Dados pessoais:** nome, e-mail, telefone e CPF não são lidos (minimização). Um CPF
  nulo não derruba o ingresso.

**Testes:** 16 de unidade com payloads sintéticos no formato real. Mais 3 de integração
contra o banco:
- compra → estorno → reenvio continua barrado;
- o ingresso só vale no seu dia (vale à 01h do dia seguinte, antes do corte);
- estorno antes da compra também barra.

A regra de validade foi quebrada de propósito, e o teste reprovou.

## 4. Como ligar a Zet à catraca (a decidir)

| Caminho | Como | A favor | Contra |
|---|---|---|---|
| **A — Worker repassa (recomendado)** | O Cloudflare Worker, que já recebe tudo, **também** encaminha o corpo bruto para o nosso relé (`Relay.Ingressos`, docs/18 e ADR-0022). A borda puxa do relé com `FonteDeRelay` + `TradutorDaZet` | Mudança pequena num código que vocês controlam; não depende do Supabase no dia; chega em segundos | Precisa hospedar o relé e mexer no Worker |
| B — Borda lê o que o Supabase já guardou | Uma função restrita devolve os `webhook_logs` brutos desde um cursor; a borda puxa com o mesmo tradutor | Reaproveita o que existe | Depende do Supabase de produção (dados pessoais) durante o evento; exige uma função nova lá |
| C — Arquivo antes de cada dia | Importar um JSONL no formato do backup (o mesmo tradutor) | Contingência sem internet | Não cobre os 61% comprados no próprio dia |

Qualquer caminho usa o mesmo `TradutorDaZet`. Nenhum substitui a pergunta abaixo sobre a
maquininha e as cortesias.

## 5. O que precisa de resposta

| # | Pergunta | Para quem |
|---|---|---|
| Z1 | Como a catraca conhece os **ingressos da maquininha e as cortesias**? A Zet tem API ou exportação **com voucher**? | Zet |
| Z2 | Caminho A, B ou C (§4)? Se A: podemos alterar o Worker e onde hospedar o relé? | você |
| Z3 | Qual o `event.id` da edição 2026, e se o formato do voucher se mantém | Zet |
| Z4 | Um ingresso da sessão 19h15 pode entrar às 20h15? Há tolerância? | organização |
| Z5 | QR válido no formato Zet que ainda não chegou (comprado há minutos): recusa e manda ao balcão de conferência, ou libera e confere depois? (B12 do docs/01) | organização |
| Z6 | Agrupamento dos ~20 tipos da Zet nos tipos do relatório (ex.: as três grafias de "Doador/ID Jovem" viram um só) | organização |
| Z7 | Retenção dos arquivos com dados pessoais (backup e planilha) | responsável LGPD |
| Z8 | Ligar `WEBHOOK_SIGNATURE_ENFORCEMENT=strict` só depois de a assinatura funcionar: hoje 99,9% chegam sem assinatura válida | você |

## 6. Dimensionamento com os números reais (estimativa)

Na sessão de pico chegaram 1.519 pessoas com ingresso on-line. Com ciclo entre 4 e 6 s por
passagem (docs/14 §7.2), uma catraca atende de 600 a 900 pessoas/h. Chegada espalhada em uma hora pede
**2 a 3 catracas só para esse fluxo**; concentrada em 30 minutos, **4 a 5**. Isso sem contar
a bilheteria local e a urna. É a resposta de B7 que faltava. O ciclo real sai da bancada (docs/21).
