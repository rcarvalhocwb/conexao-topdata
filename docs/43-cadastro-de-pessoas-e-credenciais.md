# 43. Cadastro local de pessoas e credenciais (eventos, portaria, academias, condomínios empresariais)

**Situação:** estudo **aprovado em 09/10/2026** ([ADR-0026](ADR/ADR-0026-cadastro-de-pessoas-e-usuarios.md)), com uma mudança: no lugar dos grupos do Windows da §7.3, **login próprio** (usuário administrador padrão trocado no primeiro acesso; o administrador cria usuários e define as permissões de cada papel). Nada deste documento está no código, exceto o que a §2 diz que existe.

**Pedido do dono do produto (09/10/2026):**

- **Pergunta de partida:** "onde está o cadastro das pessoas que podem acessar pelo sistema, fora a nuvem e fora o sistema de vendas, para colocar o sistema numa portaria, ativar, desativar, abrir, fechar?"
- **Usos:** eventos, portaria, academias, condomínios empresariais "entre outros que já estudamos".
- **Cadastro completo, mas nem tudo obrigatório:** "dados pessoais, fotos, biometrias, qual empresa está vinculada a pessoa, qual a sala; nem tudo deve ser obrigatório, mas precisamos prever todas elas".
- **Dois modos:** "evento exige um tipo de ação diferente: podemos cadastrar as pessoas ou cadastrar os tipos de ingresso, que é o nosso caso inicial".
- **Primeira entrega:**
  - bloquear e desbloquear na hora;
  - portões permitidos;
  - horários permitidos;
  - importar planilha.
- **Nuvem:** a portaria fica **só local** e não vai para a nuvem.

**Fontes:**

- o código e os docs 05, 11, 13, 26, 34 (Etapa B), 35 e ADR-0025;
- pesquisa na web (09/10/2026), listada na §11.

Onde a fonte é comercial, ou não foi confirmada em fonte oficial, o texto diz.

---

## 1. Resposta curta

1. **O cadastro de pessoas não existe.** A Etapa B do docs/34 previa o cadastro de **cartões**, para eventos. Dela, só estão no código:
   - as tabelas (B.1);
   - o tipo de entrada desativado negando (B.2);
   - a prévia de importação, como biblioteca, sem tela (B.3).

   Não há tela nem comando para criar, bloquear ou desbloquear. Hoje um código só entra pela nuvem, pela bilheteria (webhook) ou pelo arquivo do modo simulação.
2. **O modelo atual é de ingresso, não de pessoa.** Ele tem código, usos, validade e intervalo de reuso, mas não tem titular, empresa, sala, horário nem foto. O campo "portões permitidos" existe na tabela, mas a decisão da catraca não o lê.
3. **O docs/05 já desenhou** `person`, `credential`, `person_credential`, `access_rule` e a biometria em arquivo separado. Este estudo parte desse desenho e o completa para os quatro usos.
4. **A decisão da catraca já é feita pelo software, on-line.** Por isso o cadastro local vale **na hora**: bloquear nega na leitura seguinte, sem enviar lista para a catraca. A lista dentro da catraca (modo off-line, `InserirUsuarioListaAcesso`) é uma etapa posterior, com bancada.

## 2. O que existe hoje e pode ser reaproveitado

| Peça | Onde | Serve para |
|---|---|---|
| Decisão on-line por código, atômica, com motivo gravado | `RepositorioDeIngressos.TentarUsar` | Ponto de entrada da nova regra "credencial de pessoa" |
| Tipos de entrada (código, nome, ordem, cor, ativo) | `ticket_type` (migração 011) | Tipos de ingresso no modo evento. A ideia vira "perfil" no modo pessoas |
| Trilha só-INSERT, com máscara e HMAC (nunca o número em claro) | `credential_event`, `TrilhaDeCredenciais` | Histórico de cada credencial |
| Lotes de importação e prévia de CSV/.xlsx | `import_batch`, `Access.Importacao` | Importar pessoas |
| Cofre DPAPI da máquina | `CofreDpapi` | Guardar a chave que cifra os dados pessoais |
| Redator de dado sensível nos registros | `RedatorDeDadoSensivel` | Nome, CPF e telefone nunca vão para log |
| Grupo do Windows "ConexaoTopdata Operadores" | `SegurancaLocal` | Base para separar quem vê dado pessoal |
| Liberação manual com motivo e auditoria | Gerenciar catraca | "Abrir" a catraca para uma pessoa sem crachá |

