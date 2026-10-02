# 03 — Credenciais, cadastro e importação de cartões, e proteção de dados (LGPD)

> Estudo do especialista em credenciais do Rayzer XAcess. Repositório lido em modo
> **somente leitura** (`/home/user/conexao-topdata`, commit `78517de`). Nenhum dado
> pessoal real foi aberto: as contagens citadas vêm dos próprios docs do repositório
> (docs/22 §8.5 e §9, docs/30 §2), que já as publicaram agregadas. Todo número de cartão
> neste relatório é **fictício** (`0000000101`, `9999…`).
>
> Convenções de selo: **FEITO** (existe no código), **DOC** (decidido em doc, sem código),
> **PROPOSTA** (este estudo), **A_CONFIRMAR_COM_TOPDATA** (depende do equipamento; vem com o
> ensaio de bancada que decide), **A_CONFIRMAR** (depende do cliente/jurídico).

---

## 0. Onde estamos (retrato do que existe)

| Peça | Estado | Onde |
|---|---|---|
| Credencial como texto, `CredentialValue` com bruto/normalizado/perfil e `ToString()` mascarado | FEITO | `src/Access.Domain/Credentials/CredentialValue.cs:18-88`; ADR-0008 |
| Perfis de leitura `raw`, `qr-catraca4` (4–16), `mifare-catraca4` (pad 10) | FEITO (números da Topdata **não ensaiados**) | `src/Access.Domain/Credentials/PerfisDeLeitura.cs:30-54`; `CredentialNormalization.cs:18-90` |
| Normalização nunca remove zero à esquerda | FEITO | `CredentialNormalization.cs:8-10,74-77` (comentário e `Apply`) |
| Tabela única `ticket` para QR online **e** cartão de bilheteria (cartão = "recipiente") | FEITO | `Migrations/003_ingressos_e_provedores.sql:14-35`, `004_…:5-23`, `005_…:10` |
| Unicidade do código no evento inteiro (`ux_ticket_qr`) e por provedor (`ux_ticket_provedor_ref`) | FEITO | `003_ingressos_e_provedores.sql:41,47` |
| Colisão entre provedores recusada na ingestão, com culpado | FEITO | `RepositorioDeIngressos.cs:279-286`; `Access.Domain/Ticketing/Ingestao.cs:9-14` |
| Venda de balcão (1 linha `ticket` por cartão + 1 `ticket_sale` por venda), revenda protegida | FEITO | `RepositorioDeIngressos.cs:595-703`; `004_…:27-35` |
| Categoria (tipo) **texto livre**, fotografada em cada tentativa | FEITO | `004_…:11-23`; `RepositorioDeIngressos.cs:705-715` |
| Cartões descendo do painel (`middleware-sync-cards`) com recusa de número JSON e de tamanho | FEITO (contra servidor falso) | `src/Sync.Connectors.Rest/Painel/FonteDeCartoesDoPainel.cs:249-276` |
| Consulta de código no painel, com resposta mascarada e campo apagado após consultar | FEITO | `Desktop.ViewModels/Telas.cs:423-506`; `ConsultasDaOperacao.cs:217-255` |
| Redação de log no serializador (campos sensíveis + ≥6 dígitos + foto Base64) | FEITO | `src/Shared.Observability/RedatorDeDadoSensivel.cs:28-38,103-110` |
| Segredo da nuvem em DPAPI escopo máquina | FEITO | `src/Edge.Supervisor/CofreDeSegredos.cs:23-68` |
| Auditoria imutável por trigger (`operator_command`) | FEITO | `Migrations/009_comandos_da_catraca.sql:25-44` |
| `audit_log` encadeado por hash | **Tabela existe, ninguém grava nela** (nenhuma referência em `.cs`) | `001_esquema_inicial.sql:99-122` |
| Tabela de **tipos** (nome exibido, ordem, cor, ativo) | **NÃO EXISTE** — só `category TEXT` | docs/25 §5 linha 120; docs/29 §4 linhas 73-81 |
| Cadastro manual de cartão (tela) | **NÃO EXISTE** | docs/29:75; docs/19 §8 linhas 470-474 ("bloquear cartão… só direto no banco") |
| Importação CSV/XLSX do modelo docs/26 | **NÃO EXISTE** (nenhum leitor de planilha no código; só exportação CSV da prestação de contas em `Telas.cs:660-681`) | docs/26; docs/29:76-80 |
| Modelo da planilha para baixar | FEITO (arquivos) | `installer/modelos/modelo-cadastro-de-cartoes.xlsx`, `cartoes-modelo.csv`, `tipos-modelo.csv` (números `9999…`, docs/26:93-97) |
| Gravação de lista de acesso na catraca (offline) | **NÃO EXISTE** — só o avaliador de capacidade | `Access.Application/Devices/LimitesDeCapacidade.cs:97-127` |
| Tabelas `person`, `credential`, `card_stock`, `user_account`, `approval_request`, `biometric_template` | **Só no esboço do docs/05**, não migradas | `docs/05-modelo-de-dados.md:66-116,214-260` |
| Login | PROPOSTA FUTURA; hoje nome digitado + ACL do pipe + token da máquina | docs/27:234; `Edge.Supervisor/SegurancaLocal.cs:10-77`; `009_…:12` |

**Três inconsistências que este estudo resolve antes de propor qualquer tela:**

1. **Regra de comprimento do cartão.** docs/26:74 manda recusar código com menos de 4 ou
   mais de 16 caracteres ("limite da catraca 4, mesma regra do QR"); o perfil
   `mifare-catraca4` exige **exatamente 10** (`PerfisDeLeitura.cs:51-54`); e a base real
   do ano passado tem 12 e 14 dígitos (docs/22:359-364, 471-474). Com `mifare-catraca4`,
   **todos** os cartões reais seriam recusados (docs/22:366-368, 483-487). → A importação
   **não pode** fixar comprimento: ela aplica o **perfil do provedor** escolhido depois da
   bancada, e o 4–16 do docs/26 vale como teto genérico. `A_CONFIRMAR_COM_TOPDATA`,
   ensaio docs/21 passo 3 linhas 8–12 e docs/20 §7 ensaios 1–2.
2. **Normalização assimétrica.** A ingestão aplica o perfil; o decisor compara a leitura
   **crua** (`Access.Application/Ingressos/DecisorDeIngresso.cs:88`: `RawCardData?.Trim()`),
   limitação registrada em docs/22:390-393. Se o perfil completar zeros, a leitura de 12
   não acha o cadastro de 14. → **Toda** entrada de código (leitura, cadastro manual,
   importação, consulta, sincronização) passa pelo **mesmo** `CredentialNormalization` do
   provedor. Isso é pré-requisito da fase 3, não melhoria.
3. **"Sem limite de usos".** A tabela exige `max_uses >= 1` (`003_…:31`) e a nuvem manda
   `max_uses: null`, que vira `int.MaxValue` (`FonteDeCartoesDoPainel.cs:34-38`); a consulta
   trata `>= SemLimite` como "sem limite" (`ConsultasDaOperacao.cs:68,253`). A planilha do
   docs/26:40 também tem "vazio = sem limite". → Manter a sentinela, mas **declará-la** como
   constante única compartilhada por importação, cadastro e sincronização.

---

## 1. Modelo de domínio de credencial proposto

### 1.1 Princípio

Não criar um segundo cadastro paralelo ao `ticket`. O código já trata o cartão de
bilheteria como **recipiente** e a venda como o que se consome (docs/19 §5, linhas
350-365), e já garante unicidade do código no evento inteiro (`003_…:43-47`). A proposta
**estende** esse modelo, em migrações novas (migração publicada nunca é editada —
`005_cartao_so_na_urna.sql:4-5`):

- `ticket` continua sendo "o código que a catraca lê, e o direito associado a ele".
- Entra `ticket_type` (tipos cadastrados pelo usuário — docs/26 §1).
- Entra `credential_event` (histórico imutável de cadastro/importação/bloqueio).
- Entra `import_batch` + `import_batch_row` (importação idempotente e desfazer).
- `person` do docs/05 **não** entra na fase 3: o titular é opcional e cabe cifrado na
  própria linha do cartão (§5).

### 1.2 Tipos de credencial

| Tipo (`credential_kind`) | Como chega | Leitor / origem | Perfil de normalização | Provedor típico | Estado no produto |
|---|---|---|---|---|---|
| `qr_online` | Webhook Zet via relé (`TradutorDaZet`), contrato v1, sync | Leitor de QR da tampa, leitor 1, origem 2 | `qr-catraca4` (4–16, sem pad, sem maiúsculas) — `PerfisDeLeitura.cs:30-32` | `zet`, outros sites | FEITO (ingestão); letras e 16 chars `A_CONFIRMAR_COM_TOPDATA` (HIL-CARD-03, docs/20 §7 ensaios 3–4) |
| `cartao_bilheteria` (proximidade ou Mifare, reutilizável, recolhido na urna) | Venda de balcão, sync da nuvem, **cadastro manual**, **importação** | Fenda da urna, leitor 2, origem 3 (`urn_only`) | `raw` hoje; perfil definitivo só após bancada (docs/22:383, 483-487) | `bilheteria-local` (`reusable=1`, `urn_only=1`) | Parcial: FEITO venda/sync; cadastro/importação PROPOSTA; 10/12/14 dígitos `A_CONFIRMAR_COM_TOPDATA` |
| `codigo_barras` | Importação ou provedor | Leitor serial (TipoLeitor 5) | perfil próprio (`barcode-…`), 4–16 | qualquer | PROPOSTA; simbologias listadas em docs/20:40 (Code 128, EAN-13…) mas leitura real `A_CONFIRMAR_COM_TOPDATA` |
| `staff` / `cortesia` | Cadastro manual ou importação | QR ou cartão | o do meio físico | provedor `interno` (não reutilizável, sem aviso de uso) | PROPOSTA; cortesias da Zet **não chegam** por webhook (docs/30:60-68, Z1) — a importação é a contingência |
| `biometria_digital` / `face` | SDK próprio | Sensor/leitor facial → número de cartão na catraca (docs/11:122-127) | — | — | **PROPOSTA FUTURA** com os requisitos do §5.6; fora da fase 3 (docs/29 E6) |
| `pin` | Teclado | origens 100–113 (tipos-bilhete.csv:7-10) | hash, nunca o valor (docs/15:94) | — | Fora de escopo |

### 1.3 Atributos (proposta de esquema, migração `010_tipos_e_cadastro.sql`)

**`ticket_type` (novo)** — docs/26 §1 e docs/25 §5:

| Coluna | Tipo | Regra |
|---|---|---|
| `code` | TEXT PK | `^[A-Z0-9_]{2,20}$`, sem acento (docs/26:25). Ex.: `INTEIRA`, `MEIA`, `SOCIAL` |
| `display_name` | TEXT NOT NULL | 1–40 |
| `sort_order` | INTEGER NOT NULL | ordem nos relatórios (docs/25:44) |
| `color` | TEXT NULL | token do design system (docs/29:74) |
| `active` | INTEGER NOT NULL | inativo não aceita cartão novo e **nega o uso**, mas continua nos relatórios (docs/26:28) |
| `aliases_json` | TEXT NULL | grafias de provedor mapeadas para este tipo (as ~20 grafias da Zet — docs/30:46, Z6) |
| `created_at`, `created_by`, `updated_at`, `updated_by` | TEXT | autoria (nome digitado, §2.3) |

