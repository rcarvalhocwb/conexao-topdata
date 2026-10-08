# RB-01 — Catraca sem atender logo depois de ligar o sistema

**Quando usar:** o painel mostra a catraca como "Aguardando a catraca conectar" ou "Programa da catraca parou", e a
mensagem fala em "retorno 8" ou em "DLL".
**Impacto se não tratar:** nenhuma catraca daquele grupo atende; o público se acumula no portão.
**Tempo estimado:** 5 minutos · **Perfil necessário:** técnico (quem instalou o sistema)

## Antes de começar

- [ ] O computador está ligado e o serviço `ConexaoTopdataEdge` aparece em `services.msc`.
- [ ] Você tem permissão de administrador no computador.

## Passos

1. **Ver a mensagem exata no painel.** Abra *Diagnóstico* e copie a linha da catraca.
   - Critério de sucesso: você sabe se é "retorno 8", "parou várias vezes" (quarentena) ou outra coisa.
   - Se for "parou várias vezes": vá para o passo 4.
2. **Rodar a verificação do ambiente.** Abra um PowerShell como administrador e rode (o `-ExecutionPolicy Bypass`
   vale só para este comando; sem ele, o Windows recusa scripts por padrão):
   `powershell -ExecutionPolicy Bypass -File "C:\Program Files\Rayzer\XAcess\verificar-ambiente.ps1"`
   - Critério de sucesso: nenhuma linha diz `FALHA`. Linhas `conferir` pedem uma checagem sua, descrita na própria
     linha; com o sistema rodando, a porta aparece como `ok` (em uso pelo próprio programa das catracas).
   - Se alguma linha disser `FALHA`: corrija o item indicado (por exemplo, o .NET Framework 3.5 pelo botão
     *Habilitar .NET Framework 3.5* do assistente, ou a EasyInner.dll pelo botão *Localizar*) e repita este passo.
3. **Reiniciar o serviço.** Em `services.msc`, clique com o botão direito em `ConexaoTopdataEdge` → *Reiniciar*.
   - Critério de sucesso: em até 1 minuto, a catraca aparece como "Atendendo" no painel.
   - Se continuar sem atender: vá para o passo 4.
4. **Quarentena.** O sistema tenta de novo sozinho 15 minutos depois de isolar o grupo. Para não esperar, reinicie
   o serviço (passo 3); isso derruba por alguns segundos todas as catracas, não só as do grupo isolado.
   Se a catraca voltar a cair logo depois, o problema é a DLL ou o ambiente, não a rede.
   - Critério de sucesso: a catraca fica em "Atendendo" por pelo menos 5 minutos.
   - Se cair de novo: vá para o passo 5.
5. **Registrar e chamar o suporte.** Anote a hora, a mensagem do Diagnóstico e o arquivo do dia em
   `%ProgramData%\ConexaoTopdata\registros\` (nome `servico-AAAA-MM-DD.log`).

## Se nada funcionar

Troque o público para as outras catracas do evento e opere a catraca parada pela liberação manual, com motivo, pela
tela *Gerenciar catraca*, conforme o procedimento de contingência do evento. Cada liberação fica registrada.

## Depois

- [ ] Anotar o incidente e a causa encontrada.
- [ ] Informar à operação o horário em que a catraca voltou.
