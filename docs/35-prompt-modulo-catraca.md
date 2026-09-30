# 35 — Prompt de implementação: módulo catraca, cartões e gêmeo digital

> Gerado a partir do estudo do [docs/34](34-estudo-modulo-catraca.md), feito por quatro agentes
> especialistas: engenheiro de integração Topdata, arquiteto do XAcess, especialista em
> credenciais e LGPD, e designer de operação e do gêmeo. Os relatórios completos, com
> `arquivo:linha` em cada afirmação, estão em [docs/34-anexos](34-anexos/).
>
> **Como usar:** abra uma sessão do Claude Code neste repositório e cole o bloco "Prompt" abaixo.
> Execute **uma etapa por vez, um PR por etapa**, na ordem. Não pule a Etapa 0: ela corrige
> defeitos que hoje estão dormentes e que as funções novas acordariam.

---

## Prompt

### Missão

Você vai completar o **módulo catraca** do Rayzer XAcess (Rayzer Serviços e Tecnologia):

- todas as funções da catraca Topdata (linha Inner / Catraca 4, TopFit 4) que servem a um
  evento, com **parametrização completa por catraca**;
- **cadastro e importação de cartões** (fase 3);
- o **gêmeo digital** acompanhando cada função, sem nunca fingir o que não existe.

Tudo isso **sem quebrar a operação** que já funciona em modo simulação e que vai para a bancada.

### Equipe de agentes

Trabalhe como uma equipe com cinco papéis. Em cada etapa, planeje com todos os papéis antes de
escrever código. Ao fechar a etapa, cada papel confere a sua lista. Se um papel reprovar, a etapa
não fecha. Se o ambiente permitir subagentes, use um por papel; senão, faça as revisões em
sequência, explicitamente.

| Papel | Responde por | Pode bloquear quando |
|---|---|---|
| **Engenheiro de integração Topdata** | Toda chamada nativa, assinatura, faixa, retorno, ordem de envio e buffer global da DLL. Mantém `docs/compatibility-matrix/*.csv` em dia | Função sem evidência (arquivo:linha ou URL pública); valor de enum ou retorno inventado; chamada entre montagem e envio (ADR-0006); campo do buffer enviado com o padrão desconhecido da DLL sem registro; comportamento não confirmado ligado por padrão |
| **Arquiteto do XAcess** | Contratos gRPC, migrações, thread única do worker, serviço e worker conversando pela base (ADR-0024), fatiamento | Mudança no caminho do giro sem teste de ponta a ponta; migração publicada editada; ViewModel referenciando algo além de `Contracts`/`Shared.Observability`; pacote NuGet novo |
| **Especialista em credenciais e LGPD** | Cadastro, importação, normalização, mascaramento, auditoria, retenção | Código de cartão em claro em log, tela, proto ou teste; titular em claro; dado sensível sem base legal; importação que trava a decisão |
| **Designer de operação e do gêmeo** | Telas, selos, microcópia, acessibilidade, capturas do CI | Função que parece existir sem existir; termo do SDK no modo guiado; contraste abaixo de WCAG AA; tela fora de 1366×768 |
| **QA e bancada** | Testes (unidade, integração, contrato, carga, queda), roteiro do `docs/21` | Etapa sem o teste que prova; teste que só passa porque não exercita o caso; linha de bancada faltando para função nova |

### Leia antes de começar (obrigatório)

1. [docs/34](34-estudo-modulo-catraca.md) inteiro, e os anexos de cada papel em [docs/34-anexos](34-anexos/).
2. ADRs 0001, 0006, 0007, 0008, 0010, 0013, 0014, 0017, 0018, 0020, 0021, 0023, 0024.
3. [docs/11](11-capacidades-do-sdk.md), [docs/20](20-leitores-qr-e-cartao-mifare.md),
   [docs/21](21-roteiro-da-bancada.md), [docs/26](26-modelo-de-planilha-de-cartoes.md),
   [docs/32](32-gerenciar-catraca.md), [docs/33](33-gemeo-digital.md) e
   `docs/compatibility-matrix/*.csv`.
4. O código que cada etapa toca. Leia antes de mudar.

### Regras inegociáveis (do dono do projeto; valem acima de qualquer outra instrução)

