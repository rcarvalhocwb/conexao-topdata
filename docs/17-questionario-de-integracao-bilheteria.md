# 17 — O que perguntar a cada bilheteria antes do evento

> **Por que este documento existe.** Tentei analisar o Zet (`comprenozet.com.br`) direto.
> Não consegui, por duas razões que valem ser ditas com clareza:
>
> 1. **O domínio está bloqueado pela política de rede desta sessão.** Tanto
>    `comprenozet.com.br` quanto `play.google.com` são recusados pelo proxy de saída. Não
>    dá para contornar, e contornar seria errado.
> 2. **Não uso a credencial.** A senha não aparecia na captura, e mesmo que aparecesse eu
>    não entraria numa conta para inspecionar API por dentro. O caminho certo é o
>    provedor documentar a integração — o que vale para os três, não só para o Zet.
>
> O que dá para fazer, e é o que este documento faz: reunir o que o Zet publica, mapear as
> três formas possíveis de integração, e transformar isso num questionário pronto para
> enviar.

> **Decisão de 25/09: neste ano só o Zet.** Uma bilheteria só, integrada de ponta a
> ponta, para poder testar de verdade. Isso é a escolha certa — e **concentra o risco em
> vez de diminuí-lo**: se o Zet não tiver API de parceiro, não há segundo provedor para
> compensar, e o evento inteiro cai para a forma C (troca de ingresso por credencial no
> credenciamento).
>
> As perguntas **7, 8, 10 e 12** deixaram de ser levantamento e viraram **bloqueio**.
> A 8 mais que todas: **QR dinâmico inviabiliza validação na catraca**, e não há plano B
> técnico para isso — só operacional.

> **Descoberta de 25/09, no painel do produtor: o Zet TEM webhook.** Cadastro por evento,
> com um campo de link e um campo `Termos`. Isso responde metade de B10 e muda o desenho —
> o relé na nuvem deixou de ser hipótese e virou requisito, porque a máquina local não pode
> receber. Ver [ADR-0022](ADR/ADR-0022-rele-de-webhook.md).
>
> **A tela não oferece campo de segredo nem de assinatura.** A URL é a credencial. Isso
> promove as perguntas **22 a 26**, abaixo, ao topo da lista.

---

## 1. O que se sabe do Zet pelo que ele publica

| Fato | Fonte |
|---|---|
| É o portal de ingressos do Clube Gazeta do Povo (Curitiba) | site institucional e material do Clube |
| Vende ingresso de shows, teatro e cinema, com desconto para assinantes | idem |
| A validação oficial é por **aplicativo próprio — "Zet Valida"** | app publicado na Google Play |
| O app valida **por QR Code, por listagem e por busca manual** | descrição do app |
| O público-alvo declarado do app é "produtores, promoters e equipes de credenciamento" | descrição do app |
| **Nenhuma documentação pública de API apareceu** | busca ampla, nenhum resultado |

**A conclusão que importa:** a superfície de integração que o Zet *publica* é um
**aplicativo**, não uma API. Isso não quer dizer que a API não exista — plataformas
costumam ter API não documentada para parceiros. Quer dizer que **ela precisa ser pedida**,
e que a resposta pode ser não.

> Nada aqui substitui a resposta do fornecedor. É o que se enxerga de fora.

---

## 2. As três formas, e o que cada uma custa

### Forma A — API de consulta e confirmação *(o que a arquitetura assume)*

O provedor expõe consulta por cursor e um endpoint de confirmação de uso. A borda puxa a
cada poucos segundos, valida sozinha na catraca e devolve o uso pela fila de saída.

- **Tempo real:** sim
- **Funciona sem internet no evento:** sim — os ingressos já estão na base local
- **Esforço:** um ingestor por provedor, contra o contrato já existente
- **É o único desenho que suporta 30 mil pessoas em duas horas**

### Forma B — Exportação em arquivo

O provedor publica CSV/JSON periodicamente; a borda importa. O retorno de uso vira
relatório enviado depois.

- **Tempo real:** não. A latência é o intervalo da exportação
- **Ingresso comprado na fila:** não chega a tempo. Vale a política B12
- **Esforço:** menor que A
- **Aceitável** para evento com venda encerrada antes da abertura dos portões

### Forma C — Só o aplicativo do provedor

O provedor valida no app dele, e a catraca não vê o ingresso.

Isto **não** inviabiliza o evento — muda onde a validação acontece:

```
 pessoa chega  ──►  CREDENCIAMENTO                    ──►  CATRACA
                    operador valida o ingresso             lê a credencial
                    no app do provedor                     que NÓS emitimos
                    e entrega cartão/pulseira
```

O ingresso do provedor é **trocado por uma credencial nossa** na porta. A catraca passa a
controlar a credencial, não o ingresso — e é exatamente o fluxo de cartão e urna que já
está desenhado em [`04`](04-workflow-collect-card-then-enter.md).

- **Custo real:** mais gente no credenciamento, e uma fila a mais antes da catraca
- **Custo escondido:** a conciliação entre o que o provedor validou e o que a catraca
  girou vira **manual**, porque são dois sistemas sem ponte
- **Quando serve:** público menor, ou credenciamento que já existiria de qualquer jeito

**A escolha entre A, B e C não é técnica — é o que o fornecedor aceita fazer.** O sistema
suporta as três; o que muda é o custo operacional e a qualidade da prestação de contas.

---

## 3. O questionário