## 3. Dois modos de cadastro

| | **Modo ingressos** (eventos; o caso inicial) | **Modo pessoas** (portaria, academia, condomínio, credenciamento de evento) |
|---|---|---|
| O que se cadastra | **Tipos** de ingresso e os códigos vendidos. A pessoa é anônima | **A pessoa**, e uma ou mais credenciais ligadas a ela |
| De onde vem | Bilheteria, nuvem e planilha | Formulário na portaria e planilha |
| Regra principal | Usos (1 ou N), validade e intervalo de reuso | Situação da pessoa, validade, portões e horários |
| Nuvem | ADR-0025 (com conexão, a nuvem manda) | **Só local** (decisão de 09/10) |
| Volume típico | Dezenas de milhares de códigos | Centenas a poucos milhares de pessoas |

Os dois modos convivem na mesma instalação. A catraca procura primeiro o ingresso, como hoje, e, se não achar, a credencial de pessoa. O **credenciamento de evento** (staff, imprensa, fornecedor, artista) é modo pessoas dentro de um evento.

## 4. O que cada uso exige

Legenda: **O** = obrigatório · **R** = recomendado · **P** = previsto, opcional · — = não se aplica. Tudo é configurável por **perfil** (§5.2). A tabela traz o padrão sugerido.

| Grupo | Campo | Portaria corporativa / condomínio empresarial | Academia | Condomínio residencial | Credenciamento de evento |
|---|---|---|---|---|---|
| Identificação | Nome completo | O | O | O | O |
| | Nome social | P | P | P | P |
| | Documento: tipo (RG, CPF, CNH, passaporte, RNE) e número | R (visitante: O) | R | R | R (imprensa: O) |
| | Data de nascimento | P | R (menor de idade muda a regra, §6.5) | P | P |
| Contato | Telefone, e-mail | P | R | P | R |
| Foto | Foto do rosto (para conferência na tela, não biometria) | R | R | R | R |
| Vínculo | Empresa (cadastro próprio, com CNPJ opcional) | O (colaborador e prestador) | — | — | R (fornecedor, imprensa: veículo) |
| | Sala, conjunto ou unidade · andar · bloco/torre | R | — | O (unidade) | — |
| | Departamento, cargo ou função · matrícula | P | — | — | P |
| Classificação | Perfil (colaborador, visitante, prestador, morador, aluno, staff, imprensa, artista, fornecedor e outros, livres) | O | O | O | O |
| Validade | De / até | R (visitante e prestador: O) | O (plano) | P | O (dias do evento) |
| Permissões | Portões permitidos | R | P | P | O (zonas) |
| | Horários permitidos (faixas por dia da semana) | R | R (por plano) | P | P |
| | Limite de usos ou de entradas por dia | P | P (ex.: 1 por dia no plano) | — | P |
| Visita | Anfitrião (quem recebe, pessoa cadastrada) · motivo · previsão de chegada e saída · pré-cadastro | O (visitante) | — | R (visitante) | — |
| Veículo | Placa, modelo, cor | P | P | P | P |
| Responsável | Responsável legal (menor) | — | O se menor | P | P |
| Situação | Ativo, bloqueado (com motivo), inativo ou expirado | O | O (bloqueio por inadimplência vindo do sistema da academia, §6.4) | O | O |
| Credenciais | Cartão/crachá (Mifare), QR, senha (teclado), digital, face | ≥ 1 | ≥ 1 | ≥ 1 (**biometria nunca a única**, §7) | ≥ 1 |
| Observação | Texto operacional (sem CPF, telefone nem e-mail; o filtro do docs/26 já recusa) | P | P | P | P |
| LGPD | Base legal, consentimento da biometria (data e versão do termo), guardar até | O (gerado) | O | O | O |

**De onde vêm estes campos:**

