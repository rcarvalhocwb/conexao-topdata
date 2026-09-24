# 13 — SDK do Leitor Facial e Catraca Easy

> **Fonte:** Manual WebSocket Facial, Comandos do Leitor Facial e Protocolo Facial API
> WebService — páginas oficiais do portal do integrador, lidas na íntegra.
> Salvo indicação contrária, tudo aqui é `FONTE_PRIMARIA`.

## 1. Quando este SDK é o certo

| Produto | SDK a usar |
|---|---|
| Leitor Facial **F4**, **T4**, **T4-50k** | **SDK Facial** |
| Catraca **Fit Easy**, **Revolution Easy**, **Box Easy** | **SDK Facial** — o próprio leitor libera o acesso |
| Catracas Linha Inner (Fit/Revolution/Box 4, PNE 4) | **EasyInner** |
| Catraca Inner **com leitor facial acoplado** | **Os dois**: Facial para cadastrar pessoas e rostos, EasyInner para o acesso e o giro |

**Nada disso passa pela EasyInner.dll.** O facial é WebSocket + JSON, não tem DLL, não é
x86 e não tem limite de 30 equipamentos. Roda em qualquer linguagem e em qualquer sistema
operacional ([ADR-0011](ADR/ADR-0011-facial-separado.md)).

## 2. Duas superfícies de integração, escolhas diferentes

O fabricante expõe **duas** APIs independentes:

| | **WebSocket** (`/suporte/manual-sdk-facial/`) | **Web API HTTP** (`/suporte/protocolo-facial-web-api/`) |
|---|---|---|
| Papel | O **leitor é o cliente**, nosso software é o servidor | Nosso software é o cliente, o **leitor é o servidor** |
| Porta | configurada no leitor (o SDK usa 7792) | 80 (ou personalizada) |
| Autenticação | nenhuma no protocolo | campo `password` em todo comando (exceto `getlang`) |
| Tempo real | **Sim** — `sendlog` assíncrono | Não — requisição/resposta |
| Decide acesso on-line | **Sim** (`access: true/false`) | Não |
| Comandos | 16 | 34 |
| Uso no produto | **Caminho principal** | Administração, diagnóstico, relatórios |

**Decisão:** o caminho de acesso usa **WebSocket**. A Web API entra como superfície
auxiliar (capacidades, firmware, relatórios, `opendoor` de emergência). Nunca as duas
decidindo acesso ao mesmo tempo.

## 3. Modos de operação

Configurado por `setdevinfo` → `server_verify`:

| Valor | Modo | Quem decide |
|---|---|---|
| 0 | Somente off-line | O leitor, pelos cadastros e horários locais |
| 1 | Somente on-line | **Nosso software**, respondendo `access: true/false` |
| 2 | Automático | On-line com fallback para off-line |

**Em qualquer modo é obrigatório cadastrar os usuários no leitor** — o reconhecimento
facial acontece no equipamento. No modo on-line o leitor identifica e **pergunta**; a
regra de negócio é nossa.

Para um evento, o modo **2** é o análogo do nível T1/T2 da nossa arquitetura de
degradação ([ADR-0017](ADR/ADR-0017-niveis-de-degradacao.md)).

## 4. Ciclo de conexão — e a armadilha do `reg`

1. O leitor é configurado com o IP do servidor e **tenta conectar periodicamente**.
2. Nosso servidor WebSocket aceita a conexão.
3. O leitor envia `reg` com número de série e um bloco `devinfo` completo.
4. **Nosso software precisa responder ao `reg`.**
5. Só então a comunicação começa. Eventos acumulados offline chegam logo em seguida.

> ⚠️ **Documentado no manual:** *"O aplicativo deve responder ao comando `reg`
> **obrigatoriamente** (…) Caso a resposta não seja enviada, a comunicação com o leitor
> facial será perdida."* Um `reg` sem resposta é uma catraca morta que parece viva.

O `devinfo` do `reg` é **capability discovery de graça**: `usersize`, `facesize`,
`cardsize`, `logsize`, `useduser`, `usedface`, `usedlog`, `firmware`, `mac`, `time`.
Dá para medir ocupação e drift de relógio na conexão, sem comando extra.

> Campos `fpsize`, `usedfp`, `fpalgo`, `intercom`, `floors`, `useosdp` são declarados
> **"não aplicável"** pelo fabricante. Ignorar.

## 5. `sendlog` — o evento que importa

Enviado **toda vez que um rosto é detectado**, cadastrado ou não, em qualquer modo.

```jsonc
{ "cmd": "sendlog", "sn": "…", "count": 1, "logindex": 0,
  "record": [ { "enrollid": 300, "name": "…", "time": "2025-02-26 11:40:19",
                "mode": 8, "inout": 0, "event": 0 } ] }
```

Resposta (obrigatória no modo on-line):

```jsonc
{ "ret": "sendlog", "result": true, "cloudtime": "…",
  "message": "Ingresso válido só a partir das 19h",   // exibido no leitor
  "access": false }                                    // ← libera ou não
```

Pontos que mudam o desenho:

- **`access` é a decisão.** É aqui que o nosso motor local entra — e continua valendo que
  a nuvem não participa ([ADR-0002](ADR/ADR-0002-local-first.md)).
- **`message` é texto livre exibido no leitor.** Permite a mensagem acionável em
  português direto no equipamento, em vez de "acesso negado" genérico.
- **`mode`**: 2 = senha · 3 = cartão · 8 = facial.
- **Desconhecido chega com `enrollid: 99999999`, `event: 2` e uma `image` em Base64.**
  Isso é **foto de pessoa não cadastrada** — dado pessoal sensível. Não pode ir para log
  ([ADR-0014](ADR/ADR-0014-criptografia-e-biometria.md)), e a retenção precisa de base
  legal declarada.