`ticket.category` **continua texto** (categoria aberta é decisão tomada — docs/19 §5.4,
`004_…:11-13`), mas o cadastro/importação só aceita código existente em `ticket_type`; a
sincronização e o Zet mapeiam via `aliases_json` e, sem mapeamento, gravam o texto cru e
o relatório mostra "(não mapeado)" — nunca recusam ingresso por tipo desconhecido.

**`ticket` (colunas novas)**:

| Coluna nova | Tipo | Para quê |
|---|---|---|
| `kind` | TEXT NOT NULL DEFAULT `'qr_online'` | tipo de credencial (§1.2) |
| `holder_enc` | BLOB NULL | titular **cifrado** (DPAPI + chave própria, §5.4); nunca em claro |
| `holder_hint` | TEXT NULL | iniciais para a tela de consulta ("M. S."), opcional |
| `note` | TEXT NULL | observação ≤ 200, com filtro de CPF/telefone (docs/26:41) |
| `batch_label` | TEXT NULL | lote físico do cartão (docs/05 `card_stock.batch`) |
| `allowed_gates` | TEXT NULL | JSON com lista de portões; nulo = todos. **O decisor não lê hoje** (ReasonCode `GATE_NAO_PERMITIDO` existe, `ReasonCodes.cs:24`, sem uso em nenhum `.cs`) |
| `source` | TEXT NOT NULL | `sync:painel`, `import:<batch>`, `manual`, `balcao`, `webhook:zet` |
| `owner_of_fields` | TEXT NOT NULL DEFAULT `'nuvem'` | quem é dono do cadastro deste cartão (`nuvem` ou `local`) — resolve docs/29:81 |
| `status_reason` | TEXT NULL | motivo do bloqueio/cancelamento (obrigatório nesses estados) |
| `status_changed_at`, `status_changed_by` | TEXT NULL | autoria da última mudança de estado |
| `created_by`, `updated_at`, `updated_by` | TEXT | autoria |
| `retention_until` | TEXT NULL | prazo de expurgo do dado pessoal (§5.5) |

O que **já existe** e fica: `qr_raw` (como veio), `qr_normalized` (o que casa),
`provider_id` + `external_ref` (idempotência), `sector`, `valid_from`/`valid_to`,
`max_uses`/`used_count`, `category`, `last_used_epoch` (intervalo de reuso) —
`003_…:14-35`, `004_…:13-18`.

### 1.4 Estados

O `CHECK` atual aceita `valido | consumido | cancelado | bloqueado` (`003_…:34`). O docs/05
previa ainda `perdida | recolhida | reemitida` (`docs/05:82`) e `card_stock.state`
(`docs/05:107-109`). Proposta: **não** misturar estado de acesso com estado físico.

| Eixo | Valores | Quem muda | Efeito na catraca |
|---|---|---|---|
| **Acesso** (`ticket.status`, existente) | `valido`, `consumido`, `bloqueado`, `cancelado` | decisor (consumo), operador (bloqueio), provedor (cancelamento) | só `valido` libera (`RepositorioDeIngressos.cs:980-982`) |
| **Custódia física** (`card_custody_state`, novo, só `cartao_bilheteria`) | `em_estoque`, `em_circulacao`, `recolhido_na_urna`, `perdido`, `danificado`, `descartado` | balcão, esvaziamento da urna (docs/04 §6), operador | `perdido`/`danificado`/`descartado` ⇒ força `status=bloqueado` com motivo |

Transições de acesso permitidas (as demais recusadas no domínio **e** por trigger):

| De \ Para | valido | consumido | bloqueado | cancelado |
|---|---|---|---|---|
| valido | — | decisor | operador (motivo) | provedor/operador (motivo) |
| consumido | revenda (bilheteria) / reimportação com mais usos | — | operador | provedor |
| bloqueado | **desbloqueio** (operador, motivo) | — | — | operador |
| cancelado | **só** provedor reutilizável (já é a regra, `RepositorioDeIngressos.cs:924-930`) | — | — | — |

"Usado" = `used_count > 0`; "esgotado" = `consumido`. "Recolhido" é o eixo físico.

### 1.5 Histórico e auditoria

Tabela nova **`credential_event`** (append-only, mesmo padrão de `operator_command`,
`009_…:25-44`):

| Coluna | Conteúdo |
|---|---|
| `id` | UUIDv7 (ADR-0009) |
| `ticket_id` | alvo (nulo em evento de lote) |
| `code_masked` | `cred:****01(10)` — nunca o número (`CredentialValue.cs:79-87`) |
| `code_hash` | HMAC-SHA256 do normalizado com chave local (permite achar o histórico de um código sem guardar o número em claro no histórico) |
| `action` | `criado`, `editado`, `bloqueado`, `desbloqueado`, `cancelado`, `importado`, `importacao_desfeita`, `tipo_alterado`, `custodia_alterada`, `expurgado` |
| `before_json` / `after_json` | só campos não pessoais (tipo, estado, validade, usos, portões); titular aparece como `"titular": "alterado"` |
| `reason` | obrigatório em bloqueio/desbloqueio/cancelamento (5–200, como a liberação manual — `ComandoDeCatraca.cs:123`) |
| `actor` | nome digitado (2–80, `ComandoDeCatraca.cs:105`) |
| `workstation` | nome da máquina + usuário do Windows (vem do serviço, não da tela) |
| `batch_id` | importação de origem |
| `at` | ISO-8601 UTC |
| `prev_hash`, `hash` | encadeamento (padrão do `audit_log`, `001_…:99-110`) |

Triggers: `BEFORE DELETE` e `BEFORE UPDATE` → `RAISE(ABORT)`. O `audit_log` do esquema 001
**passa a ser usado** para ações de configuração e importação em lote (uma linha por lote),
e `credential_event` para cada cartão.

### 1.6 Unicidade e deduplicação entre provedores

| Regra | Mecanismo | Estado |
|---|---|---|
| Um código normalizado = **um** dono no evento | `UNIQUE (qr_normalized)` | FEITO (`003_…:47`) |
| Reenvio do mesmo item do provedor = atualização | `UNIQUE (provider_id, external_ref)` + upsert | FEITO (`003_…:41`; `RepositorioDeIngressos.cs:905-932`) |
| Consumo nunca é reescrito por sincronização/importação | `used_count`, `first/last_used_at` fora do `DO UPDATE` | FEITO (`RepositorioDeIngressos.cs:901-904`) |
| Colisão entre provedores = recusa com culpado | `ColisaoDeQr` | FEITO (`Ingestao.cs:9-14`) |
| Cartão importado localmente: `external_ref` = código normalizado; `provider_id` = bilheteria | igual ao que a venda de balcão já faz (`RepositorioDeIngressos.cs:633-636`) | PROPOSTA |
| **Grafias equivalentes** (mesmo código com `00` a mais/menos) | aviso "possível duplicata" na prévia; **não** junta sozinho | DOC (docs/26:88-91) / PROPOSTA de implementação: comparar `LTRIM(code,'0')` num índice auxiliar `ix_ticket_sem_zeros` |
| Cloud × local no mesmo cartão | `owner_of_fields`: cartão com dono `nuvem` não é editado localmente em tipo/validade (só bloqueio local, que **sempre** vence até ser desfeito); dono `local` não é sobrescrito pela sync. Bloqueio da nuvem sempre vence (ADR-0023:32-34) | PROPOSTA (resolve docs/29:81) |
| Mesmo número físico para dois cartões (Mifare 7 bytes truncado) | indetectável por software (docs/20:98-110); só lote de amostra na bancada | `A_CONFIRMAR_COM_TOPDATA` (docs/20 §6 pergunta 4, §7 ensaio 2) |

---

## 2. Cadastro manual

Nada disto existe hoje: não há tela nem operação de cadastro, bloqueio ou desbloqueio
(docs/19:470-474 — "hoje, só direto no banco"; docs/29:73-81). O que existe e deve ser
**reaproveitado**: a consulta (`ConsultaViewModel`, `Telas.cs:423-506`), o padrão de
validação que devolve "problemas" em vez de lançar (`ComandoDeCatraca.Criar`,
`ComandoDeCatraca.cs:81-140`) e o padrão de auditoria por trigger (`009_…:25-44`).

### 2.1 Fluxos

Todos passam pelo serviço (gRPC no named pipe, ADR-0004), nunca pela tela direto no banco.
Cada fluxo = um caso de uso em `Access.Application/Cartoes/` + um RPC novo no
`edge_control.proto` + uma linha em `credential_event` **na mesma transação** da mudança.

| Fluxo | Entrada | Regras | Resultado | Evento |
|---|---|---|---|---|
| **Criar** | código (digitado **ou lido no leitor do balcão**), tipo, provedor (padrão: bilheteria), titular opcional, validade, usos, observação, operador | código normalizado pelo perfil do provedor e aceito pelo perfil; não existe em **nenhum** provedor (`ux_ticket_qr`); aviso se existir com zeros a mais/menos; tipo existe e está ativo | `ticket` novo, `status=valido`, `source=manual`, `owner_of_fields=local` | `criado` |
| **Editar** | tipo, titular, validade, usos, observação, portões | não muda o código (código errado = cancelar + criar); cartão com `owner_of_fields=nuvem` só aceita campos locais (portões, observação); `max_uses` nunca abaixo de `used_count` (mesma regra do upsert, `RepositorioDeIngressos.cs:920`) | linha atualizada; **usos anteriores mantêm o tipo antigo** (foto por tentativa, `004_…:20-23`) | `editado` / `tipo_alterado` |
| **Bloquear** | código, motivo (5–200), operador | qualquer estado ≠ `cancelado`; **efeito imediato** na próxima leitura (o `UPDATE` de consumo só casa `status='valido'`, `RepositorioDeIngressos.cs:980-982`) | `status=bloqueado`, `status_reason` | `bloqueado` (prioridade 0 na outbox se a nuvem precisar saber — `outbox.priority` "0 = bloqueio emergencial", `001_…:77`) |
| **Desbloquear** | código, motivo, operador | só de `bloqueado`; volta a `valido` **ou** `consumido` conforme `used_count >= max_uses` | estado recalculado | `desbloqueado` |
| **Cancelar** | código, motivo, operador | definitivo para não reutilizável; para bilheteria, "cancelar" = retirar de circulação (custódia `descartado`) | `status=cancelado` | `cancelado` |
| **Consultar** | código digitado/lido | procura por normalizado **e** bruto (já faz: `ConsultasDaOperacao.cs:233`); resposta mascarada; campo apagado depois (`Telas.cs:441-444,467-468`) | situação + histórico de tentativas + **histórico de cadastro** (novo) | `consultado` **só** se o titular for revelado (§5.3) |