- **Visitante** (nome, documento, motivo, horário previsto, placa, foto, quem visita) e **pré-cadastro pelo anfitrião:** listas de recursos de fabricantes de gestão de visitantes (Senior, Bosch, Avigilon, Johnson Controls). Fonte comercial.
- **Credenciamento de evento:** documento, CPF, contato, veículo de imprensa, e credencial **pessoal e intransferível**, vinculada a zonas. Vem de regulamentos de imprensa publicados (São João da Moda 2025, SOCERJ 2026) e de guias de credenciamento.
- **Academia:** bloqueio de inadimplente e trancamento de matrícula. Vem de anúncio comercial e de uma reclamação pública, em que a falha de sincronização entre o financeiro e a catraca deixava entrar inadimplente e barrava aluno em dia. Lição para o produto: o estado vindo de fora precisa ter data de atualização e um padrão seguro (§6.4).

**Dado sensível de saúde:** este cadastro **não** tem campo de deficiência nem de condição de saúde. Quando houver necessidade de atendimento, usa-se um marcador neutro, "atendimento prioritário", sem diagnóstico. O motivo é o mesmo do docs/34 anexo 03: tipo "PCD" ligado a pessoa identificada é dado de saúde (art. 5º, II).

## 5. Modelo proposto

### 5.1 Tabelas novas (base `acesso.db`, exceto biometria)

| Tabela | Conteúdo | Observação |
|---|---|---|
| `company` | Nome, CNPJ (opcional), situação | Empresas e condôminos; uma empresa tem salas |
| `place` | Sala, conjunto ou unidade, com andar, bloco e empresa | Hierarquia simples, não um mapa |
| `person_profile` | Perfil: nome, campos exigidos, validade padrão, portões e horários padrão, se exige anfitrião, se a biometria é permitida | Configurável. É o que torna os usos "entre outros" possíveis sem versão nova |
| `person` | Situação, perfil, empresa, sala, validade, marcador de atendimento prioritário, origem (`manual`, `importacao`, `balcao`) e campos pessoais **cifrados** (§7.2) | `full_name_enc`, `document_enc`, `contact_enc`, `birth_date_enc`, `vehicle_enc`, `note`; `name_search` = HMAC de partes do nome, para buscar sem guardar em claro (§7.2) |
| `person_photo` | Foto cifrada (JPEG até 150 KB, 480×640, o mesmo tamanho do leitor facial no docs/13) | Arquivo à parte, para não inchar a base operacional |
| `credential` | Tipo (cartão, QR, senha, digital, face), valor normalizado, perfil de normalização, situação (`ativa`, `bloqueada`, `perdida`, `devolvida`) | Do docs/05. Único por (tipo, valor) |
| `person_credential` | Pessoa ↔ credencial, com validade própria | Uma pessoa pode ter crachá **e** QR |
| `time_schedule` + `time_schedule_slot` | Até 100 tabelas de horário, cada uma com faixas por dia da semana e feriado | Compatível com as 100 tabelas do Inner (docs/34 anexo 01, `InserirHorarioAcesso`) |
| `holiday` | Feriados (data, nome) | A faixa "feriado" usa esta lista |
| `access_grant` | O que a pessoa (ou o perfil) pode: portões e tabela de horário | A pessoa herda do perfil e pode ter exceções |
| `visit` | Anfitrião, motivo, previsão, chegada e saída, credencial provisória | Credencial de visitante que **expira sozinha** |
| `person_event` | Trilha só-INSERT de cada mudança na pessoa, sem dado pessoal em claro | Como o `credential_event` |
| `biometrics.db` → `biometric_template` | Digital e face, cifrados, em **arquivo separado** | Do docs/05 §8. Fase própria (§9, P6) |

### 5.2 Perfis em vez de campos fixos

Cada **perfil** diz:

- quais campos aparecem e quais são obrigatórios;
- a validade padrão (por exemplo, visitante: hoje até 23:59);
- os portões e a tabela de horário padrão;
- se exige anfitrião;
- se permite biometria.

