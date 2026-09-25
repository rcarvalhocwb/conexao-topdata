# 01 — Perguntas críticas

> **Atualização de 24/09/2026.** O manual oficial e as FAQs da Topdata foram obtidos e
> lidos. **B1, B3, B5 e B6 estão respondidos**, e 7 dos 12 itens da pauta com a Topdata
> caíram. O que sobrou está marcado abaixo. Detalhamento em
> [`11-capacidades-do-sdk.md`](11-capacidades-do-sdk.md).
>
> | Pergunta | Situação |
> |---|---|
> | B1 — SDKs disponíveis? | ✅ **Manual obtido.** Falta só o pacote de *exemplos* (assinaturas de 6 funções + valores de enum de retorno) |
> | B2 — Inventário do parque | ❌ **Aberta** — depende do cliente |
> | B3 — COM ou P/Invoke? | ✅ **P/Invoke.** Assinaturas documentadas com `ref byte` e `StringBuilder`; exemplos oficiais em C# |
> | B4 — Fail-safe × fail-secure | ❌ **Aberta** — decisão de quem responde pela segurança do evento |
> | B5 — Existe confirmação de giro? | ✅ **Sim na linha Inner** (origem 6, sensor óptico). **Não na linha Easy/facial** — a Topdata afirma textualmente que o `sendlog` não confirma giro |
> | B6 — Dá para recolher cartão e liberar entrada? | ✅ **Viável.** Existem `LiberarCatracaEntrada` e `LiberarCatracaEntradaInvertida`; o fluxo da urna é `AcionarRele2` → origem 7 → liberar → origem 6. Resta o comissionamento decidir qual variante |
> | B7 — Simultaneidade do evento | ❌ **Aberta** — depende do cliente |
> | B8 — Padrão de cartão e zeros à esquerda | ⚠️ **Parcial** — os 8 tipos de leitor e o mecanismo de dígitos variáveis estão documentados; falta saber o do parque real |
> | B9 — Biometria/facial no escopo | ❌ **Aberta** — decisão de negócio e de base legal |

Duas classes. **B = bloqueante**: sem resposta, a Fase 1 começa sobre suposição e pode ser
jogada fora. **N = não bloqueante**: a Fase 1 avança com um default documentado, e a
resposta ajusta o rumo sem retrabalho estrutural.

Para cada pergunta: por que ela importa e o que muda conforme a resposta.

---

## B — Bloqueantes

### B1. Os SDKs serão disponibilizados neste repositório (ou em local acessível)?

Hoje eles **não estão presentes**, e a rede deste ambiente **bloqueia** os domínios da
Topdata. Sem o `EasyInner.cs`, os manuais e os exemplos oficiais, a matriz de
compatibilidade não sai do estado de lacuna e o adapter real não pode ser escrito.

**O que muda:** sem isso, a Fase 1 entrega apenas o adapter *mock* + simulador (o que já é
útil e testável), mas a Fase 2 fica travada.

**Necessário:** `EasyInner.dll` e auxiliares, `EasyInner.cs`, enumerações, Manual de
Integração SDK Inner Acesso, exemplos on-line e completo, SDK do Leitor Facial (se facial
estiver no escopo), manuais dos modelos/firmwares do parque.

---

### B2. Qual é o parque real — modelo, quantidade, firmware, placa e leitor de cada equipamento?

> **Parcialmente respondida em 25/09:** cinco catracas **TopFit 4**, com cartão padrão para
> **urna coletora**. Segundo a Topdata, a linha 4 aceita leitor de QR e de proximidade (ou
> smart) juntos, com urna — o que muda a divisão das catracas
> ([`19`](19-bilheteria-local-e-divisao-das-catracas.md)). **Falta:** se as cinco têm urna
> e leitor de QR, se o cartão é de proximidade ou Mifare, e o firmware.

Sem inventário, "compatível" é chute.

**O que muda:** define quantos workers, quantos hosts, quais módulos entram, e quais
ensaios de bancada precisam ser feitos. Também define se a linha facial entra na Fase 5.

**Formato pedido:** uma linha por equipamento — modelo, nº de série, versão de firmware,
placa, leitor 1, leitor 2, opcionais (urna, biometria, facial, QR), IP, VLAN.

---

### B3. A `EasyInner.dll` é COM registrada ou DLL Win32 com exports `stdcall`?

