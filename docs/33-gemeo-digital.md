# 33 — Gêmeo digital da TopFit 4

> A catraca em 3D dentro do painel: o operador gira, aproxima, clica nas peças, vê o que
> cada uma faz **neste sistema** e assiste aos cenários da operação. No modo ao vivo o
> desenho acompanha a catraca real. **É também a porta principal da configuração da
> catraca** (seção 9): clicar numa peça abre os parâmetros dela; salvar e aplicar mandam a
> configuração para a catraca real. **Nada disto foi visto rodando numa tela Windows ainda:**
> a CI compila e testa a lógica; o desenho só aparece no Windows com tela (ver seção 8).

![Prévia da geometria: três quartos, painel e peças separadas](gemeo-digital/previa-da-geometria.png)

*Prévia renderizada fora do WPF, só com a geometria (sem o brilho e as luzes da tela).*

## 1. De onde veio o pedido, e o que mudou

O pedido original (prompt gerado no ChatGPT) supunha **Java, JavaFX e jMonkeyEngine**. O
XAcess é **C# / .NET 10 com WPF** (ADR-0012, ADR-0019). Então:

| Pedido | O que foi feito | Por quê |
|---|---|---|
| jMonkeyEngine / PBR | **Viewport3D do WPF**, materiais difuso + especular + emissivo | Nenhum pacote novo (NuGetAudit quebra o build por dependência), roda na máquina modesta do evento |
| Nova máquina de estados `OFFLINE…URN_FULL` | **Cena visual** (`CenaDaCatraca`) que resume a `DeviceStateMachine` real | A máquina real já existe no worker; duplicá-la criaria duas verdades |
| `SimulationTurnstileAdapter` / `TopdataTurnstileAdapter` | Já existem: `src/Simulator` e `Topdata.EasyInner.Adapter` | O gêmeo não fala com catraca nenhuma: fala com o serviço, pelo IPC, como toda tela |
| Event bus novo | O fluxo `AcompanharEventos` que o Painel ao vivo já usa | Uma fonte só de eventos |
| Botões "Recolher cartão", "Urna cheia", "Dois sentidos" (evacuação, D5), facial | Aparecem, com o selo **Aguardando confirmação** ou **Fora do escopo** | Mesma regra da tela Gerenciar catraca (docs/32, seção 5): nada de fingir |

## 2. O que a tela faz

- **Desenho 3D:** arrastar gira, roda aproxima, clique escolhe a peça, duplo clique mira nela.
  Setas e +/− no teclado fazem o mesmo; a lista de peças à direita escolhe sem mouse.
- **Vistas prontas:** inicial, frente, painel, braços, trás, cima. A câmera vai suave, sem
  quique (aproximação exponencial).
- **Peças separadas:** tampa sobe, braços saem, o depósito da urna aparece.
- **Painel da peça** (seção 9): o que faz, cada função com o selo *Disponível / Aguardando
  confirmação / Não usada aqui / Fora do escopo* e, logo abaixo, os parâmetros daquela peça
  nesta catraca — ou "nada a configurar nesta peça".
- **Cenários (demonstração):** QR válido, código desconhecido, liberada sem giro, cartão na
  urna, cartão da bilheteria na frente, liberação manual, queda de comunicação, urna cheia.
  Cada passo tem uma frase e acende a peça de que fala.
- **Na simulada:** com o serviço em modo simulação, o botão passa o código de teste do cenário
  na catraca simulada (o mesmo `SimularLeitura` da tela Simulador) e o gêmeo muda para ao
  vivo, mostrando o que o sistema decidiu de verdade. Fora da simulação o serviço recusa.
- **Display:** prévia do texto em 2 × 16, com aviso de palavra cortada entre as linhas (e
  quantos espaços pôr), de texto acima de 32 caracteres e de acento. "Ver no display 3D" só
  muda o desenho.
- **Ao vivo:** escolhe a catraca; cada acesso gravado vira leitura → decisão → giro no
  desenho. Sem notícia da catraca, o display apaga. O desenho e o quadro "Na catraca agora"
  mostram a configuração **que a catraca confirmou** (seção 9.5). O desenho não comanda: quem
  manda algo para a catraca é o painel de configuração, com nome e confirmação.