Já vêm prontos: **Colaborador, Prestador, Visitante, Morador, Aluno, Staff, Imprensa, Fornecedor, Artista**. O operador pode criar outros. Assim, a academia não vê "anfitrião", e a portaria não vê "plano".

## 6. Regras de decisão na catraca

### 6.1 Ordem da decisão

1. Procura o código entre os **ingressos**, como hoje. Se achou, a regra do ingresso decide, sem mudança.
2. Se não achou, procura entre as **credenciais de pessoa**. Nega quando:
   - a credencial não está ativa;
   - a pessoa está bloqueada, inativa ou fora da validade;
   - a catraca está fora dos portões permitidos;
   - o horário está fora da tabela permitida;
   - o limite de usos do dia acabou;
   - é visitante cuja visita terminou ou ainda não começou.
3. Cada negação tem um motivo próprio no "Por quê?", que já existe na tela. Os motivos novos (implementados na P2, nesta ordem de conferência) são:
   - `PessoaInativa` e `PessoaBloqueada`;
   - `CredencialInativa` (bloqueada, perdida ou devolvida);
   - `ForaDaValidade` (da pessoa ou da credencial; é a visita encerrada ou ainda não começada);
   - `PortaoNaoPermitido` (catracas da pessoa; sem elas, as do perfil; sem nenhuma, todas);
   - `ForaDoHorario` (horário de Brasília; em feriado vale a linha 7 da tabela);
   - `LimiteDiario` (conta passagens com giro e liberações dos últimos 2 minutos ainda sem giro);
   - `CatracaFechada`, conferido antes de tudo, para ingresso e pessoa.
4. O "liberado sem giro" e o estorno continuam como hoje. Uma credencial de pessoa não consome ingresso, então não há uso para estornar.

### 6.2 Bloquear e desbloquear na hora

Bloquear muda a situação na base, e a leitura seguinte já nega. A decisão é on-line, então não depende de enviar lista à catraca. Motivo obrigatório (5 a 200 letras), nome de quem pediu e conta do Windows ficam gravados na trilha.

### 6.3 Horários

- **Formato da tabela de horário:**
  - faixas `início–fim` por dia da semana, mais uma linha para feriado;
  - até 4 faixas por dia no software.
- **Limite do Inner:** na lista off-line, a catraca aceita 2 faixas por dia, segundo o WebServer do Inner. As faixas da função `InserirHorarioAcesso` não são publicadas (docs/34 anexo 01), então isso é `A_CONFIRMAR` na bancada. Para levar uma tabela à lista off-line no futuro, a tela avisa quando ela passa de 2 faixas por dia.
- **Hora de referência:** Brasília, como todo o sistema.

### 6.4 Estado vindo de fora (academia)

- **Hoje:** "inadimplente" é uma situação que o operador marca, ou que a importação traz.
- **Depois:** integração com o sistema da academia, com fornecedor a definir (fora deste estudo).
- **Regra para quando a integração existir:**
  - o estado traz a data de atualização;
  - se o dado estiver velho além de um limite configurável, vale o padrão escolhido pelo cliente, liberar ou negar;
  - o painel avisa.
- **Por quê:** é o defeito da reclamação citada na §4.

### 6.5 Menores de idade

- **Responsável legal:** obrigatório quando a data de nascimento indica menor.
- **Biometria de menor:** só com o consentimento do responsável (LGPD, art. 14).
- **Recomendação:** não oferecer biometria para menor na primeira versão.

### 6.6 "Abrir e fechar" e "ativar e desativar"

| Pedido | O que existe | O que falta |
|---|---|---|
| Abrir para alguém sem credencial | Liberação manual com motivo (Gerenciar catraca) | Ligar a liberação à pessoa ou visita, quando houver, para o histórico dela |
| Fechar a catraca (ninguém passa) | Não existe | "Catraca fechada pelo operador": a decisão nega tudo com o motivo `CatracaFechada`, sem depender de função da DLL (manutenção não tem função conhecida, docs/34). Abrir de novo com motivo |
| Ativar e desativar pessoa ou credencial | Não existe | Situação da pessoa e da credencial (§6.2) |
| Liberar nos dois sentidos, liberar saída | No serviço, atrás de chave desligada | Bancada e decisão D5 |