> **RESPONDIDA em 24/09/2026, pelo SDK 6.0.2.0.** É **DLL Win32 com exports por nome**,
> não COM. A tabela de exportação traz **774 funções**, o binário é **I386 (32 bits)**, e o
> exemplo oficial em C# as declara com `DllImport` e
> `CallingConvention = CallingConvention.Winapi` — que em Windows é `StdCall`. O retorno é
> `byte` na quase totalidade, e não `int`.
>
> A pasta do exemplo se chama `COM`, o que confunde: o conteúdo é P/Invoke puro.


O briefing diz "nativa/COM". São caminhos de interoperabilidade diferentes:
`DllImport`/P-Invoke × interop COM com apartment STA e bombeamento de mensagens.

**O que muda:** o `Topdata.EasyInner.Interop` nasce com um único seam
(`IEasyInnerNative`) e duas implementações candidatas; a resposta elimina uma delas e
define o modelo de threading do worker. Se for COM STA, o worker precisa de thread STA
dedicada e as regras de reentrância mudam.

*(A existência de um wrapper `EasyInner.cs` sugere P/Invoke, mas isso é indício, não
confirmação — `A_VERIFICAR_NO_WRAPPER`.)*

---

### B4. Qual é a política de segurança física do evento: fail-safe ou fail-secure?

Quando o Edge cai, ou o equipamento perde comunicação, ou falta energia no meio de um
giro — o portão **abre** (fail-safe, prioriza evacuação) ou **fecha** (fail-secure,
prioriza controle)?

**O que muda:** é configurável por gate no produto, mas o **default de fábrica** e a
política de cada tipo de gate precisam ser decididos por quem responde pela segurança do
evento, não pelo software. Há implicação legal e de corpo de bombeiros.

**Relacionado:** existe requisito de liberação geral por alarme de incêndio/evacuação
integrado? Por contato seco, por comando de software, ou ambos?

---

### B5. Existe confirmação de giro físico (origem 6) nos modelos do parque?

O briefing diz "em modelos compatíveis". Se o modelo do coletor/urna **não** emitir
confirmação de giro, o passo 9 do `CollectCardThenEnter` não pode ser cumprido.

**O que muda:** sem origem 6, o fluxo termina em "autorizado e recolhido, sem confirmação
de passagem" e o consumo do ingresso passa a ocorrer na confirmação de **recolhimento**
(origem 7) — uma decisão de negócio com impacto em fraude e em reconciliação, que precisa
ser aprovada explicitamente pelo cliente, não assumida pelo software.

---

### B6. Recolher o cartão para liberar a ENTRADA é fisicamente suportado na instalação?

O uso típico da urna é saída. Inverter exige que o mecanismo de recolhimento e o sentido
liberado sejam compatíveis, e isso **é característica da instalação física**, não de
software.

**O que muda:** se não for suportado no modelo/instalação, o workflow precisa ser
redesenhado (ex.: coletor separado da catraca, com liberação por gate distinto). O
assistente de comissionamento existe exatamente para não deixar essa descoberta para o
dia do evento.

**Também:** qual a capacidade física real da urna instalada e qual a rotina de esvaziamento
durante o evento (quem, com que frequência, com qual registro de custódia)?

---

### B7. Quantas pessoas, em quanto tempo, por quantos gates?

50.000 pessoas é o público total. O que dimensiona o sistema é a **simultaneidade**.

**O que precisa ser respondido:** público esperado; janela de entrada em minutos;
percentual chegando no pico e em quantos minutos; tempo médio e p95 por passagem
observado em eventos anteriores; quantidade de gates ativos; fator de indisponibilidade;
percentual sujeito a inspeção manual.

**O que muda:** a calculadora de capacidade dirá se a quantidade física de catracas é
suficiente. Se não for, é melhor descobrir agora — nenhum software resolve déficit de
catraca.

---

### B8. Credencial: qual é exatamente o identificador, e ele tem zeros à esquerda?

Tipo de cartão (RFID/Mifare/proximidade/código de barras/QR/PIN), padrão de leitura,
quantidade de dígitos (fixa ou variável), presença de Facility Code, e se o número impresso
no cartão é o mesmo lido pelo leitor.