Leitura pelo **leitor do balcão**: aceitar, mas com o aviso do docs/20 §4.2 — se o leitor
do balcão entregar hexadecimal ou bytes invertidos, o número não casa com a catraca. A tela
mostra o comprimento lido e o perfil aplicado, e o cadastro **recusa** quando o perfil
recusa. `A_CONFIRMAR_COM_TOPDATA`: docs/20 §7 ensaio 1 / docs/21 passo 3 linha 7 (o mesmo
cartão na catraca e no balcão tem de dar o mesmo texto).

### 2.2 Validações (domínio, antes do banco)

| Campo | Regra | Mensagem (sem o número) |
|---|---|---|
| código | não vazio; após `Trim()` só `[0-9A-Za-z]`; **nunca** convertido para número; perfil do provedor aplicado (`CredentialNormalization.Apply`); `IsLengthAccepted` verdadeiro; teto absoluto 4–16 (docs/26:74, docs/18 §5.0) | "O código tem N caracteres; o perfil X aceita …" (padrão de `FonteDeCartoesDoPainel.cs:267-273`) |
| código com espaço interno, ponto, traço, vírgula | recusa (docs/26:73) — **não** remove sozinho | "Código com caractere que a catraca não lê" |
| tipo | existe em `ticket_type` e `active=1` | "Tipo inexistente ou inativo" |
| validade | `validade_fim > validade_inicio`; entrada em horário de Brasília, gravação em UTC (docs/05:10; `Access.Domain/Tempo/HoraDeBrasilia.cs`) | "Validade ambígua" |
| usos | vazio (sem limite, sentinela) ou inteiro ≥ 1 | — |
| titular | opcional, 2–80, sem dígitos em sequência ≥ 6 (evita CPF/telefone no campo de nome) | "Não coloque documento no nome" |
| observação | ≤ 200; recusa padrão de CPF (`\d{3}\.?\d{3}\.?\d{3}-?\d{2}`), e-mail e telefone (docs/26:41) | "Observação não pode ter CPF, e-mail ou telefone" |
| operador | 2–80, como `ComandoDeCatraca.cs:105` | "Informe quem está cadastrando" |
| motivo | 5–200 em bloquear/desbloquear/cancelar (padrão `ComandoDeCatraca.cs:123`) | — |

### 2.3 Permissões — o mínimo seguro **sem** inventar login

Hoje: o painel só fala com o serviço pelo named pipe com ACL (SYSTEM/Administradores
total, Usuários leitura e escrita — `SegurancaLocal.cs:55-77`) **e** token de sessão lido
de arquivo que só SYSTEM/Administradores escrevem (`SegurancaLocal.cs:25-53`). O nome do
operador é digitado e **não verificado** (`009_…:12`; `ComandoDeCatraca.cs:52-55`). Login e
perfis são PROPOSTA FUTURA (docs/27:234). Proposta mínima, sem criar usuários:

| Medida | Como | Por quê |
|---|---|---|
| **Identidade técnica gravada pelo serviço, não pela tela** | o serviço registra em `credential_event.workstation` a conta Windows do cliente do pipe (identidade do chamador do named pipe) e o nome da máquina | o nome digitado é declarativo; a conta Windows não é forjável pela tela |
| **Nome digitado obrigatório** (2–80) em toda escrita | igual ao comando de catraca | trilha legível para o relatório R6 (docs/25:77-84) |
| **Motivo obrigatório** em bloquear/desbloquear/cancelar/importar | 5–200 | o "porquê" é o que a auditoria precisa |
| **Ações perigosas exigem estar no grupo Administradores do Windows** | o serviço confere o SID do chamador (não a tela): importação em massa, cancelamento em massa, desfazer importação, apagar/expurgar, **revelar titular**, exportar lista com titular | reaproveita a ACL do SO; nada de senha nova |
| **Confirmação digitada** nas ações em massa | digitar a quantidade afetada ("Cancelar 1.832 cartões? Digite 1832") | evita clique acidental sem inventar segunda pessoa |
| **Duas pessoas** (`approval_request` com `approved_by <> requested_by`, docs/05:227-232) | **PROPOSTA FUTURA** — só faz sentido com login; sem login, o `CHECK` compara nomes digitados, o que não prova nada | honestidade: não vender controle que não existe |
| Bloqueio de edição durante o pico | configuração "modo evento": importação e edição em massa pedem confirmação extra entre a abertura dos portões e o fechamento | a importação disputa o escritor do SQLite com o consumo (§3.9) |

### 2.4 Trilha de auditoria imutável (padrão das triggers de `operator_command`)

```sql
-- 010_tipos_e_cadastro.sql (PROPOSTA)
CREATE TABLE credential_event (
    id          TEXT NOT NULL PRIMARY KEY,            -- UUIDv7
    ticket_id   TEXT NULL REFERENCES ticket (id),
    code_masked TEXT NOT NULL,                        -- cred:****01(10)
    code_hash   TEXT NOT NULL,                        -- HMAC-SHA256(normalizado, chave local DPAPI)
    action      TEXT NOT NULL CHECK (action IN ('criado','editado','tipo_alterado','bloqueado',
                     'desbloqueado','cancelado','importado','importacao_desfeita',
                     'custodia_alterada','titular_revelado','expurgado')),
    before_json TEXT NULL,                            -- sem titular em claro
    after_json  TEXT NULL,
    reason      TEXT NULL,
    actor       TEXT NOT NULL,                        -- nome digitado
    workstation TEXT NOT NULL,                        -- conta Windows + máquina, posta pelo serviço
    batch_id    TEXT NULL,
    at          TEXT NOT NULL,
    prev_hash   TEXT NOT NULL,
    hash        TEXT NOT NULL,
    CHECK (action NOT IN ('bloqueado','desbloqueado','cancelado') OR length(trim(reason)) BETWEEN 5 AND 200)
) STRICT;
CREATE INDEX ix_credential_event_codigo ON credential_event (code_hash, at);

CREATE TRIGGER credential_event_nao_se_apaga BEFORE DELETE ON credential_event
BEGIN SELECT RAISE(ABORT, 'credential_event é auditoria: não se apaga'); END;
CREATE TRIGGER credential_event_nao_muda BEFORE UPDATE ON credential_event
BEGIN SELECT RAISE(ABORT, 'credential_event é auditoria: não muda'); END;

-- Mudança de estado sem motivo não passa nem por fora do caso de uso:
CREATE TRIGGER ticket_bloqueio_exige_motivo BEFORE UPDATE OF status ON ticket
WHEN NEW.status IN ('bloqueado','cancelado') AND OLD.status <> NEW.status
     AND NEW.source <> 'sync:painel' AND (NEW.status_reason IS NULL OR length(trim(NEW.status_reason)) < 5)
BEGIN SELECT RAISE(ABORT, 'bloquear ou cancelar exige motivo'); END;
```

Notas:
- O encadeamento `prev_hash/hash` é calculado no serviço (uma escrita por vez; o serviço
  é o único escritor de cadastro), e um verificador periódico refaz a cadeia (docs/05:245-246).
- **Expurgo LGPD × imutabilidade:** como o evento não guarda número nem titular em claro
  (só máscara e HMAC), a trilha **não precisa ser apagada** no expurgo; apagar a chave do
  HMAC do evento encerrado torna `code_hash` irreversível (§5.5).
- O mesmo raciocínio vale para `operator_command`: ele guarda o nome digitado, que é dado
  pessoal do operador (base legal: execução do contrato de trabalho/legítimo interesse) —
  precisa entrar na política de retenção.

---

## 3. Importação

### 3.1 O que existe e o que falta

| Item | Estado |
|---|---|
| Especificação de colunas, regras e recusas | DOC — `docs/26-modelo-de-planilha-de-cartoes.md:19-75` |
| Modelo `.xlsx` com abas Instruções/Tipos/Cartões/Exemplo, coluna A (`codigo`) com formato **Texto** (`numFmtId=49`, estilo 3 na coluna inteira), validação de lista em `tipo` e `situacao` e inteiro ≥ 1 em `usos_maximos` até a linha 100.000 | FEITO (conferido abrindo a estrutura do pacote, sem ler conteúdo de cliente) — `installer/modelos/modelo-cadastro-de-cartoes.xlsx` |
| CSVs de modelo em UTF-8 **com BOM** e **CRLF**, números fictícios `9999…` | FEITO — `installer/modelos/cartoes-modelo.csv`, `tipos-modelo.csv`; teste `tests/Integration/ModelosDePlanilhaTests.cs:20-50` |
| Leitor de CSV/XLSX, prévia, validação, gravação, desfazer | **NÃO EXISTE** (não há biblioteca de planilha em nenhum `.csproj`) |
| Importador dos CSVs exportados do painel (`autorizacoes-…`, `cartoes-autorizados-…`, `cartoes-rfid-…`) como carga de contingência | **NÃO EXISTE** — docs/22:497-498 |

Caveat do modelo: a validação de dados do Excel **não vale para colagem** (colar por cima
substitui formato e validação da célula). O modelo ajuda quem digita; o importador **não
pode confiar nele** e revalida tudo.

### 3.2 Formatos aceitos

| Formato | Aceito | Observação |
|---|---|---|
| `.xlsx` (Office Open XML) | sim | abas por **nome** (`Tipos`, `Cartões`/`Cartoes`); `Exemplo` e `Instruções` ignoradas **sempre** (docs/26:95-97) |
| `.csv` separador `;`, UTF-8 | sim, padrão (docs/26:16-17) | um arquivo de cartões; tipos em arquivo separado ou já cadastrados |
| `.csv` separador `,` ou TAB | sim, **detectado pelo cabeçalho**, com aviso | Excel em inglês salva `,`; "Texto Unicode" salva TAB + UTF-16 |
| `.xlsm`, `.xls` (binário antigo), `.ods` | **não** | macro e formato binário: fora; mensagem manda salvar como `.xlsx` |
| CSV exportado do painel (layout do docs/22 §9) | sim, **como perfil de layout** separado, mapeado para o docs/26 (§4 do docs/26: `tipo` ← categoria, `titular` ← **vazio**, `situacao` ← ativo) | carga inicial sem internet; o titular do CSV do painel **é descartado na leitura** (minimização) |
| JSONL do backup de webhooks da Zet | já coberto pelo `TradutorDaZet` (docs/30:103, caminho C) | não é planilha; mesma tela, outro tradutor |

### 3.3 Leitura robusta de CSV

