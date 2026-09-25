# 16 — Vários provedores de ingresso num mesmo evento

> **O cenário:** três sites vendem ingresso para o mesmo evento. Cada um emite o QR do
> seu jeito. Os ingressos precisam chegar à base local quase em tempo real, a catraca
> precisa validar sem internet, cada site precisa saber que o ingresso dele foi usado, e
> no fim alguém tem de prestar contas dos três.
>
> São quatro problemas diferentes, e o terceiro é o que costuma quebrar depois — porque é
> onde o dinheiro está.

---

## 1. O problema que ninguém vê antes de acontecer

A catraca lê **uma string**. Ela não sabe de qual site veio o ingresso, não sabe quanto
custou, não sabe se o comprador é VIP. Ela entrega um texto e espera uma resposta em
milissegundos.

Disso decorre a decisão mais importante deste documento:

> **O QR é único no evento inteiro, não por provedor.**

Se dois sites emitirem o mesmo código, o portão não tem como desempatar. Não existe
solução no momento da leitura — não dá para perguntar "de quem é este?" com cem pessoas
na fila. Então o conflito tem de estourar **na ingestão**, com hora, provedor e referência
dos dois lados.

É um índice único no banco, e ele recusa o ingresso conflitante sem derrubar o lote: cinco
mil ingressos bons não podem deixar de entrar porque um veio errado.

```
ux_ticket_qr  UNIQUE (qr_normalized)      ← a trava
ux_ticket_provedor_ref  UNIQUE (provider_id, external_ref)   ← reenvio é atualização
```

A segunda chave é o que torna a ingestão **repetível**. Todo provedor vai reenviar: por
retentativa, por rotina noturna, porque o operador clicou duas vezes. Reenviar o mesmo
ingresso atualiza; não duplica.

### Cada provedor tem o seu formato

Um manda `ABC-123456`, outro manda `abc123456`, o terceiro põe prefixo de lote. Por isso o
perfil de normalização é **por provedor**, não do sistema. O que nenhum perfil pode fazer
é remover zeros à esquerda — é a causa clássica de ingresso válido recusado
([ADR-0008](ADR/ADR-0008-credencial-como-string.md)).

---

## 2. Como os ingressos chegam até a máquina local

Aqui há uma restrição de segurança que muda o desenho, e que costuma ser descoberta tarde:

> **A máquina local está na mesma rede das catracas. Ela não pode ser exposta à internet.**

Abrir uma porta de entrada para três sites receberem *webhook* significa abrir um caminho
da internet pública até a VLAN dos equipamentos. Isso não se faz.

Então a borda **puxa**, não recebe:

```
    ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
    │  Bilheteria  │   │  Bilheteria  │   │  Bilheteria  │
    │      A       │   │      B       │   │      C       │
    └──────┬───────┘   └──────┬───────┘   └──────┬───────┘
           │  HTTPS de SAÍDA, por cursor         │
           └──────────────┬─────────────────────-┘
                          ▼
              ┌───────────────────────────┐
              │  Edge — máquina local      │  nenhuma porta de entrada
              │  SQLite · decide sozinho   │  na internet
              └────────────┬──────────────┘
                           │  VLAN isolada
                    catracas / coletores
```

Duas formas, e as duas são necessárias:

- **Consulta por cursor**, de segundos: "o que mudou desde a marca X?". Dá a latência de
  quase tempo real sem abrir nada.
- **Varredura completa**, periódica: pega o que a consulta incremental perdeu — e ela
  perde, porque provedor reprocessa venda, corrige lote e às vezes publica fora de ordem.

Quando o cliente exige *webhook* de verdade, o único desenho aceitável é um **relé na
nuvem**: o site entrega lá, a borda puxa de lá. A borda continua sem porta aberta.

> **B10 em aberto:** qual API cada um dos três provedores oferece, e qual a latência real
> entre a venda e o ingresso ficar disponível para consulta. Isso determina se um ingresso
> comprado na fila da catraca chega a tempo.

---

## 3. A validação na catraca

Nada disso encosta na internet. A pessoa apresenta o QR, e o que acontece é uma instrução
de banco:

```sql
UPDATE ticket
SET used_count = used_count + 1, ...
WHERE qr_normalized = $qr
  AND status = 'valido'
  AND used_count < max_uses
  AND (valid_from IS NULL OR valid_from <= $agora)
  AND (valid_to   IS NULL OR valid_to   >= $agora)
  AND EXISTS (provedor habilitado);
```