## 7. LGPD e segurança

### 7.1 Base legal e biometria

- **Portaria e academia:** os dados cadastrais e a foto usam execução de contrato e legítimo interesse (segurança do local). O cliente, que é o controlador, define e registra isso no RIPD dele.
- **Biometria (digital e face):** é **dado sensível**. Consentimento específico e destacado (art. 11), com data e versão do termo gravadas, e **sempre com alternativa**: cartão, QR ou senha. As fontes jurídicas consultadas convergem que, num condomínio, a biometria não pode ser a única forma de acesso nem custar mais a alternativa.
- **Regulação ainda em aberto:** a ANPD abriu tomada de subsídios sobre dados biométricos (Nota Técnica 17/2025) e pretende regulamentar em 2026. A regra pode mudar. Por isso a biometria fica numa fase própria, em arquivo separado e desligável.
- **Foto de conferência na tela:** não é biometria enquanto não é processada para identificar. Mesmo assim, fica cifrada e só aparece para quem tem permissão.

### 7.2 Como os dados ficam guardados no PC

- **Cifra por campo:**
  - AES-256-GCM, com chave por instalação guardada no cofre DPAPI, como o segredo da nuvem;
  - o identificador do registro entra como dado associado, o que impede copiar o cifrado de uma pessoa para outra (docs/34 anexo 03).
- **Busca por nome sem nome em claro:** HMAC das partes normalizadas do nome. Acha "Silva" sem guardar "Silva".
- **Tela:** documento mascarado por padrão. "Mostrar" fica registrado na trilha (`TitularRevelado` já existe).
- **Registros (logs):** nunca nome, documento, telefone ou foto. O redator atual já cobre os nomes de campo.
- **Retenção:** cada perfil tem "guardar até", por exemplo visitante 90 dias depois da visita e colaborador até o desligamento mais N dias.
  - Uma limpeza diária apaga o que venceu, com registro da quantidade.
  - O cliente define os prazos. O sistema não inventa prazo legal, porque a lei não fixa um (§11).
- **Direitos do titular:** exportar e excluir os dados de uma pessoa, com registro. Excluir apaga os campos pessoais e a biometria, e mantém o histórico de passagens só com o identificador interno.

### 7.3 Quem vê o quê, sem login (substituído pelo login próprio da ADR-0026)

O sistema não tem login hoje (docs/27 §11). Para dado pessoal isso não basta. Proposta para a primeira versão, sem inventar login, usando grupos locais do Windows:

| Grupo do Windows | Pode |
|---|---|
| `ConexaoTopdata Operadores` (já existe) | Operar a catraca, consultar e bloquear credencial, ver nome mascarado e foto |
| `ConexaoTopdata Cadastro` (novo) | Criar e editar pessoas, ver documento, importar |
| `Administradores` | Excluir pessoa, exportar dados do titular, mudar perfis e retenção |

O serviço confere o grupo **da conta do Windows de quem chamou**, pelo pipe. A tela só esconde o que o serviço já recusaria. Login próprio, com papéis e aprovação em duas pessoas, fica como proposta futura (docs/05 §7).

## 8. Catraca off-line e biometria no equipamento

Fica de fora da primeira entrega, porque depende de bancada.

- **Lista dentro do Inner:**
  - `InserirUsuarioListaAcesso(cartão, horário 1–100 | 101 sempre | 102 nunca)`, seguido de `EnviarHorariosAcesso` antes de `EnviarListaAcesso`;
  - alterar um usuário reenvia a lista inteira (docs/11 §2.4);
  - capacidade e faixas a confirmar (§11).
  - Serve para a catraca seguir decidindo se o PC cair. É a contingência D8, ainda fail-secure por decisão.
- **Digital no Inner Bio:** 104 funções com assinatura e sem semântica documentada (docs/34 anexo 01 §1.14). Bancada obrigatória.
- **Face:** leitor facial com SDK próprio (WebSocket, porta 7792, docs/13).
  - O usuário precisa estar cadastrado no leitor, com foto de até 150 KB.
  - O leitor manda o número do cartão à catraca.
  - Exige sincronizar pessoa e foto do PC para o leitor, e cuidado com a foto de desconhecidos que o `sendlog` envia.