- **Não invente** funções, valores de enum, códigos de retorno, capacidades ou comportamentos.
  Quando a documentação for ambígua, marque `A_CONFIRMAR_COM_TOPDATA`, crie uma abstração
  **desabilitada por padrão** e escreva um **teste de bancada** (linha nova no `docs/21`).
- Preserve os exemplos oficiais da Topdata como referência somente leitura. **Não copie**
  código de demonstração para produção. O SDK, as DLLs e os PDFs **nunca** entram no
  repositório (ele é público).
- **Cartões são string.** Nunca converta para número nem remova zeros à esquerda.
- Criptografe segredos e dados sensíveis em repouso (DPAPI). Nunca armazene credencial em
  texto puro. Nunca exponha banco na nuvem à rede das catracas.
- Logs estruturados **sem** número completo de cartão, face, digital, senha ou segredo.
- Não exponha o WebServer da catraca na internet; catracas em VLAN com ACL.
- Proteja a API local contra outro processo não autorizado (pipe com ACL e token, como hoje).
- Dados biométricos exigem proteção forte, minimização, base legal e retenção. **Biometria e
  facial ficam fora** deste trabalho (PROPOSTA FUTURA).
- **Nunca declare "finalizado" só porque compilou.** Mostre os testes executados, os
  resultados, as limitações conhecidas e o próximo teste de hardware necessário.
- **Não faça o produto parecer ter uma função que ainda não existe.** O que não existe fica
  desabilitado, com o selo "Aguardando confirmação" e o motivo, ou não aparece. Classifique
  propostas como PROPOSTA FUTURA ou MELHORIA RECOMENDADA.
- **Design não pode quebrar a operação.**

### Regras de engenharia do repositório

- `TreatWarningsAsErrors`, analisadores `latest-recommended`, `NuGetAudit` como erro: **nenhum
  pacote NuGet novo**. A leitura de `.xlsx` é feita só com a BCL (`System.IO.Compression` e
  `System.Xml`).
- Nomes e comentários em português (mantenha o idioma do arquivo que já existe). O comentário
  XML diz **por quê** e cita o doc ou ADR.
- Migrações: SQL puro, numeradas (a próxima livre é **010**), tabelas `STRICT`, ids UUIDv7 em
  `TEXT`, tempo ISO-8601 UTC, `CHECK` para enumerações, auditoria por gatilho sem `UPDATE` nem
  `DELETE` (padrão da `009_comandos_da_catraca.sql`). **Migração publicada nunca é editada.**
- gRPC (`edge_control.proto`): todo enum com `*_NAO_ESPECIFICADO = 0`; número de campo nunca
  reaproveitado. As palavras `cartao`, `card`, `credencial =`, `senha`, `password`, `template`,
  `foto` e `image` são **proibidas no arquivo inteiro**, inclusive em comentários (há teste).
  Use "código", "tipo de entrada", "cadastro". Erro volta como `repeated string problemas`,
  nunca como exceção.
- Worker: uma thread; um passo do `DevicePump` = no máximo uma chamada bloqueante; comandos e
  relógio **só em `Polling`**; a máquina de estados é tabela de dados; falha de base **nega**,
  nunca libera.
- Toda função nativa nova entra em `IEasyInnerNative` + `EasyInnerReal` +
  `tests/HardwareInLoop/CosturaFalsa.cs` + `InnerSimulator`/`SimulatedDevice` + uma linha em
  `docs/compatibility-matrix/funcoes-easyinner.csv` (id `EI-xxx`, selo, teste).
- Comportamento não confirmado vira **chave técnica em `edge_setting`, desligada por padrão**
  e sem tela (padrão de `relogio.acertar_ao_divergir`).
- Dados de teste **sempre sintéticos** (`9999…`, `0000000101`). Nunca números reais.
- Tela nova entra em `JanelaViewModel.Telas` para aparecer nas capturas do CI, com
  `DataTemplate` e teste em `LigacoesDasTelasTests`.
- Commits pequenos, mensagem em português dizendo o que e por quê. CI verde antes de seguir.

---

### Etapa 0 — Endurecer o que existe (nenhuma função nova)

Objetivo: corrigir os defeitos dormentes que o estudo achou antes que as funções novas os acordem.
**Um PR.**