| Caso | Tratamento |
|---|---|
| **BOM UTF-8** (`EF BB BF`) | removido; UTF-8 |
| BOM UTF-16 LE/BE | UTF-16 |
| Sem BOM | tenta **UTF-8 estrito** (`throwOnInvalidBytes`); se falhar, **Windows-1252** (o "CSV" do Excel pt-BR sem UTF-8) com aviso "arquivo em ANSI; acentos convertidos" — exige `CodePagesEncodingProvider` |
| Separador | 1ª linha `sep=;` (dica do Excel) respeitada; senão o separador é o que faz o cabeçalho bater com as colunas esperadas (`;` > TAB > `,`) |
| Aspas (RFC 4180) | campo entre aspas pode conter separador e quebra de linha (observação); `""` = aspas literais |
| **CRLF / LF / CR** | todos aceitos; número de linha reportado é o **físico** (o que o Excel mostra) |
| **Linhas vazias** e linhas só com separadores (`;;;;;;;`) | ignoradas e **contadas** ("12 linhas vazias ignoradas"); nunca viram erro |
| Espaços | `Trim()` nas pontas de todo campo (a normalização já faz, `CredentialNormalization.cs:67`); espaço **interno** no código = erro |
| Caracteres invisíveis | NBSP (U+00A0), zero-width (U+200B), BOM no meio de linha, TAB colado → removidos das **pontas** com aviso; no meio do código = erro |
| Cabeçalho | comparado sem caixa e sem espaços nas pontas; coluna obrigatória faltando = arquivo recusado inteiro; coluna desconhecida = aviso e ignorada; coluna repetida = recusa |
| Aspas "inteligentes", `'` inicial (texto forçado do Excel: `'0000000101`) | `'` inicial removido **só** na coluna `codigo`, com aviso |

### 3.4 Leitura robusta de XLSX

Biblioteca proposta: **DocumentFormat.OpenXml** (Microsoft, MIT) em modo **streaming**
(`OpenXmlReader`), que expõe o **tipo da célula** e o estilo — é disso que depende a regra
dos zeros. `A_DECIDIR` pelo time (alternativas: ClosedXML, ExcelDataReader); nenhuma está
no repositório hoje.

| Situação da célula `codigo` | Decisão | Base |
|---|---|---|
| texto (`t="s"` ou `t="inlineStr"`) | lê como veio | — |
| **número** (sem `t` ou `t="n"`), mesmo que "pareça certo" | **erro na linha**: "célula formatada como número; o Excel pode ter tirado os zeros" | docs/26:71; ADR-0008:23-24; risco R-30 (docs/08:41) |
| texto em notação científica (`1,23457E+11`, `1.23457E+11`) | **erro** | docs/26:72 |
| texto terminando em `,0`/`.0` | **erro** (número convertido em texto) | idem |
| fórmula (`<f>`) | **erro** — não se importa valor calculado | também fecha injeção de fórmula |
| data em `validade_*` como número serial com formato de data | aceito, convertido no sistema 1900/1904 do arquivo, interpretado como **horário de Brasília** | docs/26:38-39 |
| data como texto `dd/mm/aaaa hh:mm` | aceito | idem |
| aba oculta, várias abas "Cartões" | só a aba de nome exato; ambiguidade = recusa | — |

Proteções do pacote: tamanho descompactado ≤ 100 MB, nº de entradas ≤ 1.000, sem
`externalLink`, sem `vbaProject.bin`; senão recusa (arquivo de fora é entrada não confiável).

### 3.5 Zeros à esquerda: como detectar e avisar

O Excel (formato "Geral") remove zeros à esquerda e mostra números com mais de 11 dígitos
em notação científica; acima de 15 dígitos troca os finais por zero. No `.xlsx` isso se
**prova** pelo tipo da célula; no CSV só se **suspeita**. A regra: provado = erro,
suspeito = aviso na prévia que exige "Revisei" para confirmar.

| Sinal | Formato | Nível |
|---|---|---|
| célula numérica | xlsx | **erro** |
| notação científica / `,0` | ambos | **erro** |
| comprimento **menor** que o comprimento dominante **do mesmo tipo** (ex.: tipo com 98% de 12 dígitos e alguns de 11) | ambos | **aviso** "N códigos com 1–2 dígitos a menos que os demais do tipo INTEIRA — podem ter perdido zeros" (é o padrão visto em produção: 10 cartões de 11 dígitos, docs/22:363) |
| código igual a um já cadastrado **a menos de zeros à esquerda** (`LTRIM(code,'0')` igual) | ambos | **aviso**, e **não junta** (docs/26:88-91; 31 duplicatas SOCIAL 12↔14, docs/22:370-375) |
| perfil do provedor completa zeros (ex.: `mifare-catraca4`) | ambos | **informação** "N códigos completados até 10 dígitos pelo perfil" — nunca silencioso |
| perfil recusa o comprimento | ambos | **erro** com o tamanho, nunca o número (padrão `FonteDeCartoesDoPainel.cs:267-273`) |
| arquivo sem nenhum código começando com `0` num tipo em que a base já tem vários | ambos | aviso de lote |

Mensagem ao operador (sem número): *"Linha 42: a célula do código está como Número no
Excel. Selecione a coluna A, formate como Texto e cole de novo a partir do arquivo
original. Não redigite: o zero que sumiu não volta."*

### 3.6 Validação linha a linha e relatório de erros

Pipeline: **ler → normalizar → validar isolada → validar contra o arquivo → validar
contra a base → classificar**. Nada toca o banco até a confirmação (docs/26:50-52).

| # | Regra | Nível |
|---|---|---|
| 1 | `codigo` obrigatório; só `[0-9A-Za-z]`; 4–16 como teto; aceito pelo perfil do provedor | erro |
| 2 | `tipo` existe (na aba Tipos do mesmo arquivo **ou** na base) e ativo | erro (docs/26:60-61) |
| 3 | `situacao` ∈ {ATIVO, BLOQUEADO} (aceita `ativo`, com aviso) | erro |
| 4 | `validade_inicio`/`validade_fim` em `dd/mm/aaaa hh:mm`; fim > início | erro (docs/26:75) |
| 5 | `usos_maximos` vazio ou inteiro ≥ 1 | erro |
| 6 | `titular` ≤ 80, sem sequência de ≥ 6 dígitos | erro |
| 7 | `observacao` ≤ 200, sem CPF/e-mail/telefone | erro (docs/26:41) |
| 8 | **código repetido no arquivo** (após normalizar) | erro **nas duas linhas** (docs/26:58-59) |
| 9 | código já existe **em outro provedor** | erro "pertence a outro provedor" (mesma trava de `ux_ticket_qr`) |
| 10 | código já existe e está **bloqueado manualmente** e a linha diz ATIVO | **conflito**: não desbloqueia; "desbloqueie pela tela, com motivo" |
| 11 | código já existe e é **gerenciado pela nuvem** (`owner_of_fields=nuvem`) | conflito; opção explícita "assumir o cadastro local" (Administrador) |
| 12 | igual a outro com zeros a mais/menos | aviso |

Classificação da prévia (docs/26:50-52): **novos · mudam (com os campos que mudam) ·
iguais · com erro · com conflito · avisos**. A tela mostra as contagens e as primeiras 200
linhas de cada grupo, com **número da linha e motivo**, e o código **mascarado**
(`cred:****01(10)`). O **arquivo de devolução** (docs/26:53-54) é um CSV `;` UTF-8 com BOM,
com as colunas originais + `linha` + `erro`, gravado onde o operador escolher — ele contém
o código completo (é o dado do próprio operador), por isso não é salvo em pasta do sistema
nem anexado a log; células que começam com `= + - @` são neutralizadas como em
`Telas.cs:676-680`.

### 3.7 Idempotência, inclusão × atualização, cancelamento em massa

| Regra | Implementação |
|---|---|
| Reimportar o mesmo arquivo não duplica | chave natural `(provider_id, external_ref=codigo normalizado)` + upsert (mesmo mecanismo de `RepositorioDeIngressos.cs:905-932`); linhas **iguais** não geram escrita, nem `credential_event`, nem `updated_at` |
| Reconhecer o arquivo | `import_batch.file_sha256`; a prévia avisa "este arquivo já foi importado em 03/10 às 14:12 por Fulano (0 novos, 0 mudam)" |
| Mesmo código outra vez = atualização | tipo, titular, situação, validade, usos, observação (docs/26:55-57); **nunca** `used_count`, `first/last_used_at`, `last_used_epoch` (relógio de reuso) |
| A troca de tipo vale dali em diante | a categoria é fotografada em cada tentativa (`004_…:20-23`) — relatório não muda para trás |
| `max_uses` menor que os usos já feitos | vira `MAX(novo, used_count)`, como o upsert atual (`RepositorioDeIngressos.cs:920`) |
| Modos | **Incluir e atualizar** (padrão) · **Só incluir** (existentes ficam como estão) |
| A importação nunca apaga | cartão fora da planilha continua como estava (docs/26:62-63) |
| **Cancelamento/bloqueio em massa** | operação separada, **não** "o que não está na planilha": arquivo de uma coluna `codigo` (ou colagem), motivo único obrigatório, prévia com contagem, confirmação digitada, Administrador do Windows; gera um `import_batch` do tipo `bloqueio_em_massa`, desfazível |
| "Substituir a lista inteira" | **não oferecer** na fase 3 (é o que a sincronização completa da nuvem faz; duas fontes apagando uma à outra é o conflito do docs/29:81) |
| Desfazer a última importação | `import_batch_row.before_json` (+ cópia do `holder_enc` anterior) por linha alterada; desfaz só linhas **sem uso posterior** (`used_count` e `last_used_at` iguais aos do momento da importação); as demais ficam listadas (docs/26:64-65) |
| Padronizar `external_ref` | **usar o código normalizado** como `external_ref` em importação, cadastro manual, balcão (já é assim, `RepositorioDeIngressos.cs:633-636`) **e sincronização** — hoje `FonteDeCartoesDoPainel` usa o **bruto** (`FonteDeCartoesDoPainel.cs:234`); com perfil que completa zeros, o mesmo cartão vindo da nuvem e do balcão teria referências diferentes e colidiria consigo mesmo (`RepositorioDeIngressos.cs:281`). Com o perfil `raw` de hoje não aparece |

Esquema proposto:

```sql
CREATE TABLE import_batch (
    id TEXT NOT NULL PRIMARY KEY,               -- UUIDv7
    kind TEXT NOT NULL CHECK (kind IN ('cartoes','tipos','bloqueio_em_massa','painel_csv')),
    provider_id TEXT NOT NULL REFERENCES ticket_provider (id),
    file_name TEXT NOT NULL, file_sha256 TEXT NOT NULL, file_bytes INTEGER NOT NULL,
    encoding TEXT NULL, separator TEXT NULL, mode TEXT NOT NULL,
    rows_total INTEGER NOT NULL, rows_new INTEGER NOT NULL, rows_changed INTEGER NOT NULL,
    rows_same INTEGER NOT NULL, rows_error INTEGER NOT NULL, rows_conflict INTEGER NOT NULL,
    reason TEXT NULL, requested_by TEXT NOT NULL, workstation TEXT NOT NULL,
    previewed_at TEXT NOT NULL, applied_at TEXT NULL, undone_at TEXT NULL, undone_by TEXT NULL,
    status TEXT NOT NULL CHECK (status IN ('previa','aplicando','aplicada','falhou','desfeita','descartada'))
) STRICT;
CREATE TABLE import_batch_row (
    batch_id TEXT NOT NULL REFERENCES import_batch (id), line INTEGER NOT NULL,
    ticket_id TEXT NULL, outcome TEXT NOT NULL,        -- novo|mudou|igual|erro|conflito
    before_json TEXT NULL, used_count_at_apply INTEGER NULL,
    PRIMARY KEY (batch_id, line)
) STRICT;
-- Sem código nem titular em claro aqui: a linha aponta para o ticket; os erros ficam no
-- arquivo de devolução, que é do operador.
```