## 9. Plano em etapas

Cada etapa tem teste. Nada liga em produção sem passar.

| Etapa | Entrega | Prova |
|---|---|---|
| **P0** | Decisões da §10 registradas na [ADR-0026](ADR/ADR-0026-cadastro-de-pessoas-e-usuarios.md) | Documento (feito) |
| **L1** | Usuários e login no serviço: administrador padrão com troca obrigatória, senhas PBKDF2, bloqueio por tentativas, papéis com permissões por função, sessão, cada RPC conferida | Sem sessão nada funciona; com a senha padrão só a troca funciona; papel sem a permissão é recusado no serviço |
| **L2** | Janela de login, troca de senha no primeiro acesso, telas Usuários e Papéis, menu conforme as permissões | Ligações das telas; capturas |
| **P1** (feito) | Migração 021: `company`, `place`, `person_profile` (perfis prontos), `person` (campos cifrados), `credential`, `person_credential`, `time_schedule(+slot)`, `holiday`, `access_grant`, `person_event`; chave de cifra no cofre | Gatilhos só-INSERT na trilha; nenhum nome ou documento em claro em nenhuma coluna (varredura no teste); cifra e decifra com dado associado |
| **P2** (feito) | Decisão: depois do ingresso, a credencial de pessoa, com situação, validade, portões, horários e limite diário; motivos novos no "Por quê?"; "catraca fechada pelo operador" | Unidade por regra (fora do horário, feriado, portão, bloqueada, expirada, visita); corrida de duas catracas; decisão abaixo de 150 ms com 5 mil pessoas |
| **P3** | Serviço: comandos de pessoa, credencial, empresa, sala, perfil, horário e feriado, bloquear e desbloquear, com conferência do grupo do Windows e resposta só com dado mascarado | Contrato sem dado em claro; ação recusada fora do grupo |
| **P4** | Telas: **Pessoas** (lista, busca, ficha com foto, credenciais, permissões, histórico), **Empresas e salas**, **Horários e feriados**, **Perfis** e, em Gerenciar catraca, **Fechar e abrir a catraca** | Ligações da tela; capturas novas; tela sem permissão não mostra documento |
| **P5** | Importação de pessoas por planilha (modelo novo no `installer/modelos`), com a prévia da B.3, aplicar e desfazer, e foto por arquivo ZIP opcional | 5 mil linhas; tudo ou nada; desfazer; recusa de CPF inválido e de campo de saúde |
| **P6** | Visitantes: pré-cadastro pelo anfitrião, credencial provisória que expira, chegada e saída | Visita expira sozinha; anfitrião bloqueado não recebe visita |
| **P7** | LGPD: retenção por perfil e limpeza diária, exportar e excluir titular, termo de consentimento versionado | Limpeza conta e registra; exclusão mantém passagens anônimas |
| **P8** (bancada) | Lista off-line no Inner a partir do cadastro (horários 1–100) | INT-OFF-03/04 do docs/21 |
| **P9** (bancada e decisão) | Biometria: `biometrics.db` separada; digital (Inner Bio) e face (SDK facial), sempre com alternativa | HIL-BIO-01; consentimento obrigatório; exclusão apaga o modelo |

**Ordem recomendada:** P0 → P1 → P2 → P3 → P4 → P5. Isso entrega "cadastrar, bloquear e desbloquear na hora, portões, horários e planilha", que é o pedido. Depois P6 e P7. P8 e P9 dependem da bancada.

## 10. Decisões para o dono do produto