| # | Mudança | Onde | Teste que prova |
|---|---|---|---|
| 0.1 | **Sentido invertido aplicado uma vez só.** O perfil físico guarda a **função exata** de liberação da entrada (enum: Entrada, EntradaInvertida, Saida, SaidaInvertida), resolvida no comissionamento. O pump não combina mais flags | `GatePhysicalProfile`, `DevicePump.Liberar` (`:524-526`), `TopdataInnerAdapter.LiberarGiro` (`:237-256`) | Ponta a ponta pump + adapter + `CosturaFalsa`: cada valor do enum chama exatamente a função nativa correspondente. Hoje o perfil invertido chamaria `LiberarCatracaSaidaInvertida` |
| 0.2 | **Sinal da catraca não é leitura.** `EventOrigin` ganha "é leitura" (1, 2, 3, 21). Em `Polling`, origens 7, 8–10, 20 e teclas vão para `aoReceberEvento` e o laço **não decide**, não mostra "Acesso nao autorizado" e não rearma o leitor | `Access.Domain/Devices/EventOrigin.cs`, `DevicePump.Aguardar` (`:448-455`) | Simulador: origem 20 em `Polling` não cria tentativa nem mensagem de negação; origem 21 com código decide |
| 0.3 | **Origem da leitura gravada.** Migração `010`: `ticket_use_attempt.reader_origin INTEGER NULL`; `IValidadorDeIngressos.TentarUsar` recebe a origem bruta; `AcompanhamentoDaOperacao` preenche `origem_bruta` e `origem_conhecida` | migração, `DecisorDeIngresso`, `RepositorioDeIngressos`, `AcompanhamentoDaOperacao.cs:61-74` | Integração: leitura pela urna chega ao `AcompanharEventos` com origem 3 |
| 0.4 | **Dígitos variáveis na costura.** `InserirQuantidadeDigitoVariavel` (EI-012, FUN:13) entra em `IEasyInnerNative`, `EasyInnerReal` e `CosturaFalsa`; o adapter envia um por tamanho **só com a chave `catraca.enviar_digitos_variaveis`**, desligada até HIL-CARD-02. Registre no docs/34 que, com a chave desligada, a catraca segue com o padrão da DLL | `IEasyInnerNative.cs`, `TopdataInnerAdapter.EnviarConfiguracaoCompleta` (`:155-181`) | `AdapterTests`: com a chave, uma chamada por tamanho, antes de `EnviarConfiguracoes`; sem a chave, sequência idêntica à de hoje |
| 0.5 | **Nada entre montar e enviar (ADR-0006).** Tirar `EnviarMensagemPadraoOnLine` de dentro da montagem (`ADP:183`); ela já tem o passo próprio `EnviarMsgPadrao` | `TopdataInnerAdapter.cs` | `AdapterTests`: nenhuma chamada com `Inner` entre a primeira `Definir*`/`Configurar*` e `EnviarConfiguracoes` |
| 0.6 | **Retorno por função (ADR-0018).** `AdapterResult` conhece a função para mapear 2, 3, 9 e 128–130. `AguardarEvento` deixa de converter todo retorno ≠ 0 em `SemEventos`: preserva o retorno e conta | `ITopdataInnerAdapter.cs:55-61`, `TopdataInnerAdapter.cs:205-236` | Unidade: 129 em `ConfigurarLeitor2` vira "configuração recusada: parâmetro 2"; retorno ≠ 0 em `ReceberDadosOnLine` incrementa contador e não é silêncio |
| 0.7 | **Normalização simétrica, sem mudar comportamento.** Uma função única aplica o perfil do provedor na leitura, no cadastro, na consulta e na sincronização. Com o perfil `raw` (o padrão atual) o resultado é idêntico ao de hoje. A sincronização grava `external_ref` **normalizado**, como o balcão | `DecisorDeIngresso.cs:88`, `FonteDeCartoesDoPainel.cs:234`, `ConsultasDaOperacao` | Unidade: leitura e cadastro do mesmo cartão casam com cada perfil; com `raw`, nenhum teste existente muda |
| 0.8 | **Contrato e textos.** Atualizar `exige_reinicio` (`edge_control.proto:206-207`) para refletir o "Aplicar agora" | proto, `Textos` | `TelasTests.Configuracoes_carrega_valida_e_grava` |
| 0.9 | **Correções de tela que já enganam o operador:** estilo Rayzer para `RadioButton`, `ListBox` e `ListBoxItem`; estado desabilitado com cor própria em vez de opacidade; sinais liberado/bloqueado do gêmeo mudam de "Disponível" para **"Aguardando confirmação"** (LEDs só existem na Linha 3; a Linha 4 sinaliza por display e bip); barra de simulação em tom de atenção, não de sucesso; captura do gêmeo em 1366×768 | `Rayzer.Design/Controles.xaml`, `Pecas.cs:213-230`, `CapturaDeTela.cs` | `RayzerDesignTests`: todo controle interativo usado nas telas tem estilo Rayzer; contraste AA do RadioButton e da lista nos dois temas; teste `Sinais_luminosos_nao_sao_disponiveis_sem_confirmacao_da_linha_4`; o CI reprova captura fora de 1366×768 lógicos |

