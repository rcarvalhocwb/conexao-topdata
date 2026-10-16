# Manual Operacional das Catracas 4 (Rev. 17): o que ele define para a urna e o giro

Análise do manual enviado (`Manual Catracas 4.pdf`, Rev. 17, de 14/11/2024) e das páginas públicas do
suporte Topdata (categoria "Manuais catraca Fit"). As páginas citadas são as impressas no PDF ("PG").
Onde a fonte é a página pública, a referência é a URL.

Fontes usadas:

- Manual Operacional das Catracas 4, Rev. 17 (PDF enviado).
- Operações nas catracas 4: https://suporte.topdata.com.br/suporte/operacoes-nas-catracas-4/
- Configuração das catracas 4: https://suporte.topdata.com.br/suporte/configuracao-das-catracas/
- Web server nas catracas 4: https://suporte.topdata.com.br/suporte/web-server-nas-catracas-4/
- Apresentação da linha de catracas 4: https://suporte.topdata.com.br/suporte/linha-de-catracas-4/

Não usado nesta análise: os outros manuais da categoria (biometria, QR Code, acesso remoto, base para
eventos). Ficam como próximos itens se o escopo pedir.

## 1. Quem é responsável pela conexão

- A catraca é o cliente TCP. O IP do servidor e a porta ficam configurados **na catraca**, no menu Master,
  em "Rede": IP do Servidor (PG 98), Porta Servidor (PG 101). A porta padrão é 3570, independentemente do
  número da catraca (página "Configuração das catracas 4").
- Campos do menu Rede: IP do Servidor, DHCP, IP do Inner, máscara, gateway, porta do servidor, número do
  Inner (PG 98 a 101).
- Depois das configurações de rede, a catraca "estará pronta para iniciar a comunicação com o software
  para receber as configurações de operação" (página "Operações nas catracas 4").
- O IP do servidor deve ser fixo (PG 98). Com DHCP desabilitado (padrão), a máscara e o gateway são
  informados à mão.
- O manual não descreve nenhum comando do software para gravar IP ou porta na catraca. Isso bate com o
  que o estudo já registrou.

## 2. Os dois relés da placa

A PCI Catraca tem dois acionamentos (PG 170, seção 8.11; tempo padrão na seção 7.15, PG 129):

| Relé | Nome no menu | Função | Padrão de tempo |
|---|---|---|---|
| 1 | Acionamento 1 | Controla o giro: "tem como função única o controle do giro" (página Web server) | 5 s, faixa de 1 a 255 s no Web Server |
| 2 | Acionamento 2 | Usado pela catraca quando ela tem urna. Sem urna, fica livre para sirene ou revista (PG 130 e 170) | 5 s, faixa de 1 a 255 s |

- O **Acionamento 1** é o relé do giro. "O padrão de tempo que a catraca ficar com o giro liberado é de 5
  segundos. Caso o giro não aconteça após o tempo configurado, será necessário efetuar um novo registro"
  (PG 129). Ou seja, é a janela do seu fluxo: sem giro, a catraca cancela sozinha e a pessoa precisa ler o
  cartão de novo.
- Tempo do Acionamento 1: "o tempo em que uma catraca aguarda para completar o giro após a liberação do
  acesso" (página Web server). Esse é o prazo que o operador define.
- O **Acionamento 2** é o relé da urna quando "Possui urna? Sim" (PG 110). Não é um número de função no
  menu: a urna é uma opção do próprio menu.
- Opções de cada acionamento (PG 134): para o Acionamento 1, libera para entrada, para saída, para entrada
  e saída, ou ambos os sentidos com bilhete gerado conforme o giro. Sirene e revista não são usadas no
  Acionamento 1 (PG 134).

## 3. A urna

- Com "Possui urna? Sim", o leitor 2 fica destinado exclusivamente à urna. "O acesso de saída somente será
  liberado se ocorrer o recolhimento do cartão, que é realizado através da configuração do Acionamento 2"
  (página Web server, seção "Possui urna").
- Descrição do manual (PG 29-30): o primeiro leitor libera a entrada com o cartão na mão. Na saída, o cartão
  vai para a fenda do segundo leitor e é guiado para a urna interna. "A saída é liberada para usuário e o
  cartão fica guardado na urna" (PG 29).
- O recolhimento é feito abrindo a tampa com chave no pedestal. O processo passo a passo está em PG 179
  (seção 8.15).
- Off-line, a catraca recolhe todos os cartões do leitor 2 e libera o giro em seguida. On-line, é possível
  distinguir quem terá o cartão recolhido e quem sai com ele (PG 29).
- **Urna cheia:** o cartão depositado não é recolhido e o giro não é liberado. O leitor 1 continua
  funcionando normalmente (PG 30). Isso confirma o que o worker faz (nega e devolve o uso), e que a
  entrada pelo leitor 1 não deve parar.
- O leitor 2 da urna pode ser configurado como Somente Entrada ou Somente Saída (página Web server). Nossa
  produção usa Somente Entrada, ou seja, urna na entrada. O manual descreve a urna **na saída** como o caso
  padrão. A configuração de produção é suportada pelo menu, mas é uma configuração diferente do exemplo do
  manual.

## 4. Sentido do giro e direção

