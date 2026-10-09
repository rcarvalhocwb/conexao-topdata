# RB-09 — Restaurar o banco a partir de uma cópia de segurança

**Quando usar:** fora do evento, quando o sistema não sobe por causa do banco (`acesso.db` corrompido ou apagado). O
motivo aparece no arquivo `%ProgramData%\ConexaoTopdata\registros\servico-AAAA-MM-DD.log`, numa linha que começa
com `PARTIDA` ("Não foi possível abrir ou atualizar a base local").
**Impacto se não tratar:** sem o banco, o sistema não decide acessos e a prestação de contas se perde.
**Tempo estimado:** 15 minutos · **Perfil necessário:** técnico

## Atenção: não restaure durante o evento

Restaurar volta a base ao instante da cópia. **Todo ingresso usado depois da cópia volta a valer**: quem
já entrou passa de novo, e o reuso dos cartões perde a contagem (docs/41, achado E3-03). Ainda não existe
ferramenta que reaplique esses usos.

- **Durante o evento:** não restaure. Opere pela liberação manual com motivo (tela *Gerenciar catraca*)
  até o fim e chame o suporte. O banco com problema fica como está, para análise.
- **Fora do evento:** restaure seguindo os passos abaixo, sabendo que os usos posteriores à cópia não
  estão nela.

## Antes de começar

- [ ] Você tem uma cópia em `%ProgramData%\ConexaoTopdata\copias\`. As cópias se chamam
      `acesso-AAAAMMDD-HHmmss-fff.db` e são feitas ao iniciar o serviço e depois a cada 6 horas (as 14 mais recentes
      ficam guardadas). Escolha a mais recente que seja anterior ao problema. Para voltar à versão anterior do
      sistema depois de uma atualização, use a de `copias\antes-da-migracao\`, feita logo antes de a versão
      nova mudar a base.
- [ ] Você sabe a hora da última cópia boa. Acessos depois dela **não estão** na cópia; anote-os pelo que estiver
      no painel e no registro de acessos.
- [ ] Você tem permissão de administrador.

## Passos

1. **Parar o serviço.** Em um PowerShell de administrador: `Stop-Service ConexaoTopdataEdge`
   - Critério de sucesso: `Get-Service ConexaoTopdataEdge` mostra `Stopped`.
2. **Guardar o banco atual, sem apagar nada.** Na pasta `%ProgramData%\ConexaoTopdata`, renomeie os três arquivos
   `acesso.db`, `acesso.db-wal` e `acesso.db-shm` para `acesso.db.antes-da-restauracao`, `...-wal.antes-da-restauracao`
   e `...-shm.antes-da-restauracao`. Se algum deles não existir, pule.
   - Critério de sucesso: nenhum arquivo `acesso.db*` (sem o sufixo) está na pasta.
   - Por quê: o `-wal` e o `-shm` antigos, deixados junto da cópia, corrompem o banco restaurado.
3. **Copiar a cópia escolhida para o lugar do banco.** Copie o arquivo de `copias\` para
   `%ProgramData%\ConexaoTopdata\` e renomeie para `acesso.db`.
   - Critério de sucesso: o arquivo `acesso.db` existe, sem `-wal` nem `-shm` ao lado dele.
4. **Subir o serviço.** `Start-Service ConexaoTopdataEdge`
   - Critério de sucesso: o serviço fica `Running` e, em até 1 minuto, o painel mostra as catracas.
5. **Conferir os números.** Em *Prestação de contas*, confira o total do dia até a hora da cópia.
   - Critério de sucesso: os números batem com o que você anotou até a hora da cópia.
   - Se o serviço não subir, ou o painel acusar banco com problema: pare aqui e acione o suporte. Não apague a cópia
     nem o arquivo renomeado no passo 2.

## Se nada funcionar

Opere pela liberação manual com motivo, conforme o procedimento de contingência do evento, enquanto o banco é
resolvido. Cada liberação manual fica registrada no histórico da catraca.

## Depois

- [ ] Registrar a hora da cópia usada e os acessos que ficaram fora dela.
- [ ] Guardar o arquivo `*.antes-da-restauracao` até o suporte concluir a análise.