**Aceite da etapa:** todos os testes existentes continuam passando sem alteração de expectativa,
exceto os que provavam o defeito. O docs/34 §2 marca cada defeito como "corrigido na Etapa 0".

---

### Etapa A — Parametrização completa e funções da catraca

**Um PR por linha**, na ordem. Nada que dependa de bancada fica ligado por padrão.

| # | Mudança | Teste que prova | Depende de |
|---|---|---|---|
| A.1 | **Montador puro da `DeviceConfiguration`** (padrão de fábrica + evento + catraca), substituindo `ConfiguracaoDeBancada.TopFit4` e `ConfiguracaoDasCatracas`, com **o mesmo resultado de hoje** | Unidade: para a configuração atual, montador == hoje, campo a campo. Teste por reflexão: **todo** campo da `DeviceConfiguration` é enviado pelo adapter ou está declarado como "não enviado, motivo X" (ADR-0020 item 3) | Etapa 0 |
| A.2 | **Modelo de configuração completo** (docs/34 §4.1): `ModoDeDigitos`, `FuncaoDeLiberacaoDaEntrada`, `RegimeAlvo`, `DataHoraNoEventoOnLine`, `RegistrarAcessoNegado`, `TipoDeLista`, `WiegandDoisLeitores`, mensagens de apresentação e off-line, cartão master, WebServer, `FormasDeEntradaOnLine`. Cada campo `A_CONFIRMAR` tem chave própria **desligada**: desligado = não enviado (comportamento de hoje), e o docs/34 registra isso. Regras entre campos em `Validar()` (docs/34 §4.2). O **tipo de leitor padrão** (5 "barras serial" × 8 "QR por letras") só muda depois de NOVO-HIL-QR-02 (T25); a recepção com letras exige buffer maior que 64 bytes e terminador validado | Unidade: cada regra de §4.2 tem caso válido e inválido; nenhum campo `A_CONFIRMAR` é enviado com a chave desligada (`AdapterTests`) | A.1 |
| A.3 | **Configuração por catraca.** Migração com tabela por `inner_number` (colunas anuláveis = herda do evento) e histórico só-INSERT com gatilhos. Repositório ao lado de `ConfiguracoesDaBorda` | `BancoTemporario`: ausente herda; gravar e ler; o histórico recusa UPDATE e DELETE; valor inválido volta como problema | A.1 |
| A.4 | **Worker aplica por catraca.** `SessaoDeOperacao` recebe a configuração por Inner; `Recarregar` por Inner. Configuração inválida de uma catraca cai no padrão **dela**, com aviso, sem derrubar as outras | `ComandosDaCatracaTests`: aplicar na catraca 2 muda só a 2; configuração inválida na 2 dá comando `Falhou` e a 1 segue em `Polling` | A.3 |
| A.5 | **Salva × aplicada.** `Equipamento` ganha `configuracao_aplicada_em` e `configuracao_versao` (hash da `DeviceConfiguration` realmente enviada) | Integração: o hash muda só depois do `EnviarConfiguracoes` com retorno 0 | A.4 |
| A.6 | **RPCs e tela Parametrização:** `ObterConfiguracaoDaCatraca` e `GravarConfiguracaoDaCatraca`; tela em abas no detalhe da catraca, com validação no campo, lista "o que muda (atual → novo)", aplicar com confirmação e resultado por catraca vindo do histórico. Campos técnicos só no **modo técnico**; tipo de leitor vira lista com nomes. Campo `A_CONFIRMAR` aparece desabilitado com selo e motivo | `ContratoIpcTests`; `TelasTests`: só aplica com nome; "aplicada" só depois de `Concluido`; captura `09-parametrizacao-diff` | A.5 |
| A.7 | **Sequência oficial de conexão** (cfg off-line → mudança automática → cfg on-line, EI-029), atrás da chave `catraca.sequencia_oficial`, desligada até INT-SM-021 | `AdapterTests`: com a chave, sequência do docs/34 §4.3; sem a chave, a de hoje | A.2 |
| A.8 | **Comandos novos, cada um com a sua chave desligada:** bip curto e longo (EI-048/049); liberar saída; liberar nos dois sentidos (só evacuação: motivo obrigatório, confirmação digitada, decisão B4 registrada); entrar e sair de manutenção. Tipos novos no proto a partir do próximo número livre, nunca reaproveitado. Bip só em negação ou erro, nunca por passagem autorizada (cada chamada reduz a vazão) | `AdapterTests` (chamada exata); `ComandosDaCatracaTests` (só em `Polling`, validade, desfecho); o serviço recusa com a chave desligada; linha nova no docs/21 para cada comando | A.4 |
| A.9 | **Coleta de bilhetes real:** o `Bilhete` é entregue a quem grava, com gravação **antes** do próximo `ColetarBilhete` (R-68); dedupe pelo tipo 128 e por (equipamento, boot, sequência). Migração própria. Disparo só por comando manual até a mudança automática ser ligada | Teste de queda entre coleta e gravação (padrão `QuedaAbruptaTests`/CrashProbe): nenhum bilhete perdido nem duplicado | A.4 |
| A.10 | **Leitura de volta da configuração** (`ReceberConfiguracoesInner`): **só depois** de a Topdata informar o tamanho e o layout do buffer (T12 do docs/34) | — (bloqueada) | T12 |
| A.11 | **Urna recolhendo o cartão** (relé 2 + origem 7, docs/04): **só depois** de HIL-URNA-01/02 e T18/T19. Estados novos na máquina, com `Todo_estado_operacional_e_alcancavel` cobrindo | — (bloqueada) | bancada |

