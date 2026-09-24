# 14 — Estudo de caso: evento de 30.000 pessoas/dia com 4 catracas de entrada

> **Conclusão que vem antes de tudo:** quatro catracas atendem 30.000 pessoas/dia **apenas
> se a chegada for plana ao longo de 12 horas**. Com a curva de chegada de um evento real,
> o déficit é de 7 a 11 catracas. Nenhuma função do SDK resolve isso — mas o sistema pode
> medir, avisar com antecedência e reduzir o tempo de ciclo, e é isso que este documento
> especifica.

Ferramenta interativa com a calculadora e as fichas didáticas:
publicada como artefato (link na conversa de origem).

---

## 1. A aritmética

Premissa **nossa**, a validar em bancada e em campo — não é dado da Topdata:

| Tempo de ciclo | Quando se aplica | Por catraca | 4 catracas |
|---|---|---|---|
| 3,5 s | público treinado, crachá na mão | 1.029/h | 4.114/h |
| 4,5 s | proximidade em condição boa | 800/h | 3.200/h |
| 6,0 s | evento real: bolsa, criança, procura o ingresso | 600/h | 2.400/h |

Demanda, conforme a curva de chegada:

| Curva | Pessoas no pico | Exigido |
|---|---|---|
| Plana, 12 h de porta aberta | 30.000 em 12 h | 2.500/h |
| Concentrada, 70% em 3 h | 21.000 | 7.000/h |
| Show, 60% em 2 h | 18.000 | 9.000/h |

Déficit no cenário show, a 4,5 s: são necessárias **11,2 catracas**; existem 4.
As 4 escoam as 18.000 pessoas em **5,6 h**, não em 2 h. Ao fim do pico ainda há
**11.600 pessoas na fila**, que levam mais 3,6 h para entrar.

**Quantas catracas pedir:** divida o exigido no pico por 800 (ciclo de 4,5 s). Para o
cenário show, 12 catracas com uma de folga para manutenção.

---

## 2. Tetos de fábrica que mudam a arquitetura

Confirmados no portal de suporte da Topdata — ver
[`compatibility-matrix/limites-de-capacidade.csv`](compatibility-matrix/limites-de-capacidade.csv).

| Teto | Valor | Consequência |
|---|---|---|
| Lista de acesso no equipamento | **15.000** (14.900 com 16 dígitos) | A lista branca de 30.000 ingressos **não cabe** |
| Marcações armazenadas | **30.000** por equipamento | Coleta tem de ser contínua, nunca no fim do dia |
| Equipamentos por instância da DLL | ≈ 30 | Não é limite com 4 catracas |

### A inversão para lista negra

Como a lista branca não cabe, a catraca opera em **lista negra**
(`DefinirTipoListaAcesso` = 2): carrega-se apenas quem **não** pode entrar — ingressos
cancelados, duplicados, devolvidos. Essa lista é pequena e cabe sobrando.

**A contrapartida precisa ser dita ao cliente, por escrito:** em queda total de rede, a
catraca em lista negra **deixa passar quem não está na lista**. A escolha é entre deixar
15.000 pessoas de fora e aceitar risco de fraude durante a queda. É a pergunta **B4** e
não é decisão do desenvolvedor.

### Cancelamento instantâneo

`InserirUsuarioListaAcesso` aceita horário **102 = sempre negado**. Revogar um ingresso
duplicado não exige montar faixa de horário: entra como 102.

Mas `EnviarListaAcesso` **sobrescreve** a lista inteira — alterar um usuário obriga a
reenviar tudo, e o envio trava a catraca. É mais um motivo para a lista do equipamento ser
pequena.

---

## 3. Doze usos pouco explorados do SDK

1. **Medidor de desperdício.** Origem 5 (fim do tempo de acionamento) sem origem 6 (giro)
   antes = janela de liberação jogada fora. Com 5% de não-giro e relé de 10 s, perde-se
   quase meia catraca das quatro. O sistema calcula em tempo real e traduz: "catraca 3 está
   perdendo 12% das liberações — braço duro ou sinalização ruim".
2. **`LiberarLeitor` é o modo de falha mais caro.** O leitor trava se o ciclo de acesso não
   fechar; a catraca para de ler sem dar erro. Está enterrado na tabela de solução de
   problemas (7.2.3), e a assinatura é lacuna. Vigiar o tempo desde a última leitura por
   catraca e reabilitar sozinho.