- **Giro desta catraca (mapa de giro, D9 do docs/34 §9):** clicar nos braços (ou escolhê-los na
  lista) abre, no painel da peça, o mapa inteiro da catraca escolhida; clicar na urna mostra só
  a linha do **leitor 2 (urna)**. Para cada origem: padrão, ou a função que libera o braço,
  como o giro conta (entrada/saída) e o texto curto. Uma **seta** em volta do eixo dos braços
  (`GeometriaFit4.SetaDoGiro`) mostra o lado da regra em destaque — prevista pelo nome da função
  (entrada e saída invertida para um lado; saída e entrada invertida para o outro); qual serve
  a cada instalação é a conferência. "Ver no gêmeo" anima o sentido **só no desenho** (cenário
  "Pré-visualização do giro", narração "nada foi enviado"; recusado ao vivo). No gêmeo, salvar e
  aplicar o giro são os da catraca inteira (seção 9); a conferência "Girou para o lado da seta /
  ao contrário" continua no painel, com o selo "Sentido ainda não conferido nesta instalação"
  até lá. A mesma ViewModel (`MapaDeGiroViewModel`) e o mesmo controle (`Telas/MapaDeGiro.xaml`)
  estão na aba Giro da Parametrização, onde o giro tem salvar e aplicar próprios
  (`ControlesProprios`). Captura `NN-gemeo-digital-giro` (urna clicada, seta e
  pré-visualização), feita depois das três de sempre, sem renumerar.

O selo no alto do desenho diz sempre de onde vem o que se vê: *DEMONSTRAÇÃO · cenários só no
desenho* ou *AO VIVO · CATRACA NN · o desenho segue a catraca*. Ele fala do desenho e dos
cenários. Ao lado dele, a pílula **Configuração:** diz a situação da configuração desta
catraca, que é real nos dois modos.

## 3. Arquitetura

```
Telas/Gemeo.xaml(.cs)           desenho, câmera, mouse — nenhuma regra
        │  Quadro() a cada frame; PropertyChanged para vista e foco
        ▼
GemeoDigitalViewModel           modos, cenários, prévia, fluxo ao vivo (sem WPF, testável em Linux)
        │
        ├── CentralDaCatracaViewModel   a configuração da catraca inteira (seção 9), sem regra nova:
        │       ├── ParametrizacaoViewModel   campos, versões, salvar, aplicar (Etapa A.6)
        │       │       └── MapaDeGiroViewModel   o giro (D9), sem salvar/aplicar próprios aqui
        │       ├── GerenciarCatracaViewModel  pedidos imediatos (mensagem, relógio, reconexão)
        │       └── ConfiguracaoPorPeca        peça → campos, linha do giro e pedidos
        ├── CenaDaCatraca       máquina de estados visual, pura: o instante vem de fora
        ├── Roteiros            os cenários, passo a passo, com a narração
        ├── TraducaoAoVivo      EventoDeAcesso → acontecimentos da cena
        ├── CatalogoDaFit4      fichas das peças e a situação real de cada função
        ├── Display2x16         como 32 caracteres viram duas linhas de 16
        └── GeometriaFit4       a catraca em malhas neutras (Malha), a partir de fit4.json
                │
                └── EspecificacaoDaFit4 ← GemeoDigital/fit4.json (embutido)
```

Regras que os testes seguram:
- **Nunca dois giros ao mesmo tempo.** Giro que chega durante outro entra na fila.
- **Decisão espera a leitura ser mostrada**: o operador vê leu → decidiu → liberou, na ordem.
- **Liberado sem giro não conta**, igual à prestação de contas (ADR-0007).
- **Queda no meio do giro** termina o giro onde estava; não existe meio giro desenhado.
- **Leitor desconhecido não é inventado:** sem a origem, a leitura aparece sem celular nem cartão.

## 4. O modelo 3D

Desenhado por código (`GeometriaFit4`): caixas, cilindros, esferas e um prisma para a cabeça
com a frente inclinada. Eixos: X da coluna para os braços, Y para cima, Z para a frente;
milímetros.