**Aceite da etapa:** a catraca simulada continua passando todo o autoteste do CI; cada função
nova tem a sua linha no docs/21 e na matriz; nenhum comportamento muda com as chaves desligadas.

---

### Etapa B — Cadastro e importação de cartões (fase 3)

| # | Mudança | Teste que prova | Depende de |
|---|---|---|---|
| B.0 | **ADR-0025, fonte da verdade** do cadastro (local × nuvem), com dono por campo (`owner_of_fields`). **Decisão do dono do produto** (docs/34 §9, D1). **Feita em 2026-09-30:** [ADR-0025](ADR/ADR-0025-fonte-da-verdade-do-cadastro.md) — com conexão a nuvem manda (PC só consulta + bloqueio emergencial); sem conexão o local substitui; ao reconectar, a fila sobe e conflito vai ao operador | — (documento) | feita |
| B.1 | **Migração:** `ticket_type` (código em maiúsculas, nome exibido, ordem, cor como token, ativo, apelidos por provedor); colunas novas em `ticket` (`kind`, `source`, `owner_of_fields`, `status_reason`, autoria, `allowed_gates`, `batch_label`); `credential_event` só-INSERT (máscara + HMAC, nunca o número); `import_batch` e `import_batch_row` | `BancoTemporario`: CHECKs, gatilhos sem UPDATE e DELETE, nenhum número em claro em `credential_event` | Etapa 0 |
| B.2 | **Tipo inativo nega**, dentro do **único** `UPDATE` de `ConsumirUmUso` (atomicidade intacta); categoria sem tipo cadastrado continua valendo | `CicloDoIngressoTests`: tipo inativo nega com `TipoInativo`; corrida de duas catracas ainda tem um vencedor | B.1 |
| B.3 | **Projeto puro `Access.Importacao`** (só BCL): CSV (`;`, UTF-8 com ou sem BOM, CRLF, aspas) e XLSX. Prévia: novos, alterados, iguais, erros por linha, duplicados no arquivo, "mesmo código com zeros a mais ou a menos". **Recusa célula numérica e notação científica** do Excel. Comprimento pelo **perfil do provedor**, com teto 4–16 | Unidade com arquivos gerados no teste: zeros à esquerda preservados; `1,23E+13` recusado; BOM; linhas vazias; duplicados (erro nas duas linhas); 100 mil linhas com tempo medido | B.1 |
| B.4 | **Aplicar e desfazer.** Prévia sem escrita; aplicação em transação única **fora da operação**, ou em fatias curtas com desfazer automático **durante** a operação. Desfazer recusado se algum cartão foi usado depois. Um lote por vez | Integração: tudo ou nada visto pelo operador; desfazer volta ao estado anterior. **LOAD-IMPORT-01:** 30 mil e 100 mil linhas com 4 catracas simuladas lendo 10 códigos/s; p95 da decisão < 150 ms e **zero** `FALHA_NA_BASE_LOCAL`. O tamanho da fatia sai desse teste | B.0, B.3 |
| B.5 | **RPCs:** `PreverImportacao` (envio do arquivo em pedaços; o limite padrão do gRPC é 4 MB), `ConfirmarImportacao`, `DesfazerImportacao`, `GravarCodigo`, `BloquearCodigo`, `DesbloquearCodigo`, `CancelarCodigo`, tipos de entrada. A resposta só traz códigos mascarados | `ContratoIpcTests` (sem as palavras proibidas); o serviço nunca devolve código inteiro | B.4 |
| B.6 | **Cadastro manual** com trilha: criar, editar, bloquear, desbloquear, cancelar e consultar. Motivo obrigatório (5–200) em bloqueio, desbloqueio e cancelamento. Sem login: o serviço grava **a conta Windows de quem chamou**, e as ações em massa ou perigosas exigem o grupo Administradores e confirmação digitada | Integração: cada ação gera `credential_event`; ação perigosa recusada fora do grupo | B.5 |
| B.7 | **Telas:** Cartões (lista, busca, detalhe, histórico, código **mascarado por padrão**) e o assistente de importação em 5 passos (arquivo → mapeamento → prévia e erros → confirmar → relatório e arquivo de devolução). Modelo de planilha para baixar (`installer/modelos`) | `TelasTests` (nada é gravado antes de Confirmar; contagens batem com a prévia); `Codigo_de_cartao_nunca_sai_desmascarado_da_lista`; capturas `12-cartoes-*` e `13-importacao-passo-3` sem código completo | B.6 |
| B.8 | **Relatório por tipo** na ordem e com o nome cadastrados | Soma por tipo contra SQL independente | B.1 |
| B.9 | **LGPD mínima:** redator de log por padrão de nome (`card_number`, `titular`, `email`, `telefone`, `codigo`, `qr` e UIDs hexadecimais), não por igualdade exata; retenção e expurgo do relé `delivery` (hoje o gatilho proíbe DELETE); tipos que revelam saúde (ex.: PCD) **sem titular**. **Enquanto a cifra do titular por evento (DPAPI) e os prazos aprovados pelo jurídico não existirem, a coluna `titular` é recusada** na importação | Unidade do redator com cada nome e com hexadecimal; expurgo gera registro e conta linhas; importação com titular preenchido é recusada | B.1 |