### 3.8 Limites de tamanho (evento de 30 mil)

| Limite | Valor | Por quê |
|---|---|---|
| Linhas por arquivo | aviso > 100.000; recusa > 200.000 | 30 mil é o caso de estudo (docs/14); ~80 mil QRs por edição da Zet (docs/30:36); docs/29:80 pede teste com 100 mil |
| Tamanho do arquivo | ≤ 20 MB (CSV) / ≤ 20 MB compactado (xlsx) | 100 mil linhas × ~100 B ≈ 10 MB |
| Linha | ≤ 4 KB | linha anômala = arquivo errado |
| Prévia na tela | contagens + 200 por grupo, lista virtualizada | a tela não carrega 30 mil linhas de erro |
| Memória | parsing em streaming; o conjunto de códigos existentes carregado uma vez num `HashSet<string>` (30 mil strings ≈ poucos MB) em vez de uma consulta por linha | `Ingerir` faz 2 SELECTs + 1 upsert **por item**, com comando novo a cada vez (`RepositorioDeIngressos.cs:279`, `950-957`) |

### 3.9 Transação, rollback e desempenho no SQLite — o ponto delicado

O "tudo ou nada numa transação só" do docs/26:53-54 colide com o caminho da catraca:

- o consumo é uma **escrita** (`UPDATE ticket …`, `RepositorioDeIngressos.cs:969-995`);
- o SQLite tem **um escritor por vez**; o WAL não bloqueia leitores, mas bloqueia outro
  escritor, que espera até `busy_timeout = 5000 ms` (`SqliteConnectionFactory.cs:55-56`);
- o orçamento da decisão local é **T1 = 150 ms** (docs/04:120), e a falha de base vira
  negativa `FALHA_NA_BASE_LOCAL` (`ReasonCodes.cs:59-60`).

Uma importação de 30 mil linhas numa transação única, com `synchronous=FULL`
(`SqliteConnectionFactory.cs:53`), dois índices únicos e 30 mil `credential_event`, segura
o escritor por um tempo que **precisa ser medido** (estimativa de ordem de grandeza:
centenas de ms a ~1 s em SSD; **não medido**). Durante esse tempo, toda leitura em
qualquer catraca espera — ou, no pior caso, é negada.

**Proposta (duas fases, atomicidade lógica garantida):**

1. **Prévia sem escrita:** parsing, validação e diff em memória, com leitura em
   instantâneo do WAL (não bloqueia ninguém).
2. **Aplicação:**
   - **Fora da janela de operação** (sem catraca em `Operando` nos últimos N minutos, ou
     modo evento desligado): **uma transação**, comandos preparados e reaproveitados
     (`INSERT … ON CONFLICT DO UPDATE` com parâmetros ligados uma vez), eventos em lote.
     Falha = `ROLLBACK`, `import_batch.status='falhou'`, nada mudou.
   - **Durante a operação:** fatias de **500–1.000 linhas por transação** (cada uma curta),
     `import_batch.status='aplicando'`; se uma fatia falhar, o serviço **desfaz as fatias
     já aplicadas** pelo mesmo mecanismo do "desfazer" antes de responder, e o lote termina
     `falhou`. O operador vê "tudo ou nada" — a atomicidade é do lote, não da transação.
   - Um lote por vez (trava no serviço); o worker nunca importa.
3. **Critério de aceite (a medir, nosso, não Topdata):** `LOAD-IMPORT-01` — importar 30
   mil (e 100 mil) linhas com o simulador lendo 10 códigos/s em 4 catracas; p95 da decisão
   continua < 150 ms e nenhuma `FALHA_NA_BASE_LOCAL`. O tamanho da fatia sai desse teste.
4. Depois de aplicar: `PRAGMA optimize`; `wal_checkpoint(PASSIVE)` fica com o SQLite.

### 3.10 Tipos (aba Tipos)

Processada **antes** dos cartões, no mesmo lote. Cria tipo novo, atualiza nome/ordem/ativo;
**nunca apaga** tipo (relatórios antigos dependem dele — docs/26:28). Tipo marcado `NAO`
passa a **negar** o uso — isso exige o decisor olhar o tipo, o que ele não faz hoje (a
categoria é só texto na linha do `ticket`). Proposta: motivo novo `TIPO_INATIVO` no
catálogo (`ReasonCodes.cs`; acrescentar é compatível, renomear não), e a nuvem guarda
motivo desconhecido como veio (docs/31:169-170).

### 3.11 Modelo de planilha para baixar

- Botão "Baixar modelo" na tela de importação copia `installer/modelos/…` (já instalados,
  docs/26:8-14) para onde o operador escolher. A aba **Cartões** vem vazia (docs/26:95-97).
- Acrescentar ao modelo (PROPOSTA): na aba Instruções, o parágrafo "como colar sem perder
  zeros" (Colar especial → Valores, com a coluna já em Texto) e "nunca abrir o CSV do
  painel no Excel e salvar de novo" (docs/31:185-186).
- A validação de `ativo` na aba Tipos só vai até D200 (`sheet2.xml`), e as de Cartões até a
  linha 100.000 — suficiente, mas o importador não depende delas.
- O teste existente (`ModelosDePlanilhaTests.cs:20-50`) ganha: coluna A em Texto,
  validações presentes, aba Cartões vazia.

### 3.12 O que vai para a nuvem (docs/31)

O contrato v1 tem **dois sentidos e nenhum para cadastro subindo**: cartões **descem**
(`middleware-sync-cards`, docs/31 §3) e tentativas **sobem** (`middleware-sync-events`,
docs/31 §4). A nuvem é dona do cadastro (docs/31:40; ADR-0023:32-34).

| Dado local | Vai para a nuvem? | Como |
|---|---|---|
| Tentativas de cartões cadastrados/importados localmente | **sim**, já vai | `card_id` + `admission_type` (tipo fotografado) + `extra` (docs/31:133-153) |
| O cadastro em si (código, tipo, validade) | **não no v1** | PROPOSTA de contrato **v2** (`middleware-register-cards`), só se o cliente quiser a nuvem espelhando a contingência; exige combinar antes (docs/31:245-250) |
| Titular, observação | **nunca** | minimização; a própria resposta da nuvem deve parar de mandar nome (docs/31:24,124) |
| `credential_event` / lotes de importação | opcional, **sem** código em claro e sem titular | outbox prioridade 9 ("histórico", `001_…:77`), só com máscara e contagens |
| Bloqueio local | não sobe no v1; **vence localmente** até ser desfeito | regra de dono (§1.6): bloqueio da nuvem sempre vence; bloqueio local não é desfeito pela sincronização |

Consequência de desenho: a sincronização completa (`full_sync`) da nuvem reescreve
`category`, `valid_from/to`, `max_uses` (`RepositorioDeIngressos.cs:913-920`). Para o
cadastro local não ser atropelado, o upsert da sincronização passa a respeitar
`owner_of_fields` (cartão `local` não recebe campos de cadastro da nuvem; recebe só
cancelamento/bloqueio). Sem isso, importar localmente e depois ligar a nuvem desfaz a
importação em silêncio.

---

## 4. Gravação na catraca (lista de acesso off-line / contingência)

**Nada disto está implementado.** O adapter não chama nenhuma função de lista (nenhuma
referência a `InserirUsuarioListaAcesso`/`EnviarListaAcesso` fora do código gerado de
interop); o que existe é o **avaliador de capacidade** que recusa o plano antes do envio
(`LimitesDeCapacidade.cs:97-127`, testes em `tests/Unit/LimitesDeCapacidadeTests.cs`).

### 4.1 Quando faz sentido

| Nível (ADR-0017:15-20; docs/15:71-76) | Quem decide | Lista na catraca serve? |
|---|---|---|
| T0/T1 — internet fora é o **regime normal** | borda (SQLite) | **não** — nada muda para quem passa |
| **T2 — PC/serviço caiu, catraca viva** | catraca, pela lista gravada | **é o único caso** |
| T3 — catraca isolada | política do portão (ADR-0013) | não |

E hoje T2 **nem acontece**: a mudança automática para off-line está desligada — "se o
programa parar, a catraca para de liberar" (docs/21:261-262). Ligar exige
`HabilitarMudancaOnLineOffLine` + `PingOnline` (funcoes-easyinner.csv:6,29-30) **e** a
decisão B4 fail-safe × fail-secure do responsável pela segurança (docs/29 E3; ADR-0013).
Portanto a lista é **fase posterior** e depende de B4.

### 4.2 O que a lista da catraca **não** sabe fazer (perda de regra em T2)

| Regra da borda | Existe na lista do equipamento? | Consequência em T2 |
|---|---|---|
| Uso único / `max_uses` | não há contador por usuário documentado (`InserirUsuarioListaAcesso(Cartao, Horario)`, csv:35) | QR online passa quantas vezes for apresentado |
| Intervalo de reuso (4–5 min) do cartão da bilheteria | não | cartão passado pela grade entra de novo |
| `SomenteNaUrna` (cartão só na fenda) | não documentado por leitor | `A_CONFIRMAR_COM_TOPDATA` — se o leitor da frente ler proximidade, a pessoa passa **com** o cartão |
| Validade por **data** (ingresso de um dia, docs/30:38,82) | só **tabelas de horário semanais** 1–100 (`InserirHorarioAcesso(Horario, DiaSemana, FaixaDia, Hora, Minuto)`, csv:38) | um ingresso de sábado vale em todo sábado → só carregar a lista **do dia** |
| Tipo/categoria, prestação de contas | não | bilhetes off-line (`ColetarBilhete`, csv:40) sem segundos e sem categoria; reconciliação na volta |
| Bloqueio imediato | `102 = sempre negado` existe, mas **reenviar a lista inteira** a cada mudança (csv:36) | revogação chega com atraso de um envio completo |

Conclusão prática: a lista em T2 é **lista branca "do dia" com regras simples**, e o
cartão reutilizável da bilheteria é o **pior candidato** (perde reuso e urna). Para ele,
PROPOSTA: em T2, **não** carregar cartões de bilheteria, ou carregá-los só se a bancada
provar que o leitor da frente não lê proximidade (`A_CONFIRMAR_COM_TOPDATA`).

### 4.3 Limites por modelo