**O que muda:** o produto trata credencial **sempre como string**
([ADR-0008](ADR/ADR-0008-credencial-como-string.md)), mas a normalização (padding,
truncamento, extração de FC) é específica do padrão e precisa ser modelada e testada com
cartões reais. Erro aqui vira "cartão válido recusado" no dia do evento.

---

### B9. Biometria e facial estão no escopo? Qual a base legal e a política de retenção?

Dado biométrico é dado pessoal sensível (LGPD, art. 5º, II). Coleta exige base legal
específica, finalidade declarada, prazo de retenção e procedimento de eliminação.

**O que muda:** se estiverem no escopo, entram a segregação criptográfica do armazenamento,
o fluxo de consentimento, o relatório de acesso ao titular e a rotina de expurgo — e o
prazo da Fase 5 cresce. Se não estiverem, o produto nasce sem superfície biométrica, o que
reduz materialmente a exposição.

**Quem é o controlador dos dados — o cliente final, o organizador do evento, ou a casa de
espetáculo?** A resposta define contratos e responsabilidades.

### B10. Como os ingressos de cada provedor chegam à base local, e com qual atraso?

Três sites vendendo para o mesmo evento significam três integrações de entrada. Precisa-se
saber, de cada um: **que API existe** (consulta por cursor, exportação em arquivo, webhook),
**qual o atraso real** entre a venda e o ingresso ficar disponível, e **se o cancelamento
também é publicado** — ou se estorno só aparece em conciliação posterior.

**O que muda:** a máquina local fica na mesma rede das catracas e **não pode ter porta
aberta para a internet**. Provedor que só oferece webhook exige um relé na nuvem, do qual a
borda puxa. O atraso publicado é o que determina se o ingresso comprado na fila chega a
tempo — ver B12. Ver [`16`](16-multiplos-provedores-de-ingresso.md), seção 2.

**Primeiro provedor identificado: Zet (`comprenozet.com.br`), do Clube Gazeta do Povo.** O
que ele publica é um **aplicativo de validação** ("Zet Valida"), não uma API — nenhuma
documentação de integração apareceu em busca pública. Se não houver API para parceiro, a
forma de trabalho muda de "a catraca valida o ingresso" para "o ingresso é trocado por uma
credencial nossa no credenciamento". O questionário para fechar isto, com os três
provedores, está em [`17`](17-questionario-de-integracao-bilheteria.md).

**Pergunta que decide sozinha o desenho:** o QR do provedor é **estável** ou
**dinâmico/rotativo**? Código que muda a cada minuto não pode ser validado offline por
terceiro, e nesse caso não existe escolha.

### B11. Para cada provedor, "utilizado" é a autorização ou o giro físico?

Autorizar não é passar. Entre o comando de liberação e o giro confirmado (origem 6) cabem
a desistência, a catraca travada e a liberação que ninguém aproveitou.

**O que muda:** avisar na autorização pode queimar um ingresso pago sem que ninguém tenha
entrado; avisar só no giro faz um sensor defeituoso apagar entradas reais do sistema do
provedor. O produto sustenta os dois e informa a diferença no relatório, mas a escolha é
contratual e precisa ser feita **antes** do evento, provedor a provedor.

### B12. O que fazer com o ingresso comprado durante o evento que ainda não chegou?

Alguém compra pelo celular a três metros da catraca.

**O que muda:** negar é simples e gera reclamação na porta; consultar o provedor na hora
põe uma dependência de rede no caminho da porta, onde ela não deveria estar; liberar com
conferência transfere o risco para a contabilidade. **Sem resposta, o comportamento é
negar** — é o único que não inventa risco no lugar do cliente. Ver
[`16`](16-multiplos-provedores-de-ingresso.md), seção 6.

---

## N — Não bloqueantes (com default assumido)