3. **Ingresso consumido só na origem 6.** Consumir na autorização pune quem foi autorizado,
   desistiu e voltou. Consumir no giro também torna a detecção de duplicado correta.
4. **Revista sorteada e auditável.** Função 5 do relé é saída de revista. O critério passa
   do segurança para o software: a cada N pessoas ou lista marcada. Remove a acusação de
   seleção por aparência, que é problema jurídico em evento grande.
5. **As catracas como sinalização.** LED verde, LED vermelho, bip curto, bip longo e luz de
   fundo são comandos independentes da decisão. Luz apagada = pista fechada; vermelho fixo =
   sem rede; bip longo = chamar supervisor. Quatro faróis de operação sem hardware novo.
6. **Nome e setor no display.** 32 caracteres (16 com data). "MARIA · PISTA A" elimina a
   pausa de quem confere o ingresso e pega setor errado antes da entrada. Motivo da recusa
   em português faz a pessoa resolver sozinha em vez de travar a pista discutindo.
7. **Registrar acesso negado é alarme de fraude.** Muita instalação desliga para poupar
   memória. Ligado, a subida súbita da taxa de recusa numa única pista é lote de ingresso
   falso ou leitor sujo — ações imediatas e diferentes.
8. **Medidor de memória do equipamento.** 30.000 marcações parece folgado para 7.500 pessoas
   por catraca, até lembrar que uma passagem pode gerar mais de uma marcação. O painel
   precisa mostrar marcações guardadas / 30.000 por catraca.
9. **Deriva de relógio corrompe a ordem.** Com 4 relógios fora de sincronia, "entrou na
   pista A antes ou depois de tentar na C?" deixa de ter resposta confiável. Ler
   periodicamente e alertar sobre desvio.
10. **`PingOnline` é o que mantém o regime.** Sem ping, a catraca cai para off-line **em
    silêncio** e decide pela lista gravada, que pode estar velha. O painel mostra o regime
    real, não o configurado.
11. **Cartão mestre desligado por padrão.** Ele passa por cima da lista **no equipamento**,
    então o software nunca nega e às vezes nem sabe. Com equipe rotativa, é porta dos fundos.
12. **Evacuação com dois sentidos.** `LiberarCatracaDoisSentidos` é proibido em operação
    normal (carona) e é exatamente o que se quer numa evacuação. Ação deliberada, registrada,
    com duas pessoas — nunca um botão solto na tela.

---

## 4. O que fazer com o déficit, em ordem de eficácia

1. **Mais catracas.** Única saída que resolve. O resto compra minutos.
2. **Validar antes da catraca.** Cada recusa custa um ciclo inteiro **e** uma discussão que
   trava a pista. O sistema mede a taxa de recusa por pista, que é o gatilho para deslocar
   equipe.
3. **Achatar a chegada.** Entrada por horário ou por setor. Sair de 60%/2 h para 40%/4 h
   corta a exigência pela metade.
4. **Reduzir o ciclo.** Cada 0,5 s a menos vale ~350 pessoas/hora nas quatro catracas.
5. **Modo contingência.** Fluxo livre com contagem, por decisão registrada, quando a fila
   vira risco de segurança. Multidão comprimida mata; há um ponto em que contar vale mais
   que conferir.

---

## 5. O que este estudo **não** prova

- **Quantas marcações uma passagem gera** em off-line (1, 2 ou 3). Muda o dimensionamento da
  memória pela metade. `A_CONFIRMAR_COM_TOPDATA`.
- **Intervalo de dígitos do cartão:** manual diverge (4–16 na seção 4.1.2, 1–16 na 4.1.9).
- **Qual tipo de leitor** corresponde ao leitor de QR do modelo que o cliente tem.
- **Assinatura de `LiberarLeitor`**, citada só na tabela de solução de problemas.
- **Origens 11, 14–17 e 19**, ausentes da tabela oficial.
- **Tempos de ciclo e curva de chegada** são premissa nossa, não medição.
- E o principal: **nada aqui foi executado contra hardware.**

---

## 6. Próximo passo de código que decorre deste estudo

Uma trava que recuse montar lista de acesso acima da capacidade do modelo, usando
`limites-de-capacidade.csv` como fonte — hoje o sistema aceitaria montar 30.000 entradas e
só descobriria o problema no envio, com a catraca travada no meio do pico.