Afetou uma linha: entrou. Afetou zero: só então se pergunta **por quê** — e essa pergunta
é barata, porque só acontece no caminho da negativa.

Os motivos são um catálogo fechado, e cada um vira uma linha do relatório final:

| Motivo | O que significa na prática |
|---|---|
| `Consumido` | Entrou |
| `Desconhecido` | **O QR não existe aqui.** Falsificação — ou ingresso vendido que nunca chegou |
| `UsosEsgotados` | Já entrou. Print compartilhado, ou tentativa de reentrada |
| `Cancelado` | Estorno ou troca antes do uso |
| `Bloqueado` | Bloqueio da operação |
| `ForaDaJanela` | Ingresso de outro dia, ou de outro horário |
| `ProvedorDesabilitado` | A operação desligou aquela bilheteria |

**Toda tentativa vira linha, inclusive a negada.** Sem isso não existe prestação de
contas: sabe-se quem entrou e nunca quem tentou — e é justamente quem tentou que explica a
diferença entre o que o site vendeu e o que passou pela catraca.

### O print compartilhado em dois portões ao mesmo tempo

Oito catracas lendo o mesmo QR no mesmo segundo: exatamente uma consome, sete registram
`UsosEsgotados`. Sem trava distribuída, sem consultar a nuvem, sem combinar nada entre os
portões.

---

## 4. O retorno ao site: o que significa "utilizado"

Esta é a pergunta cara, e ela não é técnica.

**Autorizar não é passar.** O sistema sabe a diferença: a liberação é um comando, e a
passagem física só é confirmada quando o sensor óptico acusa o giro — a origem 6
([ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md)). Entre as duas cabem casos
reais: a pessoa desistiu, a catraca travou, alguém liberou e não passou.

Se o retorno ao site disser "utilizado" na autorização, um ingresso pago pode ser queimado
sem que ninguém tenha entrado. Se disser só no giro, uma catraca com sensor defeituoso faz
o site achar que ninguém entrou.

**O que o sistema faz hoje:** grava os dois fatos separados e envia o aviso de uso na
**mesma transação do consumo** — não existe "consumiu e esqueceu de avisar". A confirmação
de giro é anexada depois, quando chega, e a diferença entre os dois números aparece no
relatório como *usos sem passagem física*.

O aviso vai para a fila de saída com o conector **daquele provedor**, prioridade 5, e
chave de idempotência `uso:{ingresso}:{número do uso}` — reenviar não duplica, e o segundo
uso legítimo de um passe de dois dias é um aviso diferente, não o mesmo repetido. A
entrega é do drenador ([`15`](15-integracao-e-sincronizacao.md)): se a internet estiver
fora, o aviso espera e sobe depois, na ordem certa.

> **B11 em aberto:** para cada provedor, "utilizado" é a autorização ou o giro? Provedores
> diferentes podem responder diferente, e o sistema suporta os dois — mas alguém precisa
> decidir, por contrato, antes do evento.

### O cancelamento que chega depois da entrada

O provedor não é dono do fato local. Se o estorno chegar depois de a pessoa ter passado, a
entrada **não** é desfeita: ela vira uma linha chamada *cancelados depois de usados*, que é
exatamente o que é — uma divergência financeira para alguém resolver.

---

## 5. A prestação de contas

Cinco conjuntos precisam fechar. Eles quase nunca fecham sozinhos, e o valor do relatório
está em nomear **cada diferença**:

```
   (1) o que o site diz que vendeu
        │
        ├──► (2) o que chegou na base local          diferença = FALHA DE INGESTÃO
        │
        ├──► (3) o que foi validado na catraca       diferença = no-show
        │
        ├──► (4) o que girou de verdade              diferença = autorizado sem passagem
        │
        └──► (5) o que o site confirmou ter recebido diferença = aviso pendente
```

O relatório por provedor, que o sistema já produz:

| Linha | Pergunta que responde |
|---|---|
| Ingressos recebidos | Quantos chegaram até aqui |
| Nunca usados | *No-show* — legítimo, e a maior linha de todas |
| Usados | Ingressos com pelo menos uma entrada |
| Usos consumidos | Total de entradas (um passe de 2 dias usado 2 vezes conta 2) |
| **Usos com passagem física** | Quantos giraram de verdade |
| **Usos sem passagem física** | **Precisam de explicação, um a um** |
| **Cancelados depois de usados** | Estorno posterior à entrada |
| **Avisos pendentes de confirmação** | O site ainda não confirmou — risco de cobrança divergente |
| Tentativas negadas, por motivo | O que aconteceu na porta |

E um número que não pertence a provedor nenhum, e que é o mais importante do relatório:

> **QR desconhecidos** — tentativas com código que a base local não conhece. Ou é
> falsificação, ou é ingresso vendido que nunca chegou até nós. **No segundo caso o cliente
> foi barrado por falha nossa**, e isso não aparece em nenhum outro lugar.

### O corte, e por que ele não é detalhe

O relatório é sempre **até um instante de corte**. Rodar de novo amanhã com o mesmo corte
tem de dar exatamente o mesmo número, mesmo que evento atrasado tenha chegado no meio.

Prestação de contas que muda sozinha depois de assinada não é prestação de contas. O que
chegar depois do corte entra no relatório seguinte, como delta.

### Por baixo, a trilha é imutável

`audit_log` é encadeado por *hash*, com gatilhos que proíbem `UPDATE` e `DELETE`. Adulterar
um registro antigo quebra a cadeia e a verificação acusa. É o que permite sustentar o
número diante de um provedor que discorda dele.

---

## 6. O ingresso comprado na fila

Alguém compra pelo celular a três metros da catraca. A venda existe no site; na base local,
ainda não.

Três políticas possíveis, e **a escolha é do cliente, não minha**:

1. **Negar.** Simples, defensável, e gera reclamação na porta.
2. **Consulta pontual com prazo curto** — uma chamada só para aquele código, com teto de
   tempo e queda para negativa. Custa uma dependência de rede no caminho da porta, e num
   evento sem internet ela simplesmente não responde.
3. **Liberar com conferência** — entra, marcado para revisão, e a reconciliação resolve
   depois. Rápido na porta, e transfere o risco para a contabilidade.

> **B12 em aberto.** Sem resposta, o comportamento é o (1): negar. É o único que não
> inventa risco no lugar do cliente.

---

## 7. O que existe hoje, e o que não existe

**Construído e testado** (15 testes de integração contra SQLite de verdade):

- Cadastro de provedores, com perfil de normalização e conector próprios
- Ingestão idempotente, em lote, que não cai por causa de um item ruim
- Recusa de QR colidente entre provedores, nomeando os dois lados
- Consumo com vencedor único, janela de validade, passe de vários usos
- Registro de **toda** tentativa, inclusive negada e inclusive QR desconhecido
- Aviso de uso enfileirado na mesma transação do consumo, no conector do provedor
- Confirmação de passagem física e confirmação de recebimento pelo provedor
- Relatório de conciliação por provedor, com corte reproduzível

**Não existe:**

1. **Nenhum ingestor real.** Não há código que fale com a API de bilheteria nenhuma — o
   contrato de entrada é a chamada `Ingerir(lote)`, e quem a alimenta ainda não foi escrito.
2. **Nenhum conector de saída real.** O aviso entra na fila e nada o entrega
   ([`15`](15-integracao-e-sincronizacao.md), §11).
3. **O caminho da catraca até aqui não está ligado.** `TentarUsar` é chamado por teste, não
   pelo worker: falta o motor de decisão da Fase 2.
4. **Não há tela.** O relatório é um tipo de dados, não um PDF nem um painel.
5. **Nada foi exercitado com hardware.**

### Verificação

As travas foram violadas de propósito. Remover o índice único e a checagem de colisão faz o
teste de QR repetido reprovar, como tem de fazer.

**Uma verificação falhou em ser verificação, e isso está registrado no próprio teste:**
troquei o `UPDATE` de instrução única pela implementação ingênua — ler, decidir em memória,
escrever — e o teste de concorrência **passou igual**. A razão é que cada tentativa abre a
própria transação e o SQLite serializa escritas no arquivo. A instrução única continua
sendo a implementação certa, porque não depende de repetição por `SQLITE_BUSY` nem passa
pela promoção de leitura para escrita. Mas quem garante o vencedor único naquele teste é o
banco, não a minha cláusula `WHERE`, e eu havia afirmado o contrário.