O mecanismo é um **tripé de verdade**: o eixo desce inclinado (45°) e os três braços saem num
cone em volta dele, de modo que um fica sempre na horizontal fechando a passagem. Um terço de
volta leva cada braço ao lugar do próximo — há teste para isso, e para o sentido da entrada
(o braço de cima vai para trás, como quem empurra vindo da frente).

**Para trocar por um modelo do Blender** (`fit4.glb`): os objetos precisam ter os nomes que
`GeometriaFit4` usa (`Base`, `Coluna`, `Tampa`, `Flange`, `Cubo`, `Braco01..03`,
`DisplayMoldura`, `DisplayTela`, `TecladoBase`, `Teclas`, `QrMoldura`, `QrJanela`,
`ProxPlaca`, `ProxSimbolo`, `UrnaMoldura`, `UrnaFenda`, `UrnaDeposito`, `LiberadoFundo`,
`LiberadoSeta`, `BloqueadoFundo`, `BloqueadoXis`, `FacialCorpo`, `FacialTela`), com o pivô
do rotor no centro do cubo. A tela só lê `ModeloDaFit4`; um leitor de glTF que produza o
mesmo `ModeloDaFit4` troca o desenho sem mexer em ViewModel, cena ou testes. O WPF não lê
glTF sozinho: isso exige um pacote novo, e fica para quando houver o arquivo.

## 5. Medidas

Em `src/Desktop.ViewModels/GemeoDigital/fit4.json`. **A confirmar:** vêm de material comercial
da Topdata (variante Facial: 300 × 1342 × 250 mm sem os braços), não de fonte primária, e as
medidas internas (altura do eixo, comprimento do braço, cabeça) são estimadas pelas fotos. A
tela diz isso embaixo do desenho enquanto `fonteDasMedidas` começar com `A_CONFIRMAR`.

**Na bancada:** medir altura total, altura do eixo, comprimento e diâmetro do braço, e a
inclinação do eixo; corrigir o JSON e trocar `fonteDasMedidas`.

As fotos de referência usadas para a disposição das peças (perfil da catraca e o painel com
facial, display, teclado, QR e proximidade) são material comercial da Topdata e **não estão no
repositório**, que é público.

**Cores.** As do objeto físico (pintura, inox, teclas, luzes da cena, display) também estão no
`fit4.json`, em `aparencia`: a catraca é a mesma em qualquer tema, e a regra do projeto é
não ter cor solta em tela. O que depende do tema — o realce da peça, o verde e o vermelho
acesos, a urna cheia — vem das chaves Rayzer (`Rayzer.Brand.Cyan`, `Rayzer.Access.Granted`,
`Rayzer.Access.Denied`, `Rayzer.Warning`).

## 6. Acessibilidade

- Tudo o que o mouse faz no desenho tem caminho pelo teclado (lista de peças, botões de vista,
  setas e +/− com o desenho focado).
- O estado da catraca desenhada está também em texto (`RayzerStatus`, com leitor de tela
  avisando a mudança), e a narração do cenário é uma lista, não só animação.
- Cor nunca sozinha: verde e vermelho do desenho têm seta e X; o estado tem texto.

## 7. Limitações conhecidas

- **Ao vivo, o giro chega junto com a decisão.** O fluxo manda cada tentativa quando ela é
  gravada; se o giro ainda não aconteceu, o desenho mostra "liberada" e volta a travar depois
  da espera pelo giro, sem mostrar o giro que veio depois. Resolveria: o serviço difundir
  também a confirmação da passagem (um campo a mais em `EventoDeAcesso`, ou um segundo evento
  com o mesmo `evento_id`).
- **A origem ao vivo só existe para tentativas novas.** Desde a Etapa 0.3 do docs/35, a
  origem bruta de cada leitura é gravada com a tentativa (`ticket_use_attempt.reader_origin`,
  migração 010) e o serviço preenche `origem_bruta`, `origem_conhecida` e
  `origem_desconhecida` (QR 21, frente 2, urna 3). Tentativas gravadas antes da migração não
  têm origem e continuam aparecendo sem o objeto (exceto "use a fenda da urna", deduzido do
  motivo).
- **Urna cheia ao vivo** só aparece quando o serviço mandar a origem 20.
- **Sinais liberado/bloqueado** estão "Aguardando confirmação": as luzes comandadas pelo
  sistema só existem na Linha 3 (docs/02, docs/11); na TopFit 4 o aviso à pessoa é o display.
  O desenho acende verde/vermelho só para marcar o momento, e a ficha diz isso
  (`A_CONFIRMAR_COM_TOPDATA`; docs/34 §7.1, P16).