- **O usuário precisa estar cadastrado no leitor mesmo no modo on-line** — o `enrollid`
  do `sendlog` tem que corresponder a um cadastro. Pré-sincronização é obrigatória.
- Envio e recebimento são **independentes**: um `sendlog` pode chegar no meio da resposta
  de outro comando. Exige correlação por `ret`/`cmd`, não por ordem de chegada.

## 6. Confirmação de giro: não existe

Confirmado pela própria Topdata: as catracas com leitor facial **não enviam evento de
confirmação de giro físico via WebSocket**. O `sendlog` é o único retorno, e confirma
autorização e comando elétrico — não passagem.

**Consequência direta:** numa Catraca Easy, o produto **nunca** pode registrar
`PhysicalPassage`. Tudo é `AccessAuthorized`, e os relatórios precisam dizer isso
([ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md)). Se a contagem de público
precisar ser confiável, a linha Easy é a escolha errada de hardware — e isso é uma
conversa a ter **antes** de comprar.

## 7. Comandos

Inventário completo em
[`compatibility-matrix/comandos-facial.csv`](compatibility-matrix/comandos-facial.csv).

**WebSocket (16):** `reg`, `sendlog`, `senduser` (recebidos) · `enabledevice`,
`disabledevice`, `getuserlist`, `getuserinfo`, `setuserinfo`, `setuserlock`, `deleteuser`,
`setdevinfo`, `setdevlock`, `cleanuser`, `getalllog`, `cleanlog` (enviados).

**Web API HTTP (34):** inclui os acima mais `getdevcap`, `getrtlog`, `gettime`, `settime`,
`adduser`, `deleteusers`, `enableuser`, `checkuserid`, `checkregstatus`,
`deleteuserface`, `getuserlock`, `deleteuserlock`, `cleanuserlock`, `cleanadmin`,
`cleandatebase`, `getlog`, `setlogs`, `reboot`, `initsys`, `initmenu`, `getlang`,
**`opendoor`**.

Detalhes que importam:

- **Paginação** (`getuserlist`, `getalllog`): `stn: true` começa do início, `stn: false`
  pede a próxima página; repetir até `to == count`.
- **`setuserinfo`** com `backupnum: 0` envia usuário sem foto; `backupnum: 50` envia com
  foto em Base64. **Foto recomendada até 150 KB, tamanho ideal 480×640.**
- **`setuserlock`** define janelas por usuário (`weekzone`, `starttime`, `endtime`) e
  `verifymode`: 0 face/cartão/senha · 2 só senha · 3 só cartão · 8 só face · 9 face+senha ·
  10 cartão+face · 11 cartão+senha · 14 face e (cartão ou senha).
- **`setdevlock`** define até **8 `dayzone`** (5 faixas cada) e **8 `weekzone`**. É o teto
  de granularidade das regras locais — bem menor que as 100 tabelas do Inner.
- **`cleanuser`, `cleanlog`, `cleandatebase`, `initsys`** são destrutivos → entram na
  aprovação em duas pessoas ([`05`](05-modelo-de-dados.md), `approval_request`).
- **`opendoor`** (só Web API) abre a porta direto. Precisa de RBAC forte e auditoria.

## 8. Códigos de erro do cadastro facial (`reason`)

| `reason` | Significado | Mensagem ao operador |
|---|---|---|
| 1 | Usuário não encontrado / erro de formato | "Cadastro não encontrado" |
| 2 | Senha incorreta ou vazia | "Senha do equipamento incorreta" |
| 3 | Dispositivo ocupado | "Equipamento ocupado, tente em instantes" |
| 5 | Nenhum rosto detectado | "Não foi possível encontrar um rosto na foto" |
| 6 | Múltiplos rostos | "A foto tem mais de uma pessoa" |
| 7 | Resolução muito grande | "Foto muito grande — use 480×640" |
| 8 / 9 | Rosto muito grande / muito pequeno | "Ajuste o enquadramento do rosto" |
| 10 | Qualidade baixa | "Foto sem nitidez suficiente" |
| 11 | Rosto fora do centro | "Centralize o rosto na foto" |
| 12 | Rosto duplicado | "Este rosto já está cadastrado para outra pessoa" |

Esses códigos são o que transforma "falha no cadastro" em instrução útil na tela de
credenciamento — exatamente o que o [`GLOSSARIO`](GLOSSARIO.md) exige.

## 9. Impacto na arquitetura

1. `Topdata.Facial.Adapter` é **x64, multiplataforma**, sem DLL. Hospeda um **servidor
   WebSocket** (leitores conectam nele) e um **cliente HTTP** para a Web API.
2. Não há limite de 30 equipamentos nem thread única. Concorrência real.
3. Precisa de: resposta obrigatória ao `reg`, correlação de mensagens assíncronas,
   reconexão e fila de eventos acumulados.
4. **Fotos de desconhecidos em Base64 exigem tratamento de dado sensível desde o dia 1.**
5. Em Catraca Easy, `PhysicalPassage` não existe — o domínio precisa modelar isso como
   capacidade ausente, não como falha.

## Fontes

- [Manual WebSocket Facial](https://integrador.topdata.com.br/suporte/manual-sdk-facial/)
- [Comandos do Leitor Facial](https://integrador.topdata.com.br/suporte/comandos-leitor-facial/)
- [Protocolo Facial API WebService](https://integrador.topdata.com.br/suporte/protocolo-facial-web-api/)
- [Download do SDK Leitor Facial](https://integrador.topdata.com.br/suporte/download-sdk-leitor-facial/)
- [Catraca Easy — existe confirmação de giro físico via WebSocket?](https://integrador.topdata.com.br/suporte/catraca-easy-existe-confirmacao-de-giro-fisico-via-websocket/)