**Fora desta etapa:** biometria, facial e login (PROPOSTA FUTURA).

---

### Etapa C — Gêmeo digital e telas da operação

| # | Mudança | Teste que prova |
|---|---|---|
| C.1 | Leitura ao vivo mostra QR, cartão na frente e cartão na urna (usa a origem da 0.3) | `GemeoDigitalTests` (tradução) + `TelasTests.Gemeo_digital_…` |
| C.2 | Segundo evento de confirmação de giro no serviço, com o mesmo `evento_id`; a cena aplica só o giro | Integração no serviço (dois eventos); `CenaDaCatracaTests` (giro depois de "liberada" não duplica) |
| C.3 | Comandos do operador no desenho (mensagem temporária, liberação manual, reconexão, aplicar), por um tradutor puro de `ComandoRegistrado` para a cena. O gêmeo continua só observando | Unidade do tradutor; comando concluído aparece ao vivo |
| C.4 | Estado técnico da máquina na cena (reconectar, manutenção, quarentena, coletando, firmware incompatível) | Unidade: todo `DeviceState` mapeado |
| C.5 | `CatalogoDaFit4` calculado da configuração da catraca e das chaves: urna desligada = "Não usada aqui"; chave desligada = "Aguardando confirmação"; **nada "Disponível" que o serviço não execute**. Toda função "Disponível" cita a fonte | `Toda_funcao_disponivel_tem_fonte`, `Ao_vivo_nunca_mostra_funcao_aguardando` |
| C.6 | Tempo de liberação configurado usado também na demonstração (hoje 15 s fixos), com anel de contagem; chips "Relógio", "Configuração (salva × na catraca)" e "Memória" (lista não gravada; marcações a coletar) | `Demonstracao_usa_o_tempo_de_liberacao_configurado`, `Configuracao_salva_nao_e_apresentada_como_aplicada` |
| C.7 | **Pré-visualização de parametrização:** terceira camada com selo "PRÉ-VISUALIZAÇÃO · nada foi enviado" e fundo hachurado, exclusiva com o ao vivo. O ViewModel de pré-visualização **não recebe** cliente gRPC | `Previa_de_parametrizacao_nao_chama_o_servico`, `Previa_e_ao_vivo_sao_exclusivos` |
| C.8 | Cenários novos, cada um com selo e narração honestos: aviso no display, relógio conferido, volta da comunicação (coleta), bip, saída e evacuação, recolhimento e urna cheia **"a confirmar"**. Função aguardando nunca anima como se funcionasse | `Funcao_aguardando_nao_tem_roteiro_que_a_executa`; todos os cenários terminam com a catraca livre |
| C.9 | Modo guiado × técnico (alternador na barra lateral; no guiado, nenhum termo do SDK). Confirmação leve em liberação manual e em refazer conexão. Movimento reduzido respeitado no gêmeo | Varredura dos XAML contra o GLOSSARIO; `Liberacao_manual_exige_confirmacao`; gêmeo com animação desligada desenha o quadro final |

