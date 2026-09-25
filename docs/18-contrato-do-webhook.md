# 18 — Contrato do webhook de ingressos · versão 1

> **Para quem é este documento:** a equipe técnica da bilheteria (Zet). Nós definimos o
> link e o formato; vocês produzem. Ele pode ser enviado como está.
>
> **Situação:** proposta nossa, ainda não confirmada pelo Zet. O leitor deste formato já
> está implementado e testado do nosso lado (`TradutorDoContratoV1`, 21 testes).

---

## 1. Para que serve

Cada venda, cancelamento ou alteração de ingresso do evento deve ser enviada para nós
**assim que acontecer**. A catraca valida sem internet, a partir de uma base local — e
essa base só conhece o que vocês enviarem.

Um ingresso que não chegar até nós **será recusado na catraca**, com o comprador na
frente dela.

## 2. Para onde enviar

```
POST https://apizet.ruailuminada.com/webhook/<token>
Content-Type: application/json; charset=utf-8
```

O `<token>` é uma sequência de pelo menos 32 caracteres que enviaremos por canal separado.
**Ele é a credencial.** Tratem o link inteiro como segredo: não o registrem em log aberto,
não o incluam em captura de tela, não o compartilhem.

## 3. O que responderemos

| Resposta | Significado | O que fazer |
|---|---|---|
| `202 Accepted` | Recebido e guardado | Nada |
| `404` | Link errado ou token inválido | Conferir o cadastro — **não** repetir |
| `413` | Corpo acima de 1 MB | Dividir em entregas menores |
| `5xx` ou sem resposta | Indisponibilidade nossa | **Repetir**, com espera crescente |

Respondemos rápido e sem interpretar o conteúdo: guardamos primeiro, lemos depois. Um
`202` significa "está guardado", não "está correto".

## 4. O corpo

```json
{
  "versao": 1,
  "id": "entrega-9f3c2a10",
  "emitidoEm": "2026-11-14T20:31:07-03:00",
  "ingressos": [
    {
      "referencia": "ZET-8842179",
      "qr": "0081AC33F0",
      "setor": "pista",
      "validoDe": "2026-11-14T18:00:00-03:00",
      "validoAte": "2026-11-15T02:00:00-03:00",
      "usos": 1,
      "categoria": "inteira",
      "situacao": "valido"
    }
  ]
}
```

### Envelope

| Campo | Obrigatório | Tipo | Regra |
|---|---|---|---|
| `versao` | **sim** | número | Sempre `1` nesta versão |
| `id` | recomendado | texto | Identificador desta entrega. Ajuda a rastrear reenvio |
| `emitidoEm` | recomendado | texto | Data ISO 8601 **com fuso** |
| `ingressos` | **sim** | lista | Um ou mais ingressos. Lista vazia é aceita |

### Cada ingresso

| Campo | Obrigatório | Tipo | Regra |
|---|---|---|---|
| `referencia` | **sim** | texto | Identificador do ingresso **no sistema de vocês**. **Nunca muda.** É por ele que prestamos contas |
| `qr` | **sim** | **texto** | Exatamente o conteúdo do QR Code, caractere por caractere |
| `situacao` | **sim** | texto | `valido` ou `cancelado` |
| `setor` | não | texto | Pista, camarote, etc. |
| `validoDe` | não | texto | Início da validade, ISO 8601 **com fuso** |
| `validoAte` | não | texto | Fim da validade, ISO 8601 **com fuso** |
| `usos` | não | número | Quantas entradas o ingresso dá. Padrão `1` |
| `categoria` | não | texto | `inteira`, `meia`, `solidaria` — ou qualquer outra. Texto livre |

**Campos a mais são ignorados.** Vocês podem incluir o que quiserem além disso, sem
combinar conosco antes.

## 5. As três regras que não são negociáveis

Todas as outras podem ser conversadas. Estas três, se quebradas, barram gente na porta.

### 5.1 O QR é sempre texto, nunca número

```json
"qr": "0081443"     ✅
"qr": 81443         ❌  recusado
```

Um número JSON destrói zeros à esquerda antes de chegar até nós — `0081443` vira `81443`,
que é outro código. **Recusamos a entrega inteira** quando o QR vem como número, porque
aceitar "convertendo para texto" produziria um código que não existe.