| # | Pergunta | Default assumido na Fase 1 |
|---|---|---|
| N1 | Versão do .NET: 8 (pedido) ou 10 LTS? | **.NET 10 LTS** — o suporte do .NET 8 termina em nov/2026. Ver [ADR-0016](ADR/ADR-0016-runtime-dotnet.md); trocar para 8 é uma linha de `TargetFramework` |
| N2 | WPF ou WinUI 3? | **WPF** — acessibilidade (UIA) mais madura e menos dependência de runtime externo. Camada de View fina para permitir troca. [ADR-0012](ADR/ADR-0012-wpf-versus-winui.md) |
| N3 | Versão do Windows nos hosts de Edge | Windows 10 21H2+ / Windows Server 2019+, x64, com subsistema x86 |
| N4 | O host do Edge é dedicado ou compartilhado? | Dedicado. Compartilhado exige análise de antivírus e concorrência de porta |
| N5 | Topologia de rede: catracas em VLAN isolada? | Sim, VLAN isolada com ACL para o Edge. Se não, vira item de risco alto |
| N6 | IP fixo ou DHCP com reserva nas catracas? | IP fixo. DHCP sem reserva vira alerta de drift |
| N7 | Quais conectores de nuvem na Fase 4, e em que ordem? | REST/HTTPS primeiro; demais por prioridade do cliente |
| N8 | Sistema externo de ingressos: qual API, qual contrato? | Importação CSV/JSON de contingência garantida; API específica sob contrato |
| N9 | Idioma adicional além de pt-BR? | Somente pt-BR; textos externalizados desde a Fase 1 |
| N10 | Quantos operadores simultâneos no desktop? | Até 5 estações por Edge |
| N11 | Anti-passback: global no evento ou por zona/setor? | Por zona, com janela configurável e política declarada para partição de rede |
| N12 | Retenção de eventos no banco local | 90 dias quentes + arquivamento; ajustável |
| N13 | Modelo de licenciamento/telemetria comercial | Sem telemetria externa por padrão (local-first também em privacidade) |
| N14 | Há necessidade de alta disponibilidade do Edge (par redundante)? | Não na Fase 1–3; arquitetura não impede, mas HA real exige ADR próprio |
| N15 | Sirene/horários de sirene são usados na instalação? | Não habilitado por padrão |

---

## Itens que só a Topdata pode responder

> **6 dos 10 itens abaixo foram respondidos ou parcialmente respondidos pelo manual oficial.**
> Os que permanecem são os que a documentação pública realmente não cobre.

Consolidados em [`08-riscos-e-validacoes-topdata.md`](08-riscos-e-validacoes-topdata.md),
seção "Pauta para a Topdata". Resumo do que é genuinamente ambíguo e não se resolve lendo
o manual:

1. ~~O limite de ~30 equipamentos é por instância, thread ou processo?~~ ✅ **Respondido:** por **instância da DLL**, isto é, por thread de comunicação — e a Topdata recomenda múltiplos processos, **cada um em sua porta TCP**.
2. ~~Os buffers são globais ao módulo ou por handle?~~ ✅ **Respondido:** a DLL inteira é **não thread-safe** e `EnviarConfiguracoes` limpa o buffer após enviar. Serialização por worker é obrigatória; a hipótese de relaxar está descartada.
3. ~~A DLL é reentrante entre threads?~~ ✅ **Respondido: não.** *"Múltiplas threads não devem chamar funções da EasyInner.dll simultaneamente."*
4. Qual o comportamento documentado de `ReceberDadosOnLine` em timeout e em queda de
   socket — retorna código, bloqueia indefinidamente, ou lança?
5. ⚠️ **Parcialmente respondido.** A tabela oficial traz 17 origens com nomes de enum. **11, 14–17 e 19 continuam ausentes** — seguem como desconhecidas ([ADR-0018](ADR/ADR-0018-eventos-desconhecidos.md)).
6. ⚠️ **Parcialmente respondido.** A origem 6 é gerada pelo **sensor óptico** após liberação, na linha Inner; a linha Easy não confirma giro. Falta a lista modelo a modelo — fecha na bancada.
7. ✅ **Respondido:** existem `LiberarCatracaEntrada`, `LiberarCatracaEntradaInvertida`, `LiberarCatracaSaida`, `LiberarCatracaSaidaInvertida` e `LiberarCatracaDoisSentidos`. Qual usar depende da orientação física — decidido no comissionamento.
8. Comportamento na urna cheia: o equipamento recusa a leitura sozinho ou depende do
   software bloquear o fluxo?
9. ✅ **Respondido:** o SDK **sempre** vence, e `EnviarConfiguracoes` envia até os **defaults da DLL** para o que não foi setado. Ver [ADR-0020](ADR/ADR-0020-configuracao-sempre-completa.md).
10. Matriz oficial de **firmware mínimo** por função do SDK.