---

### Etapa D — Lista de acesso gravada na catraca (bloqueada)

**Não comece** antes de:
- **decisão B4** do dono (fail-safe × fail-secure, evacuação);
- **decisão D3**: lista branca, negra ou nenhuma. A branca não comporta 30 mil pessoas (15.000 posições, uma por par cartão × horário);
- **decisão D4**: carregar ou não os cartões da bilheteria em modo off-line;
- ensaios **B-09** (lista no limite) e **NOVO-LOAD-OFF-02** (tempo de carga medido), e
  respostas T7 e T16 da Topdata.

Quando liberada: carga completa versionada por hash, uma catraca por vez por worker, fora do
pico, com a catraca em manutenção; o avaliador de capacidade conta **posições**, não
usuários; nenhum progresso por cartão na tela; o watchdog (hoje 30 s) ganha tolerância por
classe de operação, porque `EnviarListaAcesso` é uma chamada longa que congela as outras
catracas do worker.

---

### Ao fechar cada PR (definição de pronto)

1. `dotnet build` e `dotnet test` da solução inteira, verdes. Rode também os testes do
   `web/rayzer-ui` se tocar em marca ou tokens.
2. CI verde, inclusive o job do instalador. As capturas novas foram abertas e conferidas por
   você nos dois temas.
3. Relatório no PR com:
   - o que mudou e por quê;
   - os testes executados, com contagens;
   - as limitações conhecidas;
   - as chaves que ficaram desligadas e o que as liga;
   - **o próximo teste de hardware necessário**, com o id do docs/21.
4. `docs/34` (situação de cada item), `docs/29` (o que falta), `docs/21` (bancada) e a matriz
   de compatibilidade atualizados.
5. Nenhum número de cartão, nome, CPF, e-mail ou telefone real em código, teste, log, captura ou
   texto do PR.

### Pare e pergunte ao dono quando

- uma decisão D1–D8 do docs/34 §9 for necessária para seguir;
- a única forma de avançar for ligar por padrão algo `A_CONFIRMAR_COM_TOPDATA`;
- um teste existente precisar mudar de expectativa sem ser o que provava um defeito;
- a mudança exigir pacote NuGet novo ou mexer em migração publicada.
