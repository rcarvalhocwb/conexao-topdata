# RB-02 — Catraca não fala com o computador (porta ou firewall)

**Quando usar:** a catraca fica sem comunicação ("Sem notícia") logo depois da instalação, ou depois de trocar o
computador de rede, ou quando a catraca está numa sub-rede diferente da do computador.
**Impacto se não tratar:** a catraca não recebe comandos nem envia acessos ao sistema.
**Tempo estimado:** 10 minutos · **Perfil necessário:** técnico de rede ou quem instalou o sistema

## Antes de começar

- [ ] Você sabe o IP do computador do sistema (`ipconfig` na linha *Endereço IPv4*).
- [ ] Você sabe a porta configurada para o grupo (padrão 3570; ver *Catracas* no assistente).
- [ ] Você tem permissão de administrador no computador.

## Passos

1. **Confirmar que o programa das catracas está na porta certa.** No assistente, aba *Catracas*, veja a porta do grupo.
   Na catraca, confira se o endereço do servidor é o IP do computador e a mesma porta.
   - Critério de sucesso: IP e porta batem nos dois lados.
   - Se não baterem: corrija a catraca (ou o assistente) e vá para o passo 3.
2. **Conferir se a porta está escutando.** No computador, em um PowerShell:
   `Get-NetTCPConnection -State Listen -LocalPort 3570`
   (troque 3570 pela porta do grupo).
   - Critério de sucesso: aparece uma linha com o processo `Edge.Worker.X86`.
   - Se não aparecer: o worker não subiu. Vá para o RB-01.
3. **Conferir a regra do firewall.** Em *Firewall do Windows com Segurança Avançada*, procure a regra
   **Rayzer XAcess — catracas (entrada TCP)**. Ela vale para o programa das catracas, na sub-rede local.
   - Critério de sucesso: a regra existe, está habilitada e tem o escopo *Sub-rede local*.
   - Se a catraca estiver em outra sub-rede (por exemplo, uma VLAN): edite a regra e troque o escopo remoto para o
     endereço ou a sub-rede da catraca. Anote a mudança: ela some se o sistema for reinstalado.
4. **Testar a conexão a partir de outro computador da mesma rede.** Com `Test-NetConnection IP-DO-PC -Port 3570`.
   - Critério de sucesso: `TcpTestSucceeded : True`.
   - Se der falso: a rede (switch, VLAN, IP) está bloqueando. Acione a equipe de rede do local.
5. **Verificar no painel.** Volte a *Catracas*.
   - Critério de sucesso: a catraca aparece como "Atendendo" em até 1 minuto.

## Se nada funcionar

Opere as catracas que estão atendendo e, para a catraca sem comunicação, siga o procedimento de contingência do evento.

## Depois

- [ ] Anotar a regra do firewall que foi alterada, se alguma foi.
- [ ] Registrar o incidente.