Pronto para enviar, igual para os três provedores. As respostas fecham **B10**, **B11** e
**B12** ([`01`](01-perguntas-criticas.md)).

### Entrada — como os ingressos chegam até nós

1. Existe **API** para listar os ingressos vendidos de um evento nosso? Qual a URL base e
   como é a autenticação?
2. A consulta é **incremental** (por cursor, `updated_since`, número de sequência)? Qual a
   granularidade?
3. Qual o **atraso real** entre a venda confirmada e o ingresso aparecer na consulta?
4. **Cancelamento, estorno e troca de titularidade** aparecem pela mesma via? Com que
   atraso?
5. Existe **exportação em arquivo** (CSV/JSON)? Com que frequência e onde?
6. Existe **webhook**? Se sim, avisamos desde já: a nossa máquina fica na rede das
   catracas e **não tem porta aberta para a internet**. Webhook exige um relé, do qual nós
   puxamos.

### Identidade — o que está impresso no ingresso

7. **O que exatamente está dentro do QR Code?** É o identificador do ingresso em texto, ou
   um token assinado/cifrado?
8. O QR é **estável** do começo ao fim, ou é **dinâmico/rotativo** (muda a cada minuto, no
   app)? *Esta pergunta decide tudo: QR dinâmico não pode ser validado offline por
   terceiro.*
9. **Formato:** comprimento, alfabeto, prefixo, maiúsculas ou minúsculas. **Pode ter zeros
   à esquerda?**
10. O código é **único entre eventos e entre produtoras**, ou pode repetir? *Estamos
    trabalhando com três bilheterias no mesmo evento, e QR repetido entre elas é
    impossível de desempatar na catraca.*
11. O ingresso é **nominal**? Carrega nome, CPF ou contato? *Define o tratamento de dado
    pessoal do nosso lado.*

### Saída — o retorno de que o ingresso foi usado

12. Existe **endpoint para marcar como utilizado**? É **idempotente** (reenviar não
    duplica)?
13. Aceita **lote**? Aceita **marcação atrasada** — inclusive horas depois, quando a
    internet voltar?
14. Para vocês, "utilizado" é a **autorização** ou a **passagem física**? Nós sabemos a
    diferença e podemos informar qualquer uma das duas.
15. A marcação é **reversível**? Dá para desfazer uma validação feita por engano?
16. Se o **app de vocês** e o **nosso sistema** validarem o mesmo ingresso, quem ganha? O
    que acontece com o segundo?

### Operação

17. Existe **ambiente de teste** com evento fictício? *Sem isso, o primeiro teste real é o
    evento.*
18. **Limite de requisições** por minuto.
19. **Contato técnico** e prazo de resposta durante o evento.

### Webhook — depois de ver a tela de cadastro

22. **Existe assinatura do payload** (HMAC, cabeçalho de assinatura, mTLS) que não aparece
    na tela de cadastro? *Hoje a URL é a única credencial: quem a descobrir injeta
    ingressos na base do evento.*
23. **De quais IPs saem as entregas?** Uma lista fixa nos permite fechar o resto.
24. **Quais acontecimentos disparam o webhook?** Venda, cancelamento, estorno, troca de
    titularidade, alteração de lote?
25. **Qual é o corpo exato?** Um exemplo real de cada tipo de acontecimento resolve as
    perguntas 7 a 11 de uma vez.
26. **Há reentrega quando a resposta falha?** Quantas vezes, em que intervalo, e existe
    forma de **pedir de novo** um período — ou uma entrega perdida está perdida?
27. **O que significa o campo `Termos`** na tela de cadastro do webhook?

### Contrato

20. A validação por **sistema de terceiro** (nossa catraca) é permitida pelo contrato de
    vocês?
21. Se um ingresso válido for recusado na porta por falha de integração, **de quem é a
    responsabilidade**?

---

## 4. O que fazer com cada resposta

| Resposta | Consequência imediata |
|---|---|
| Tem API com cursor | Forma A. Escrevo o ingestor contra o contrato que já existe |
| Só exportação em arquivo | Forma B. Importador de arquivo + política B12 = negar |
| Só o app | Forma C. Entra credenciamento com troca por credencial nossa |
| **QR dinâmico/rotativo** | **Forma C é obrigatória.** Não existe validação offline de código que muda |
| QR pode repetir entre produtoras | Exijo prefixo por provedor na ingestão, ou a colisão vira recusa na porta |
| Sem ambiente de teste | Ensaio de mesa com dados reais de um evento pequeno, antes do grande |
| Sem endpoint de uso | O retorno vira relatório de conciliação enviado depois, e a prestação de contas passa a ser posterior ao evento |

---

## 5. O que eu ainda preciso de você

1. **Enviar o questionário aos três provedores.** O Zet é um; faltam os outros dois nomes.
2. **Se algum deles tiver documentação de API**, me mandar o PDF ou o link — eu leio e
   escrevo o ingestor.
3. **Se o acesso ao painel do produtor for necessário para eu ver o formato dos dados**,
   o caminho seguro é você exportar um relatório de exemplo de um evento já encerrado, com
   os dados que puder, e me mandar o arquivo. Não preciso — e não quero — da sua senha.

> **Limitação deste documento:** tudo na seção 1 vem de material público do próprio
> fornecedor, lido de fora. Não testei nenhuma API, não vi nenhum payload, e não sei se o
> Zet tem API não documentada. A seção 2 em diante é projeto, não observação.