| # | Pergunta | Recomendação |
|---|---|---|
| C1 | Cifrar os dados pessoais no PC (nome, documento, contato, foto)? | **Sim, por campo, com chave no cofre DPAPI** (§7.2) |
| C2 | Quem vê e quem edita, sem login? | **Grupos do Windows** (§7.3), com login próprio depois |
| C3 | Perfis prontos e configuráveis? | **Sim**, os 9 da §5.2, editáveis |
| C4 | CPF obrigatório? | **Não**: obrigatório só onde o perfil exigir (por exemplo, visitante e imprensa) |
| C5 | Biometria na primeira entrega? | **Não**: fase P9, depois da bancada e da regra da ANPD, sempre com alternativa |
| C6 | Fechar a catraca pelo software (negar tudo)? | **Sim**: motivo `CatracaFechada`, com nome e motivo |
| C7 | Retenção padrão por perfil (o cliente pode mudar)? | **Visitante: 90 dias depois da visita. Demais: até inativar mais 180 dias.** São sugestões; o cliente decide no RIPD dele |
| C8 | Integração com sistemas de academia? | **Depois**: a primeira versão só tem "inadimplente" manual ou por planilha |

## 11. Fontes e o que ficou sem confirmação

**Pesquisa (09/10/2026):**

- **ANPD, tomada de subsídios sobre dados biométricos:**
  - Nota Técnica 17/2025/CON1/CGN/ANPD, [gov.br/anpd](https://www.gov.br/anpd/pt-br/acesso-a-informacao/participacao-social/outras-acoes/ts-01-2025-nota-tecnica-17-de-abertura.pdf/@@display-file/file);
  - cobertura em [Mattos Filho](https://www.mattosfilho.com.br/unico/anpd-dados-biometricos/), [Teletime](https://teletime.com.br/04/06/2025/anpd-realiza-tomada-de-subsidios-sobre-uso-de-dados-biometricos/) e [Mobile Time](https://www.mobiletime.com.br/noticias/02/12/2025/anpd-uso-banal-biometria/).
  - A norma ainda não saiu; a previsão é 2026.
- **Biometria em condomínio e LGPD** (fontes jurídicas e comerciais, sem guia oficial específico): [Migalhas](https://www.migalhas.com.br/depeso/405764/biometria-em-condominios-tecnologia-exige-conformidade-com-a-lgdp), [ANACON](https://anacon.adv.br/biometria-em-condominios-limites-do-dever-de-adesao-e-responsabilidades-sob-a-lgpd/), [Porter](https://porter.com.br/biometria-facial-condominio-lgpd/).
- **Gestão de visitantes** (fontes comerciais): [Senior](https://www.senior.com.br/blog/controle-de-visitantes), [Avigilon](https://www.avigilon.com/br/blog/visitor-management-systems), [Bosch, ficha técnica](https://cdn.commerce.boschsecurity.com/public/documents/VisMgmt_V5.5_Data_sheet_ptBR_122847028363.pdf), [HID](https://www.hidglobal.com/pt/products/hid-visitor-manager?ls=ppc).
- **Credenciamento de evento:**
  - [regulamento de imprensa, Santa Cruz do Capibaribe](https://santacruzdocapibaribe.pe.gov.br/public/files/REGULAMENTO.pdf);
  - [regulamento SOCERJ](https://socerj.org.br/wp-content/uploads/2026/08/Regulamento_Credenciamento_Midia_SOCERJ_v3-1-4.pdf);
  - [in2event](https://help.in2event.com/hc/en-us/articles/36760368757777-What-is-accreditation);
  - [wristband.com](https://www.wristband.com/blogs/news/media-press-and-photographer-event-credentials).
- **Academia** (fonte comercial e reclamação pública): [anúncio](https://www.ohub.com.br/anuncio/sistema-de-academia-catraca-software-52403), [Reclame Aqui](https://www.reclameaqui.com.br/innova-solucoes/venda-de-solucao-softwarecatraca-defeituoso-sem-suporte_8896123/).
- **Capacidade do Inner 2 Biométrico** (ficha de varejo, **não confirmada**): 3 mil digitais, 15 mil usuários, 100 tabelas de horário. Vem de [pontofrio.com.br](https://www.pontofrio.com.br/terminal-controle-acesso-inner-2-biometrico-topdata/p/1572906846).

**Sem confirmação (perguntas para a Topdata ou para a bancada):**

- quantas faixas por dia `InserirHorarioAcesso` aceita;
- quantos usuários cabem na lista da TopFit 4;
- como a catraca trata o feriado na lista off-line;
- a semântica das funções de biometria.