### 5.2 Datas sempre com fuso

```json
"validoDe": "2026-11-14T18:00:00-03:00"     ✅
"validoDe": "2026-11-14T21:00:00Z"          ✅
"validoDe": "2026-11-14T18:00:00"           ❌  recusado
```

Sem fuso, a mesma data é três horas diferente dependendo de quem lê. O erro aparece como
ingresso recusado antes da hora ou aceito depois dela.

### 5.3 A `referencia` nunca muda, e o `qr` de uma referência nunca muda

Reenviar o mesmo ingresso é **seguro** — a `referencia` é a chave. Mas se a mesma
`referencia` chegar com outro `qr`, é outro código na catraca; e se dois ingressos
diferentes tiverem o mesmo `qr`, **recusamos o segundo** — a catraca não tem como saber de
qual dos dois é.

## 6. Quando enviar

| Acontecimento | O que enviar |
|---|---|
| Venda confirmada | O ingresso com `"situacao": "valido"` |
| Cancelamento, estorno | **O mesmo ingresso**, mesma `referencia`, com `"situacao": "cancelado"` |
| Troca de setor, de data, de titular | O mesmo ingresso com os campos atualizados |

Cancelamento não é um tipo diferente de mensagem: é o mesmo ingresso com outra situação.
Menos coisa para dar errado dos dois lados.

**Um cancelamento que chegar depois de a pessoa ter passado não desfaz a entrada.** Ele é
registrado como divergência na prestação de contas.

## 7. Repetição e ordem

- **Repitam sempre que não receberem `202`.** Recebemos o mesmo ingresso quantas vezes
  vierem; ele é gravado uma vez só.
- **A ordem não importa.** Numeramos as entregas na chegada.
- **Não há limite de ingressos por entrega**, até 1 MB de corpo.

## 8. O que NÃO queremos receber

Pedimos o mínimo, de propósito. Cada dado pessoal que recebemos vira responsabilidade
nossa sob a LGPD.

**Não enviem**, a menos que combinemos antes: CPF, e-mail, telefone, endereço, data de
nascimento, dado de pagamento.

Se o evento exigir conferência nominal na porta, conversamos sobre um campo `titular` com
o mínimo necessário e prazo de retenção definido.

## 9. Versionamento

Esta é a versão `1`. Uma mudança que altere o significado de um campo existente será a
versão `2`, e **recusamos versões que não conhecemos** em vez de tentar ler "mais ou menos"
— é exatamente aí que se aceita um ingresso que devia ser recusado.

Acrescentar campos novos **não** muda a versão.

## 10. O retorno: o que nós enviamos a vocês

Quando um ingresso passa pela catraca, enviamos:

```json
{
  "versao": 1,
  "tipo": "consumo",
  "ingresso": "ZET-8842179",
  "provedor": "zet",
  "uso": 1,
  "de": 1,
  "categoria": "inteira",
  "portao": "portao-2",
  "em": "2026-11-14T23:12:44.0000000+00:00"
}
```

| Campo | Significado |
|---|---|
| `ingresso` | A `referencia` que vocês enviaram |
| `uso` / `de` | Qual uso foi consumido, de quantos o ingresso dá |
| `categoria` | A que vocês enviaram, devolvida |
| `portao` | Onde a pessoa passou |
| `em` | Quando, sempre em UTC |

Cada aviso leva o cabeçalho `Idempotency-Key`. **Um mesmo aviso pode chegar mais de uma
vez** — quando a internet do evento cai, guardamos e reenviamos quando volta. Respondam
`409 Conflict` a um aviso já recebido; tratamos como sucesso.

**Precisamos que vocês nos digam para onde enviar isto**, e como autenticar.

## 11. O que precisamos de vocês

1. **Confirmar** que conseguem produzir este formato — ou nos dizer o que não conseguem.
2. **Um exemplo real** de cada acontecimento (venda, cancelamento, troca), de um evento de
   teste. Resolve mais dúvida que qualquer especificação.
3. **O endereço e a autenticação** para o retorno da seção 10.
4. **Um evento de teste** no ambiente de vocês, para ensaiarmos antes do evento real.
5. **O que é o campo `Termos`** na tela de cadastro do webhook.