| Fonte | Modelo/escopo | Lista | Observação |
|---|---|---|---|
| `limites-de-capacidade.csv:2` | Controle Catraca / Inner Acesso (produção atual) | **15.000** | qualquer quantidade de dígitos — FONTE_PRIMARIA |
| `limites-de-capacidade.csv:3` | idem, cartão de 16 dígitos | **14.900** | FONTE_PRIMARIA |
| `limites-de-capacidade.csv:4` | Inner Plus / Inner Net (descontinuados) | 5.000 a 15.000 | 10d=7.500, 12d=6.425, **14d=5.625**, 16d=5.000 |
| `limites-de-capacidade.csv:5` | Placa antiga (até 1999) | 1.125 a 3.000 | fora de escopo |
| `modelos.csv:9` | Coletor Urna 4 | 15.000 (4 a 16 dígitos) | FONTE_PRIMARIA |
| **`modelos.csv:2`** | **Catraca Fit 4 (TopFit 4, o modelo do evento)** | **LACUNA** | `A_CONFIRMAR_COM_TOPDATA` — docs/11 §5 ("capacidade real de lista e de bilhetes por modelo → ensaio de bancada"); ensaio **B-09 Off-line: "Lista no limite"** (docs/09:38) |
| `limites-de-capacidade.csv:6` | Marcações (Inner Acesso, Catraca 3) | 30.000 | coleta contínua; marcações por passagem 1–3 `A_CONFIRMAR_COM_TOPDATA` (csv:7) |

O código modela só "produção atual / Plus-Net / antiga" (`LimitesDeCapacidade.cs:9-19`);
falta a entrada "Linha 4" até a bancada dizer em qual placa a TopFit 4 cai.

Dimensionamento com os números do repositório:

| Conjunto | Tamanho | Cabe em 15.000? |
|---|---|---|
| Cartões da bilheteria (cadastro 2025) | 2.243 (docs/22:349) | sim (mas ver §4.2) |
| QR online do **dia de pico** | 4.215 (docs/30:39) | sim |
| QR online da **edição** | ~80 mil (docs/30:36) | **não** → só a lista do dia |
| Evento de 30 mil pessoas | 30.000 | **não** → lista negra ou subconjunto (docs/14 §2; ADR-0017:22-24) |
| Evento limitado a 12 mil/dia | 12.000 | sim, com 20% de folga (docs/14 §7.1) |

### 4.4 Carga completa × incremental

- **Não existe incremental no equipamento:** `EnviarListaAcesso` **sobrescreve** a lista
  e "alterar 1 usuário exige reenviar a lista inteira" (csv:36); o envio **trava a
  catraca** enquanto roda (docs/14:68-69; `LimitesDeCapacidade.cs:37-39`).
- "Incremental" do nosso lado = **só reenviar quando o conteúdo mudar**: a borda calcula a
  lista desejada por catraca, faz o hash (ordenado) e compara com o último enviado com
  sucesso (`device_list_version`: hash, quantidade, tipo branca/negra, enviado_em,
  enviado_por, retorno nativo). Mudou → agenda envio.
- **Janela:** fora do pico, **uma catraca por vez**, nunca duas do mesmo corredor juntas;
  bloqueio emergencial (revogação) tem caminho expresso (docs/15:236-237) mas ainda é um
  envio completo.
- Sequência (a partir do manual, com as lacunas marcadas): `DefinirTipoListaAcesso` →
  `InserirHorarioAcesso`… → `EnviarHorariosAcesso` (deve preceder a lista, csv:39) →
  `ApagarListaAcesso` (**parâmetro e envio automático divergem no manual**, csv:37; pauta
  Topdata item 7, docs/08:97) → `InserirUsuarioListaAcesso` × N (retornos 128/129/130,
  csv:35) → `EnviarListaAcesso`.
- Formato do código na lista: "string conforme padrão-dígitos" (csv:35) — tem de ser o
  **mesmo texto que a catraca lê** (perfil do provedor). Dígitos variáveis na lista:
  `A_CONFIRMAR_COM_TOPDATA`.
- **Cartão master** desligado (burla a lista — csv:24; docs/14:107-108).

### 4.5 Verificação

Não há função documentada para **ler de volta** a lista (nenhum `Receber…Lista` na matriz).
Então:

1. Conferir o retorno de cada `InserirUsuarioListaAcesso` e do `EnviarListaAcesso`; contar
   inseridos × planejados; qualquer 128/129/130 aborta e registra.
2. Registrar `device_list_version` só com retorno 0.
3. **Prova funcional só na bancada/comissionamento**, nunca no evento: com o serviço
   parado e a mudança automática ligada, apresentar um cartão da lista (libera), um fora
   (nega) e um `102` (nega); medir o tempo de envio de 2 mil e de 15 mil usuários.
4. Na volta de T2: `ColetarBilhete` contínuo, commit antes do próximo (CHAOS-REC-01,
   docs/11:104-106), dedupe pelo tipo 128 (tipos-bilhete.csv:18) e pela chave
   `(device, boot, seq)` (ADR-0009).

### 4.6 O que depende da Topdata (com o ensaio que decide)

| Pergunta | Selo | Ensaio |
|---|---|---|
| Capacidade da lista na **TopFit 4**, por nº de dígitos (10, 12, 14) | `A_CONFIRMAR_COM_TOPDATA` | B-09 "Lista no limite" (docs/09:38) |
| `ApagarListaAcesso` com ou sem `Inner`; envia sozinho? | `A_CONFIRMAR_COM_TOPDATA` | pauta item 7 (docs/08:97) + INT-OFF-03 |
| Tempo de `EnviarListaAcesso` e se a catraca atende durante o envio | `A_CONFIRMAR_COM_TOPDATA` | B-09 cronometrado |
| Lista respeita leitor 1 × leitor 2 (cartão só na urna em T2)? | `A_CONFIRMAR_COM_TOPDATA` | B-08 + B-09 combinados |
| Formato do cartão na lista com dígitos variáveis (12 e 14 juntos) | `A_CONFIRMAR_COM_TOPDATA` | B-05 + INT-OFF-03 |
| Marcações por passagem em off-line (1 a 3) | `A_CONFIRMAR_COM_TOPDATA` (csv:7) | B-09 "bilhetes" |
| Comportamento em T3 sem PC e sem energia | decisão B4 (cliente) + `A_CONFIRMAR_COM_TOPDATA` | B-11 energia cortada |

---

## 5. LGPD e segurança

> Premissa: nenhum dado real entra no repositório (docs/22:245-246, 345-347, 427-428;
> docs/30:8-11); testes da nuvem só com cartões fictícios em projeto de teste
> (docs/31:227-228). O enquadramento jurídico abaixo é **proposta técnica** — a base legal
> final é do encarregado/jurídico do cliente (`A_CONFIRMAR`, docs/29 §5 linha 139, Z7).

### 5.1 Inventário e base legal por tipo de dado

| Dado | Onde fica | É dado pessoal? | Base legal proposta (Lei 13.709/2018) | Necessário para decidir? |
|---|---|---|---|---|
| Número do cartão de bilheteria **sem titular** | `ticket.qr_normalized` | pseudônimo fraco: só vira pessoal quando cruzado com venda nominal | execução de contrato (art. 7º, V) — a entrada vendida | **sim** |
| Voucher/QR online | `ticket` | **sim** para a Zet (liga ao comprador); pseudônimo aqui (o tradutor não lê nome/CPF — docs/30:86-87) | execução de contrato (art. 7º, V) | **sim** |
| Tentativas (código, hora, catraca, motivo, giro) | `ticket_use_attempt`, `access_decision`, outbox | sim, quando o código é ligável a alguém | execução de contrato + legítimo interesse em segurança e prevenção de fraude (art. 7º, IX) + prestação de contas | sim (fato da borda) |
| **Titular** (nome) de cartão pessoal | proposta `ticket.holder_enc` | **sim** | execução de contrato; só quando o cartão é nominal (docs/26:36) | **não** — só para a consulta no balcão |
| **Tipo quando revela saúde ou idade** ("PCD/Autista", "Acompanhante PCD", "Idosos", "Crianças de 06 a 12" — grafias vistas na Zet, docs/30:46) | `ticket.category`, `ticket_use_attempt.category` | **dado sensível (saúde, art. 5º, II)** se ligado a pessoa identificada | art. 11 — só agregado; **proibir** titular preenchido nesses tipos | não para decidir; sim para relatório **agregado** |
| Tipo "SOCIAL" | idem | pode indicar vulnerabilidade social | minimização: só agregado | idem |
| Observação | proposta `ticket.note` | pode virar pessoal por descuido | só texto operacional; filtro de CPF/e-mail/telefone (docs/26:41) | não |
| Nome digitado do operador | `operator_command.requested_by`, `edge_setting.updated_by`, `ticket_sale.operator`, proposta `credential_event.actor` | **sim** (empregado/prestador) | legítimo interesse/obrigação de auditoria | sim (trilha) |
| Conta Windows e máquina | proposta `credential_event.workstation` | sim | idem | sim |
| CPF, e-mail, telefone | **não coletados** (docs/26:43-46; Zet: docs/30:86-87; nuvem: docs/31:24,124) | — | — | **não** |
| Corpo bruto dos webhooks da Zet | relé `delivery.body` (`Relay.Ingressos/ArmazenamentoDeEntregas.cs:55-78`) | **sim** — o webhook traz nome, CPF, e-mail, telefone (docs/30:8-11) | execução de contrato, **com retenção curta** | não — a borda usa só voucher, data, tipo |
| Template biométrico / face | não existe | **sensível** (art. 5º, II; art. 11) | ver §5.6 | — |

### 5.2 Minimização (o que já está certo e o que ajustar)

| Regra | Estado |
|---|---|
| Cartão de bilheteria **não precisa de nome**; titular opcional e vazio no cartão reutilizável | DOC (docs/26:36,43-46) — manter; a importação do CSV do painel **descarta** o titular (§3.2) |
| Nuvem não deve mandar nome/CPF na lista de cartões | pedido P1 (docs/31:24,124); a borda **já ignora** `customer_name` (lê só `card_number`, validade, usos, `admission_type` — `FonteDeCartoesDoPainel.cs:212-244`) |
| Tradutor da Zet não lê dado pessoal; CPF nulo não derruba ingresso | FEITO (docs/30:86-87) |
| Tipos sensíveis (PCD, idoso, criança) | PROPOSTA: `ticket_type.sensitive=1` → titular proibido, relatório só agregado com supressão de célula pequena (< 5) |
| Relé guarda o corpo inteiro com dado pessoal, sem expurgo possível (trigger proíbe `DELETE`, `ArmazenamentoDeEntregas.cs:72-76`) | **Conflito com retenção.** PROPOSTA: um arquivo SQLite **por evento** no relé, apagado inteiro no fim da retenção (com registro), ou gatilho de `DELETE` que só aceita `received_at < corte_de_retencao` gravado numa tabela de controle |

### 5.3 Mascaramento em tela e em log

Regra do enunciado: *"Logs estruturados sem número completo de cartão, face, digital,
senha ou segredo."*