- "Posição" da catraca, Direita ou Esquerda, determina o sentido de entrada e de saída pelo giro do braço
  (página Web server, seção "Posição"). O padrão é Direita, com entrada anti-horária.
- O padrão de liberação do giro é para os dois lados. A configuração de sentido por leitor e acionamento
  muda isso (PG 129).
- O leitor 1 tem opções de sentido invertido: Entrada Invertido, Saída Invertido e Entrada e Saída Invertido
  (página Web server, seção "Leitor 1").
- Os pictogramas da tampa mostram o sentido liberado (PG 130 a 134).

## 5. Quando o registro é gravado

- "O registro somente é realizado na memória da Catraca após o giro ser concluído" (página "Operações
  nas catracas 4"). Esse é o ponto de gravação do bilhete. Se o giro não acontece, o registro não existe
  na catraca, e o sistema precisa tratar isso como uso devolvido, sem bilhete.
- Acesso negado por lista de controle de acesso tem mensagem própria (página "Operações").

## 6. Revista e função

- Revista é selecionada só na saída, e só no modo off-line (página "Operações").
- "Função" de 0 a 9 (até 10 funções) permite um tipo de registro por função, com acionamento e consulta de
  lista próprios. Registro com função é gravado com um código na memória da catraca (página "Operações").
- Para escolher a função no teclado: tecla F seguida do número, depois o cartão (página "Operações").

## 7. Exemplo de configuração do próprio manual

Exemplo de tela (página "Configuração das catracas 4"), catraca Fit 4 sem urna:

- Acionamento 1: Entrada e Saída, giro de 5 s.
- Acionamento 2: Desabilitado.
- IP do servidor: 192.168.1.100 (computador do software). IP do Inner: 192.168.1.125.
- Porta de comunicação: 3570. Número do Inner: 1.

Esse exemplo é o mesmo que o operador vai digitar na catraca. Ele mostra que o número do Inner e o IP
do servidor são os dois campos que precisam bater com o cadastro.

## 8. Desabilitar o Web Server

A página "Web server nas catracas 4" diz que o Web Server pode ser desabilitado:

- Off-line: pelo software Gerenciador de Inners.
- On-line: pela configuração no software TopAcesso.

Essa é a resposta pública da Topdata para a pergunta de fechar o Web Server. A função correspondente no SDK
é `DesabilitarWebServer(byte)`, que o estudo já tinha identificado. Não usamos hoje, e o estudo não
confirmou se ela persiste. Ver T32 no `docs/34`.

## 9. O que muda no nosso código e nas decisões

Confirmado pelo manual:

1. **Urna na entrada (produção):** fica como está. O manual descreve urna na saída como padrão, então é
   preciso registrar que a configuração de produção é uma opção do menu, e não o exemplo do manual.
2. **Prazo do giro definido pelo operador:** confirmado. O Acionamento 1 tem tempo de 1 a 255 s no Web
   Server. O padrão é 5 s. O SDK aceita de 0 a 50 s (`docs/34`, FUN 17). Nosso código valida 0 a 50 s.
   Um tempo acima de 50 s não é possível pelo SDK.
3. **Prazo de cancelamento do giro:** hoje o worker espera o tempo do Acionamento 1 mais 3 s de margem
   (`MargemDoGiro`, `DeviceStateMachine.cs:56`). O manual diz que a catraca cancela sozinha no tempo
   configurado e envia a origem 5. A margem é um reforço para o caso de a origem 5 se perder. Decidir se
   fica ou se sai. Se sair, o cancelamento acontece exatamente no tempo do operador, como no fluxo descrito.
4. **Tempo da urna:** a produção usa 15 s no Acionamento 2 (`MontadorDaConfiguracao`). O manual padrão é 5 s,
   e a faixa é de 1 a 255 s. Está dentro da faixa. Confirmar na bancada que 15 s é o tempo que o cartão
   precisa para cair.
5. **Registro do bilhete:** a catraca só grava após o giro. O sistema deve tratar a origem 5 como uso
   devolvido, sem bilhete.
6. **Urna cheia e leitor 1:** a entrada pelo leitor 1 continua durante urna cheia. Confirmar que o worker
   não bloqueia o leitor 1 nessa condição. Hoje o código nega e devolve o uso, mas não há teste específico
   para o leitor 1 nesse caso.

## 10. O que o manual não cobre

- A lista de funções da DLL (`ConfigurarAcionamento1/2`, `LiberarCatraca*`, `AcionarRele2`) está no Manual de
  Desenvolvimento, que já está em `vendor/topdata`. Não está neste PDF.
- Os códigos de origem 1 a 21 também estão no Manual de Desenvolvimento.
- O manual não diz qual é a janela da urna (quanto tempo o cartão tem para cair). Diz só que o acionamento
  tem tempo configurável.

## 11. Próximos testes na bancada

- **U1:** o cartão cai com o Acionamento 2 em 15 s? (já decidido que a função 3 é a da urna; falta o tempo.)
- **U8:** depois do giro, em quantos segundos o próximo cartão é lido sem fechamento por software?
- **U-A:** com o Acionamento 1 em 5 s, quantas pessoas completam o giro? E em 15 s?
- **U-M:** o cancelamento sem margem (exatamente no tempo) dispara a origem 5 de forma confiável?
- **U-C:** com a urna cheia, o leitor 1 continua liberando a passagem?
