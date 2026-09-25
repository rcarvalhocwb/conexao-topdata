# 22 — O sistema do Supabase: onde ele entra no desenho

> **O que se sabe, pelo usuário:** existe um sistema feito no Lovable, com banco no
> Supabase, que **recebe os payloads do Zet**, **recebe informações da catraca** e
> **cadastra os cartões**. O código ainda não foi visto — o usuário está verificando se
> consegue baixá-lo.
>
> Este documento é o que dá para dizer **antes** de ver o código: onde ele se encaixa, o
> que muda, e o que precisa ser respondido.

---

## 1. Onde ele se encaixa

Nas três memórias de [`15`](15-integracao-e-sincronizacao.md), o sistema do Supabase é a
**nuvem** — o dono do cadastro:

```
  ┌──────────────────────────────┐
  │  Supabase (Lovable)          │  recebe o Zet · cadastra cartões · controle
  │  NUVEM — dono do CADASTRO    │
  └──────────────┬───────────────┘
                 │  ingressos e cartões descem · usos e passagens sobem
  ┌──────────────┴───────────────┐
  │  Edge (este produto)         │  decide na porta, sem internet
  │  BORDA — dono do FATO        │
  └──────────────┬───────────────┘
                 │
          TopFit 4 × 5
```

**A boa notícia:** parte do que foi construído do lado da nuvem pode já existir lá. Se o
webhook `apizet.ruailuminada.com/webhook` já aponta para uma função do Supabase, o relé
([ADR-0022](ADR/ADR-0022-rele-de-webhook.md)) pode não ser necessário — a função do
Supabase faz o papel dele.

**O que não muda:** a decisão na porta continua local. A catraca não pergunta nada ao
Supabase na hora do giro. Se a internet cair no meio do evento, a borda continua decidindo
com o que já desceu.

---

## 2. As perguntas, em ordem de impacto

### 1. Como o sistema recebe as informações da catraca **hoje**?

**É a pergunta que pode mudar o ensaio de bancada.** Há dois jeitos possíveis, e eles não
convivem bem:

| Se hoje é… | Consequência |
|---|---|
| A catraca manda direto para o Supabase, pela internet (modo WebServer / HTTP da Catraca 4) | Para o ensaio pela `EasyInner.dll`, a catraca precisa ser **reapontada para o PC local**. E fica o risco de segurança: a regra do projeto é **nunca expor a catraca à internet** |
| Um programa no PC local lê a catraca e manda para o Supabase | Esse programa e o nosso worker **disputam a mesma catraca** — só um pode ser o servidor dela |
| Ainda não recebe — está previsto | Não há conflito |

### 2. O webhook do Zet cai numa função do Supabase?

Se sim, o relé fica redundante — desde que essa função **guarde o payload bruto antes de
interpretar** e nunca descarte o que não entender. Ver ADR-0022, "o relé não sabe o que é
um ingresso".

### 3. O cadastro de cartões acontece onde, e funciona sem internet?

Se a venda de balcão é feita no aplicativo do Lovable, **ela para quando a internet cai**.
A borda tem venda de balcão local (`VenderNoBalcao`), que funciona sem internet. É preciso
decidir quem é o balcão no dia do evento.

### 4. Para onde vai o aviso de uso?

A volta ([`18`](18-contrato-do-webhook.md), seção 10) pode ir direto para o Zet ou passar
pelo Supabase. Se passar pelo Supabase, é ele quem dá a baixa no Zet — e a borda só precisa
de um conector, não de dois.

---

## 3. O que preciso do código baixado

| Arquivo | Para quê |
|---|---|
| `supabase/migrations/*.sql` | O esquema: tabelas de ingresso, cartão, evento de catraca |
| `supabase/functions/*` | A função que recebe o webhook do Zet, e a que recebe a catraca |
| `src/integrations/supabase/types.ts` | Os tipos que o Lovable gera a partir do banco — o retrato mais fiel do esquema |
| O `README`, se houver | Como foi pensado |

**Não preciso do `.env`, e ele não deve vir.** Se vier, eu não uso.

**Este repositório é público.** O código do Lovable não entra nele — eu leio, extraio o
que a integração precisa (nomes de tabela, formato de payload), e só isso vira código aqui.
Antes de qualquer coisa, confiro se há chave ou senha no que chegou.

---

## 4. As regras que valem para a integração, qualquer que seja o esquema

Vêm do escopo original do projeto e não dependem do que o código mostrar:

1. **A borda nunca guarda a chave `service_role` do Supabase.** Ela dá acesso total ao
   banco, e a máquina local fica numa rede com catracas. A borda usa uma credencial
   própria, com acesso só ao que precisa — por política de linha (RLS) ou por uma função
   do Supabase com token dedicado.
2. **A rede das catracas nunca alcança o Supabase.** Quem fala com a nuvem é o PC, pela
   rede dele.
3. **A borda puxa; a nuvem não empurra para ela.** A máquina local não abre porta para a
   internet ([ADR-0022](ADR/ADR-0022-rele-de-webhook.md)).
4. **A decisão de acesso nunca consulta a nuvem.** O Supabase fora do ar não barra
   ninguém na porta.

---

## 5. O que muda no código, provavelmente

Sem ter visto o esquema, a expectativa é pequena — foi para isso que as portas existem:

- Uma implementação de `IFonteDeIngressos` que puxa do Supabase, no lugar da `FonteDeRelay`.
  O laço de ingestão, o cursor e a idempotência não mudam.
- Uma configuração do `ConectorRest` apontando para o Supabase, se a volta passar por lá.
- Talvez a origem dos cartões vendidos no balcão, se o balcão continuar sendo o Lovable.

O ensaio de bancada ([`21`](21-roteiro-da-bancada.md)) **não depende de nada disto**: ele
usa ingressos de um arquivo, e pode acontecer antes.