- **Liberação manual e mensagem temporária** (na Gerenciar catraca ou no painel do display)
  não passam pelo fluxo de acessos: o desenho ao vivo não as mostra.
- **Configuração no gêmeo:** as limitações da central estão na seção 9.7.
- **Transparência** (a pessoa genérica) no WPF depende da ordem de desenho; vista de alguns
  ângulos ela pode cobrir a catraca de um jeito estranho. É só um objeto de cena.

## 8. Como conferir no Windows

1. Instale ou rode o painel em modo simulação (docs/23).
2. Abra **Gêmeo digital** no menu.
3. Rode cada cenário; em "QR válido", confira: celular chega, verde acende no desenho, braço gira um
   terço para trás, display volta à mensagem padrão.
4. Marque **Ao vivo**, e na tela **Simulador** passe `1000000001` na catraca 1: o desenho
   precisa ler, liberar e girar.
5. Arraste, role, clique no display, no leitor de QR e nos braços; teste as vistas e as peças
   separadas; troque o tema claro/escuro.
6. Configuração (seção 9.3): mude a mensagem no display e desligue a urna; confira a bola nas
   duas peças, as duas linhas em "o que muda", Salvar, "Aplicar nesta catraca…" e a pílula
   chegando a "Aplicada" (simulação: a catraca simulada confirma a versão). Confira em
   1366 × 768 que o painel da peça aparece ao lado do desenho.
7. Deixe o gêmeo aberto 10 minutos e confira no Gerenciador de Tarefas que a memória não
   cresce.

O autoteste do painel (`--autoteste`) já passa por esta tela nos dois temas.

## 9. Central de configuração da catraca

### 9.1 O pedido

O dono do produto (2026-10-02): *"Quando eu mudo as funções do gêmeo, a catraca real muda
também? Essa é a intenção: mudar no gêmeo, aplicar a alteração e ela envia para a catraca real.
O gêmeo seja o lugar de configuração da catraca inteira."*

Antes, não: o gêmeo só observava (o mapa de giro era a exceção). Agora o gêmeo é a **porta
principal** da configuração de cada catraca. Nada de regra nova: os campos, a validação, a
origem (padrão do sistema / vem do evento / definido nesta catraca), a situação (enviado,
chave técnica desligada, a confirmar), o salvar com nome, o aplicar em dois passos e o
"Aplicada" só com o pedido concluído e a versão igual são os da Parametrização (Etapa A.6 do
docs/35), pelos mesmos RPCs (`ObterConfiguracaoDaCatraca`, `GravarConfiguracaoDaCatraca`,
`ObterMapaDeGiro`, `GravarMapaDeGiro`, `EnviarComando`). **Proto intocado, nenhuma migração.**
A Parametrização ("Ver em lista") e a Gerenciar catraca continuam existindo; as duas ganharam o
atalho para o gêmeo ("Abrir no gêmeo", "Configurar no gêmeo").

### 9.2 Peça → parâmetros

| Peça (clique) | Parâmetros (configuração: salvar e aplicar) | Pedidos imediatos | Só no modo técnico |
|---|---|---|---|
| **Leitor de QR** e **leitor de cartão da frente** (os dois são o leitor 1) | Tipo de leitor — lista com nomes; selo do 5 × 8 (NOVO-HIL-QR-02) | — | Leitor 1 (operação) |
| **Urna** | Leitor da urna: ligado / desligado (leitor 2); a linha "Leitor 2 (urna)" do mapa de giro | — | — |
| **Braços** | Segundos liberada esperando o giro (relé 1); o mapa de giro inteiro (4 origens) | — | Função de liberação da entrada |
| **Display** | Mensagem padrão (até 32) | Mensagem temporária (texto e segundos) | — |
| **Coluna** (por dentro passa a placa de controle) | Firmware e relógio da catraca (o que o serviço sabe) | Acertar o relógio; refazer a conexão | Wiegand com dois leitores e rearme do leitor (desabilitados: chave técnica desligada, HIL-CARD-05 e INT-SM-032); **relé 2**, sempre desabilitado, selo "Aguardando ensaio NOVO-HIL-REL-04/06" (a catraca recebe o padrão de fábrica, função 0 e tempo 0, igual em todas) |
| Tampa, base, teclado, sinais, leitor facial | A ficha de sempre e **"nada a configurar nesta peça"** | — | — |

