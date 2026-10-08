# RB-06 — Catraca sem comunicação

**Quando usar:** no painel, a catraca aparece como "Sem notícia do programa da catraca" ou "Programa da catraca não
responde", e não atende há mais de um minuto.
**Impacto se não tratar:** a catraca não envia nem recebe nada do sistema; os acessos dela não aparecem.
**Tempo estimado:** 5 a 10 minutos · **Perfil necessário:** operador (passos 1 a 3); técnico (passos 4 em diante)

## Antes de começar

- [ ] Você sabe qual é a catraca (nome e número, como aparecem no painel).

## Passos

1. **Ver se o sistema está respondendo.** Se *todas* as catracas estão sem comunicação, vá para o RB-01 (o serviço pode
   ter parado). Se só uma está, continue aqui.
   - Critério de sucesso: outras catracas aparecem como "Atendendo".
2. **Pedir a reconexão pelo painel.** Em *Gerenciar catraca*, escolha a catraca, informe seu nome e clique em
   *Refazer conexão*.
   - Critério de sucesso: em até 30 segundos, a catraca volta a "Atendendo" (o histórico mostra o pedido como feito).
   - Se não voltar: vá para o passo 3.
3. **Verificar o cabo e a energia da catraca.** Confira se a catraca está ligada e se o cabo de rede está encaixado
   nas duas pontas, com luz de atividade.
   - Critério de sucesso: a luz de rede está acesa e a catraca responde ao display.
   - Se não: troque o cabo ou a fonte e repita o passo 2.
4. **Conferir IP e porta.** Siga o RB-02 (passos 1 e 4).
   - Critério de sucesso: `Test-NetConnection` até o IP da catraca na porta do grupo responde.
5. **Reiniciar o serviço**, como no RB-01 (passo 3), somente se nenhum passo anterior resolveu e ninguém estiver
   no portão desta catraca.

## Se nada funcionar

Feche a catraca ao público e direcione o fluxo para as outras. Não tente liberar pela tela sem o procedimento de
contingência do evento: a passagem não fica garantida enquanto a catraca estiver sem comunicação.

## Depois

- [ ] Anotar o horário em que a catraca parou e em que voltou.
- [ ] Registrar o incidente com a causa encontrada.