| Superfície | Hoje | Proposta |
|---|---|---|
| `CredentialValue.ToString()` | mascarado: `cred:****NN(len)`; ≤ 4 chars não mostra nada (`CredentialValue.cs:63-87`) | usar **sempre** o tipo, nunca `string` crua, em log e em mensagem de erro |
| Consulta no painel | código mascarado, campo apagado após consultar (`Telas.cs:441-444,467-468,487`; `ConsultasDaOperacao.cs:248`) | titular mostrado **só** as iniciais; "Revelar titular" = Administrador do Windows + motivo + `credential_event(titular_revelado)` |
| Prévia/relatório de importação | não existe | linha + motivo + código mascarado; código completo só no arquivo de devolução do operador (§3.6) |
| Relatórios R1–R8 | agregados; "nunca o número completo nem o titular" (docs/25:112-113) | idem; R6 lista códigos **mascarados** (docs/25:79-81) |
| Alertas | "nenhum alerta pode trazer o número de cartão completo" (docs/29:102) | idem |
| Log estruturado | redator no serializador: campos com nome sensível viram `[REDIGIDO]`; texto livre perde sequências de ≥ 6 dígitos e foto Base64 (`RedatorDeDadoSensivel.cs:28-38,41-51,103,111`); teste `SEC-LOG-01` (`tests/Integration/SecLog01Tests.cs:62`) | **três lacunas** abaixo |

Lacunas do redator (achadas lendo o código, não testadas):

1. **Nome de campo por igualdade exata** (`CamposSensiveis.Contains(nome)`,
   `RedatorDeDadoSensivel.cs:65`). `card_number`, `card_id`, `customer_name`,
   `codigo`, `code`, `qr`, `voucher`, `titular`, `holder`, `email`, `telefone`, `phone`,
   `observacao` **não estão** na lista (ela tem `cardnumber`, `nome`, `name`…,
   `RedatorDeDadoSensivel.cs:31-37`). Propor: normalizar o nome (minúsculas, sem `_`/`-`)
   e acrescentar esses termos, ou casar por "contém".
2. **Código alfanumérico** (QR com letras, UID Mifare em hexadecimal — ex. fictício
   `04A1B2C3`) não casa com `\d{6,}` (`RedatorDeDadoSensivel.cs:103`). Propor padrão
   adicional para `[0-9A-Fa-f]{8,}` isolado, excluindo UUID com hífens e carimbo ISO.
3. **Códigos de 4–5 dígitos** passam por decisão consciente (piso 6 para não mutilar
   diagnóstico, `RedatorDeDadoSensivel.cs:93-96`). Aceitável **se** o código nunca for
   interpolado em texto livre — regra de revisão + teste que injeta um código de 4
   dígitos por um caminho de log e exige máscara.

### 5.4 Criptografia em repouso (DPAPI) para o que for sensível

ADR-0014 decide: segredos em DPAPI escopo máquina; dado pessoal sensível cifrado em
repouso; biometria segregada com chave distinta; backup cifrado; SQLCipher recusado
(ADR-0014:11-19,31-32). Hoje só o segredo da nuvem usa DPAPI (`CofreDeSegredos.cs:23-68`).

| Dado | Proposta |
|---|---|
| `holder_enc`, `note` (se contiver algo pessoal) | **Envelope:** chave de dados AES-256-GCM **por evento**, gerada na criação do evento e guardada cifrada com DPAPI `LocalMachine` + entropia, em arquivo com ACL só SYSTEM/Administradores (mesmo desenho do `CofreDeSegredos`); o valor no banco = `nonce ‖ cifrado ‖ tag`, com `ticket.id` como dado associado (impede copiar o cifrado de um cartão para outro) |
| Chave HMAC de `credential_event.code_hash` | outra chave por evento, mesmo cofre |
| Número do cartão | **fica em claro** no `ticket` (a comparação de 150 ms é por índice sobre texto, ADR-0008:21) — protegido pela ACL da pasta de dados e por não estar ligado a nome; nunca em log |
| Backup | não existe (docs/29:131); quando existir, cifrado e **sem** a chave junto (ADR-0014:24-25) |
| Migração de host | DPAPI prende à máquina: procedimento documentado de exportar/importar a chave do evento sob Administrador (ADR-0014:26-27) |

Vantagem do envelope por evento: **expurgo por destruição da chave** (crypto-shredding) —
apagar a chave do evento torna ilegíveis o titular e os HMAC também em cópias e backups
esquecidos.

### 5.5 Retenção e expurgo após o evento

Prazos **propostos** para o jurídico validar (`A_CONFIRMAR`):

| Dado | Prazo proposto | Como expurgar | Verificação |
|---|---|---|---|
| Titular e observação | fim do evento + 30 dias | `UPDATE ticket SET holder_enc=NULL, note=NULL` + destruir chave de dados + `VACUUM` | `credential_event(expurgado)` com contagem; relatório de expurgo |
| Número do cartão nas tentativas e decisões | até fechar a prestação de contas (corte com hash, docs/25:108-110) + 90 dias | substituir por HMAC com chave destruída depois (pseudonimização irreversível), mantendo agregados e o hash do corte | refazer o R7 com o mesmo corte e conferir o hash |
| Outbox já enviada / cartas mortas resolvidas | 7 dias após envio | `DELETE … WHERE sent_at < …` | contagem |
| Relé `delivery` (dados da Zet) | fim do dia de operação + 7 dias | arquivo por evento apagado (§5.2) | registro de expurgo |
| Arquivos de planilha do cliente | não ficam no sistema (lidos do caminho escolhido; nada copiado) | — | — |
| `operator_command`, `credential_event`, `audit_log` | 5 anos ou conforme política da empresa (`A_CONFIRMAR`) | nome do operador pseudonimizado após o prazo | cadeia de hash continua verificável |
| Backups de CSV da nuvem e da Zet (fora do repositório) | prazo de descarte definido pelo encarregado (Z7, docs/30:118) | fora do sistema | — |

Expurgo = **caso de uso** com Administrador + motivo + confirmação digitada, gera
`audit_log`; **nunca** automático sem aviso. O sistema mostra "dados pessoais do evento X
serão expurgados em N dias".

### 5.6 Direitos do titular e prestação de contas

- **Relatório ao titular** (docs/29:139): por código apresentado pelo próprio titular
  (cartão/QR em mãos), listar cadastro (tipo, validade, situação) e tentativas — gerado por
  Administrador, registrado; cartão ao portador sem titular não tem titular a atender.
- **Correção/eliminação**: editar/apagar titular pelo cadastro manual (trilha registra
  "titular alterado", não o valor).
- **Exportação da prestação de contas**: agregada, mascarada, com cabeçalho e hash de corte
  (docs/25 §4); CSV já existente protege contra injeção de fórmula (`Telas.cs:674-680`).
- **Incidente**: se a nuvem tiver exposto `authorizations` pela chave `anon`, é incidente de
  dado pessoal a avaliar (docs/31:58-59; docs/22:444-463) — fora da borda, mas o XAcess
  não usa a chave `anon` nem as tabelas (docs/22:441-442).

### 5.7 Biometria e facial — **PROPOSTA FUTURA**, com requisitos de entrada

Estado: fora do escopo até E6 (docs/29:35, "só com base legal"); ADR-0014 exige proteção
forte, minimização, base legal e retenção; o esquema previsto é `biometrics.db` separado
(docs/05:248-260). **Recomendação: não habilitar enquanto não houver login real**, porque
a exportação/exclusão biométrica exige **duas pessoas** (docs/05:227-232; comandos
destrutivos do facial, docs/13 §7) e, sem login, "duas pessoas" são dois nomes digitados.

Requisitos para entrar (todos obrigatórios):

| # | Requisito | Fonte |
|---|---|---|
| 1 | Base legal do art. 11 escrita pelo jurídico (consentimento específico e destacado, ou hipótese do art. 11, II, "g"), e **RIPD** (art. 38) | ADR-0014; docs/15:102-105 |
| 2 | **Caminho alternativo sem biometria** (cartão/QR) para quem não consentir, sem prejuízo | minimização |
| 3 | Aviso visível no local **antes** da primeira pessoa chegar; o `sendlog` do facial manda **foto de desconhecidos** em Base64 | docs/14:225-229; docs/13:175 |
| 4 | Guardar **template**, nunca foto; foto de cadastro descartada após gerar o template | ADR-0014 |
| 5 | `biometrics.db` separado, chave própria (envelope DPAPI distinta), fora do backup operacional; nunca em log (redator já corta foto Base64 e campos `template/face/biometria`, `RedatorDeDadoSensivel.cs:35-36,111`) | docs/05:250-260 |
| 6 | Preferir **casamento no equipamento** (template na catraca/leitor), com a borda recebendo só o número de cartão (fluxo facial + Inner: o leitor manda número de cartão, docs/11:123-127) | docs/15:96-100 |
| 7 | Retenção = fim do evento + prazo curto; expurgo **no equipamento também** (`deleteuser`/`cleanuser` do facial), com prova | docs/13 §7; ADR-0014:19 |
| 8 | Login com papéis + aprovação em duas pessoas para exportar/limpar | docs/05:227-232; docs/27:234 |
| 9 | Nenhum template à nuvem | ADR-0023 (nuvem não decide) |
| 10 | Digital na TopFit 4 (`EnviarDigitalUsuarioBio`) é **LACUNA** no manual | funcoes-easyinner.csv:59 (EI-058); `A_CONFIRMAR_COM_TOPDATA`, B-10 (docs/09:39) |

---

## 6. Casos de teste

Convenções do repositório: nomes em português descrevendo o comportamento
(`tests/Unit/Credentials/CredentialValueTests.cs:16-120`), integração com banco temporário
(`tests/Integration/BancoTemporario.cs`), e **violar a trava de propósito** para provar que o
teste pega (docs/19:385-386; docs/30:95). Todo código abaixo é fictício (`9999…`, `0000000101`).

### 6.1 Unidade — normalização e validação do código (`Access.Domain`)