Todo campo que a catraca pode sobrepor ao evento tem uma peça, e toda origem do giro também
(teste `Todo_campo_da_catraca_e_toda_origem_do_giro_tem_uma_peca`). No modo guiado não aparece
campo técnico nem termo do SDK; o painel diz só "No modo técnico aparecem mais N ajustes desta
peça". Campo que aguarda confirmação aparece **desabilitado, com o selo e o motivo**.

### 9.3 Passo a passo do operador

1. Abra **Gêmeo digital** no menu (ou "Configurar no gêmeo" na Gerenciar catraca, ou "Abrir no
   gêmeo" na Parametrização). Escolha a **catraca** no alto da coluna da direita.
2. Embaixo do desenho, no cartão **Configuração desta catraca**, escreva **o seu nome** (fica
   registrado). O cartão diz: *"Configuração real desta catraca — vale depois de Aplicar."*
3. **Clique numa peça** do desenho (ou escolha na lista "Peças"). Ao lado do desenho abre o
   painel dela: o que faz e os parâmetros desta catraca. Em cada parâmetro, desmarque "Usar o
   padrão do evento" e escolha o valor.
4. Clique em **outra peça** e mude o que precisar. As alterações se acumulam: o cartão embaixo
   do desenho mostra **um só "o que muda (atual → novo)"** com todas, e a faixa de cima do
   desenho diz "N alterações não salvas". No desenho, **bola** (cor de atenção) = peça com
   alteração não salva; **cubo** (cor de informação) = peça com valor diferente do padrão do
   evento. A legenda e a lista em palavras ficam embaixo do desenho.
5. **Salvar** grava tudo, com o seu nome. A pílula vira "Salva, não aplicada". (**Desfazer**
   volta todas as peças ao que está salvo.)
6. **Aplicar nesta catraca…** pede confirmação ("ela reconecta e fica alguns segundos sem
   atender; se estiver no pico, prefira aplicar depois"). **Aplicar agora** manda o pedido
   "Aplicar configuração" **só desta catraca** — o mesmo da Parametrização. A pílula fica em
   "Aplicando: aguardando a catraca".
7. A pílula só diz **"Aplicada"** quando o pedido terminou (`Concluido`) **e** a versão que a
   catraca aceitou é a do salvo (Etapa A.5). Falhou ou expirou nunca é "Aplicada".
8. Mensagem temporária, acertar o relógio e refazer a conexão (display e coluna) **não são
   configuração**: vão na hora, sem Salvar nem Aplicar, registrados com o seu nome, como na
   Gerenciar catraca.
9. Depois de aplicar uma regra de giro, gire uma vez e registre no painel dos braços ou da
   urna: "Girou para o lado da seta" ou "Girou ao contrário".

### 9.4 Demonstração × configuração

O selo *DEMONSTRAÇÃO · cenários só no desenho, nada vai para a catraca* vale para os
**cenários**. O painel de configuração é real nos dois modos e diz isso no próprio cartão. Os
cenários continuam sem tocar no serviço; "Ver no gêmeo" do giro continua só no desenho.

### 9.5 Ao vivo: o que a catraca está usando

A catraca **não devolve a configuração** (leitura de volta bloqueada, Etapa A.10, T12). Então,
ao vivo, o gêmeo só afirma valores quando a versão que a catraca aceitou é a do salvo e não há
pedido em curso: aí o desenho usa a mensagem padrão e a urna (ligada/desligada) desta catraca,
e o quadro **Na catraca agora** lista os parâmetros enviados e o giro. Fora disso, o quadro diz
por quê ("ainda não confirmou nenhuma", "aplicando", "está com outra versão, não a salva") e o
desenho usa o padrão do evento, dito em texto. Na demonstração, o desenho usa o padrão do evento.

### 9.6 Desenho em 1366 × 768

A coluna da direita (catraca, painel da peça, peças, cenários, prévia do display, "Na catraca
agora") rola sozinha, na altura do desenho, e volta ao topo quando se escolhe uma peça — o
painel aparece ao lado do 3D. O cartão da catraca inteira fica embaixo do desenho, em duas
colunas, e não sai do lugar ao navegar entre peças. A página inteira rola, como antes.
Capturas: `08-gemeo-digital-{tema}-config-peca` (leitor da frente aberto) e
`08-gemeo-digital-{tema}-config-pendente` (display e urna alterados, nada salvo; o rascunho é
desfeito no fim da captura).

### 9.7 Limitações

- **Salvar são duas gravações** (campos e giro têm RPCs próprios). Cada uma é inteira; se a do
  giro for recusada, nada mais é gravado; se a dos campos for recusada depois do giro salvo, a
  tela diz exatamente isso e o resto continua em "o que muda". Um RPC único com transação
  pediria campo novo no proto e uma transação entre os dois repositórios — não feito.
- **Gravar troca a camada inteira** da catraca (como na Parametrização). Se outra tela salvar
  enquanto há rascunho no gêmeo, o gêmeo mantém o rascunho e avisa ("salva em outro lugar");
  quem salvar depois grava por cima. Sem rascunho, o gêmeo recarrega o novo sozinho.
- **Trocar de catraca** joga fora o rascunho não salvo, como na Parametrização.
- **Os nomes das funções do giro** (EI-041 a EI-044) aparecem também no modo guiado: é o
  painel do mapa de giro de antes (D9), que não tem modo guiado.
- **Relé 2** não é um campo da catraca: aparece só no técnico, desabilitado, até o ensaio.
- O desenho ainda não foi visto numa tela Windows (seção 8).

## 10. Testes

- `tests/Unit/GemeoDigital/CenaDaCatracaTests.cs` — a máquina visual: fila de giros, ordem
  leitura → decisão, negação de 3 s, liberação sem giro, queda de comunicação, relógio que volta.
- `tests/Unit/GemeoDigital/GeometriaFit4Tests.cs` — faces para fora, braço horizontal, um
  terço de volta leva cada braço ao próximo, sentido da entrada, texto do display de pé,
  proporções, nomes únicos, variante sem urna.
- `tests/Unit/GemeoDigital/GemeoDigitalTests.cs` — display 2 × 16, especificação, catálogo
  (o que não existe aparece como aguardando), todos os cenários terminam com a catraca livre,
  códigos de teste existem no `simulacao.exemplo.json`, tradução do fluxo ao vivo.
- `tests/Integration/TelasTests.cs` — a ViewModel contra o serviço de verdade; mapa de giro:
  braços abrem o painel, urna leva ao leitor 2, a seta acompanha a função, salvar exige o nome,
  aplicar em dois passos, conferência tira o selo, pré-visualização não grava nem comanda, aba
  Giro da Parametrização.
- `GeometriaFit4Tests.Seta_do_giro_aponta_no_sentido_da_cena` — a seta gira para o mesmo lado
  que o rotor da cena.
- `tests/Integration/TelasTests.cs` — central da configuração (seção 9): cada peça abre o
  painel certo com os campos certos (guiado e técnico); alterações de várias peças num só "o que
  muda"; Salvar exige o nome e grava os dois RPCs; erro numa peça bloqueia o Salvar; Aplicar pede
  confirmação, usa o "Aplicar configuração" desta catraca e só diz "Aplicada" depois de
  `Concluido` com a versão igual; campo a confirmar e relé 2 desabilitados com selo; guiado sem
  termo do SDK; ao vivo mostra o que a catraca confirmou e não presume; salvo em outro lugar
  recarrega (ou avisa, com rascunho); "Abrir no gêmeo" e "Ver em lista".
- `tests/Unit/GemeoDigital/GemeoDigitalTests.cs` — todo campo e toda origem do giro têm peça;
  peça sem parâmetro não tem nada a configurar; marcações perto da peça, lado a lado, bola e cubo.
- `tests/Integration/LigacoesDasTelasTests.cs` — toda ligação do XAML aponta para propriedade
  que existe (inclusive `Telas/CentralDaCatraca.xaml` e `Telas/CampoDaCatraca.xaml`).