| # | Caso | Entrada (fictícia) | Esperado |
|---|---|---|---|
| U-01 | Zeros à esquerda preservados no perfil `raw` | `0000000101` | normalizado `0000000101`, ≠ `101` |
| U-02 | Perfil que completa zeros | `99994567` em perfil pad 10 | `0099994567` e aviso "completado" |
| U-03 | Perfil **nunca** trunca | 11 dígitos em perfil 10 | recusa por comprimento (já existe o espírito em `CredentialValueTests.cs:45`) |
| U-04 | Notação científica | `9,99999E+11`, `9.99999E+11`, `1E+15` | erro `NOTACAO_CIENTIFICA` |
| U-05 | Número com `,0`/`.0` | `999900000101.0` | erro |
| U-06 | Espaços nas pontas | `␠9999000101␠` | aceito, `Trim` |
| U-07 | Espaço interno, ponto, traço, vírgula | `9999 000101`, `9999.000101`, `9999-000101` | erro "caractere que a catraca não lê" (docs/26:73) |
| U-08 | NBSP / zero-width nas pontas | ` 9999000101​` | aceito com aviso |
| U-09 | Apóstrofo de texto forçado | `'0000000101` | aceito como `0000000101`, com aviso |
| U-10 | Comprimento 3 e 17 | `999`, 17 dígitos | erro (teto 4–16) |
| U-11 | QR com letras em perfil de cartão numérico | `ABC1234567` | depende do perfil; cartão Mifare → erro |
| U-12 | Mensagem de erro nunca contém o código | qualquer recusa | `mensagem` não contém a sequência; contém o comprimento |
| U-13 | `CredentialValue.ToString` de código com letras | `04A1B2C3` fictício | `cred:****C3(8)` |
| U-14 | Equivalência por zeros | `0000000101` × `00000000000101` | "possível duplicata" = verdadeiro; **não** iguais |
| U-15 | Observação com CPF/e-mail/telefone | `000.000.000-00` fictício, `a@b.c`, `(11) 99999-0000` | erro |
| U-16 | Titular com sequência de 6+ dígitos | `Fulano 123456` | erro |
| U-17 | Validade | `31/02/2026 10:00`; fim antes do início; `2026-12-05` | erro nos três |
| U-18 | Validade em Brasília → UTC | `05/12/2026 18:00` | `2026-12-05T21:00:00Z` |
| U-19 | `usos_maximos` | vazio → sentinela; `0`, `-1`, `1,5` → erro | — |
| U-20 | Tipo inexistente / inativo | `VIP` | erro na linha |
| U-21 | Tipo sensível com titular | tipo `PCD` + titular | erro (§5.2) |

### 6.2 Unidade — leitor de CSV/XLSX (`Access.Application/Cartoes/Importacao`)

| # | Caso | Esperado |
|---|---|---|
| I-01 | UTF-8 **com BOM**, `;`, CRLF (igual ao modelo instalado) | lê; cabeçalho sem o BOM |
| I-02 | UTF-8 sem BOM | lê |
| I-03 | Windows-1252 com acento no titular | detecta, converte, aviso |
| I-04 | UTF-16 LE com TAB ("Texto Unicode") | lê |
| I-05 | Separador `,` | detecta pelo cabeçalho, aviso |
| I-06 | Primeira linha `sep=;` | ignora a linha, usa `;` |
| I-07 | LF puro, CR puro, mistura CRLF/LF | mesmas linhas; número de linha físico correto |
| I-08 | Linhas vazias no meio e no fim; linha `;;;;;;;` | ignoradas e contadas |
| I-09 | Campo entre aspas com `;` e quebra de linha na observação | um campo só; a linha seguinte continua com o número certo |
| I-10 | Cabeçalho em maiúsculas/espaços | aceito |
| I-11 | Coluna obrigatória faltando / repetida | arquivo recusado inteiro, antes de validar linhas |
| I-12 | Coluna extra desconhecida | aviso, ignorada |
| I-13 | Linha com colunas a mais/a menos | erro na linha |
| I-14 | Linha > 4 KB; arquivo > 20 MB; > 200 mil linhas | recusa com motivo |
| I-15 | XLSX: `codigo` como **célula numérica** | erro na linha, mesmo que o valor tenha 12 dígitos |
| I-16 | XLSX: `codigo` texto com zeros | aceito com zeros |
| I-17 | XLSX: `codigo` com fórmula | erro |
| I-18 | XLSX: data serial em `validade_inicio` (sistema 1900 e 1904) | convertida em horário de Brasília |
| I-19 | XLSX: abas Exemplo e Instruções preenchidas | ignoradas; nada do Exemplo é importado |
| I-20 | XLSX com macro/`.xlsm`, zip-bomb, link externo | recusa |
| I-21 | Código repetido no arquivo (inclusive `0000000101` e `␠0000000101`) | erro **nas duas** linhas |
| I-22 | Heurística de zeros perdidos: tipo com 50 códigos de 12 e 3 de 11 | aviso nos 3 |
| I-23 | Layout do CSV do painel | mapeado para docs/26; titular **descartado**; tipo ← categoria |
| I-24 | Arquivo de devolução | só as linhas com erro, colunas originais + `linha` + `erro`; célula iniciada por `=`/`+`/`-`/`@` neutralizada |

### 6.3 Unidade — cadastro manual (casos de uso)

| # | Caso | Esperado |
|---|---|---|
| C-01 | Criar válido | `ticket` + `credential_event(criado)` na mesma transação |
| C-02 | Criar código que existe em outro provedor | recusa "pertence a outro provedor" |
| C-03 | Criar sem operador / operador com 1 caractere | recusa (2–80) |
| C-04 | Editar `max_uses` abaixo de `used_count` | vira `used_count` |
| C-05 | Editar tipo de cartão já usado | uso antigo mantém o tipo antigo no relatório |
| C-06 | Editar cartão `owner_of_fields=nuvem` (tipo/validade) | recusa; observação/portões aceitos |
| C-07 | Bloquear sem motivo / motivo de 4 caracteres | recusa (domínio **e** trigger) |
| C-08 | Desbloquear cartão esgotado | volta a `consumido`, não a `valido` |
| C-09 | Cancelar e depois tentar reativar ingresso não reutilizável | recusa |
| C-10 | Consulta revela titular só para Administrador e registra `titular_revelado` | sem Administrador: iniciais |

### 6.4 Integração (SQLite real, `BancoTemporario`)

| # | Caso | Esperado |
|---|---|---|
| G-01 | **Trilha imutável**: `UPDATE`/`DELETE` em `credential_event` | `RAISE(ABORT)`; violar o trigger de propósito → o teste reprova |
| G-02 | Cadeia de hash: adulterar `before_json` por fora (trigger desligado no teste) | verificador acusa a posição |
| G-03 | Importar → reimportar **o mesmo arquivo** | 2ª prévia: 0 novos, 0 mudam, N iguais; nenhuma escrita, nenhum evento |
| G-04 | Reimportar com tipo alterado em 10 linhas | 10 "mudam"; tentativas antigas mantêm a categoria antiga (`ticket_use_attempt.category`) |
| G-05 | Linha ATIVO para cartão **bloqueado manualmente** | conflito; continua bloqueado |
| G-06 | Importação **nunca apaga**: cartão fora da planilha | intacto |
| G-07 | Falha no meio (erro forçado na linha 20.001 de 30.000) | nada mudou (transação única) ou fatias revertidas (modo fatiado); `import_batch.status='falhou'` |
| G-08 | Desfazer a última importação | volta ao estado anterior; cartões **usados depois** ficam e são listados |
| G-09 | Colisão com QR do Zet já ingerido | linha recusada; ingresso do Zet intacto (`ux_ticket_qr`) |
| G-10 | **`external_ref` normalizado**: cartão importado com perfil pad 10, depois o mesmo cartão vendido no balcão e vindo da nuvem | **uma** linha de `ticket`, sem colisão consigo mesmo (hoje `FonteDeCartoesDoPainel.cs:234` usa o bruto) |
| G-11 | **Normalização simétrica**: cadastro de 14 dígitos com perfil que completa zeros, leitura de 12 | casa (hoje o decisor compara cru, `DecisorDeIngresso.cs:88`) — **só depois da bancada** decidir o perfil |
| G-12 | Sincronização completa da nuvem sobre cartão `owner_of_fields=local` | tipo/validade locais preservados; `removed_cards` ainda cancela |
| G-13 | Tipo marcado inativo | próxima leitura nega com `TIPO_INATIVO`; relatório antigo mantém o tipo |
| G-14 | Bloqueio vale na próxima leitura | `TentarUsar` nega `Bloqueado` sem reiniciar nada |
| G-15 | Cancelamento em massa de 1.000 códigos | prévia com contagem; confirmação digitada; desfazível |
| G-16 | **SEC-LOG-01 estendido**: importar/cadastrar/bloquear com códigos fictícios de 4, 5, 10, 12, 14 dígitos e um alfanumérico, titular fictício | nenhum log/alerta contém código completo nem titular; campo `card_number`/`titular`/`codigo` redigido |
| G-17 | Expurgo | titular nulo, chave do evento destruída, `VACUUM`; `credential_event(expurgado)`; relatório R7 com o mesmo corte dá o mesmo hash |
| G-18 | Titular cifrado | coluna não contém o nome em claro (`instr(holder_enc, 'Fulano') = 0`); trocar `holder_enc` entre dois `ticket` falha na decifragem (dado associado) |

### 6.5 Carga e desempenho

| # | Caso | Critério |
|---|---|---|
| L-01 | **30.000 linhas** (xlsx e csv), 12 e 14 dígitos, 5 tipos, 1% de erros, 50 duplicados | prévia < 5 s; aplicação medida e registrada; memória < 200 MB |
| L-02 | **100.000 linhas** (docs/29:80) | idem, sem estourar a tela (lista virtualizada) |
| L-03 | `LOAD-IMPORT-01`: importar 30 mil **com o simulador lendo** 10 códigos/s em 4 catracas (modo simulação, docs/23) | p95 da decisão < 150 ms (T1, docs/04:120); **zero** `FALHA_NA_BASE_LOCAL`; o tamanho da fatia sai daqui |
| L-04 | Reimportar 30 mil iguais | nenhuma escrita (conferir `total_changes()`), tempo só de leitura |
| L-05 | Queda do serviço (`kill -9`) durante a aplicação | na volta: lote `falhou` ou `aplicada`, nunca meio aplicado sem registro (padrão `QuedaAbruptaTests.cs`) |

### 6.6 Bancada (dependem da Topdata/equipamento)

| # | Caso | Ensaio |
|---|---|---|
| H-01 | Mesmo cartão na catraca e no leitor do balcão dá o mesmo texto | docs/20 §7 ensaio 1; docs/21 passo 3 linhas 1 e 7 |
| H-02 | Quantos dígitos a TopFit 4 entrega para os cartões de 12/14/11 dígitos do estoque | docs/21 passo 3 linhas 8–12 |
| H-03 | Lista de acesso: capacidade na TopFit 4, tempo de envio, `ApagarListaAcesso` | B-09 |
| H-04 | Lista respeita urna × leitor da frente em off-line | B-08 + B-09 |

---

## 7. Ordem recomendada (resumo executivo)

1. **Antes de qualquer tela:** normalização simétrica (leitura = cadastro), `external_ref`
   normalizado também na sincronização, e dono por campo (`owner_of_fields`) — sem isso a
   importação e a nuvem se sobrescrevem (docs/29:81).
2. **Migração 010:** `ticket_type`, colunas novas de `ticket`, `credential_event` com
   triggers, `import_batch(_row)`.
3. **Cadastro manual** (criar/editar/bloquear/desbloquear/cancelar/consultar) com trilha.
4. **Importação** CSV → XLSX, prévia, devolução, idempotência, desfazer; `LOAD-IMPORT-01`
   decide transação única × fatias.
5. **LGPD mínima antes de cadastrar titular real** (docs/29:170): cifra do titular por
   evento, redator ampliado, prazos aprovados, expurgo. Enquanto isso não existir, **não
   aceitar a coluna `titular`** (importar vazia).
6. **Lista na catraca** só depois de B4 e da bancada B-09.
7. **Biometria/facial** só depois de login real, base legal do art. 11 e RIPD.

