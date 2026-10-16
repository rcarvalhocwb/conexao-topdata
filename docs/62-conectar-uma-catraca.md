# Conectar uma catraca ao sistema

Para o operador, sem ferramenta extra. O que aparece na tela está no painel, na tela **Catracas**, e no
**Assistente de configuração** (passo "Verificar o ambiente").

## Como a conexão funciona

A catraca é quem abre a conexão: ela se conecta ao **IP deste PC** e à **porta** que estão configurados
nela. O sistema escuta essa porta (padrão **3570**). Por isso o trabalho é digitar três coisas na catraca
e confirmar que a conexão chegou.

## Os três dados para digitar na catraca

| Na catraca | O que digitar | De onde vem |
|---|---|---|
| IP do servidor | o IP deste PC | tela Catracas, linha "Este PC: IP …" |
| Porta do servidor | a porta do programa das catracas | mesma linha; padrão 3570 |
| Número do equipamento (Inner) | o número da catraca, de 1 a 99 | o mesmo número que está no cadastro da instalação |

O tipo de conexão é **TCP/IP com porta fixa**. Como entrar na configuração da catraca e onde ficam esses
campos está no manual do equipamento (Web Server da catraca).

Se o PC tiver mais de um endereço (por exemplo, cabo e Wi-Fi), use o que está **na mesma rede da catraca**.
A tela mostra todos.

## Passo a passo

1. **Confira o PC.** No Assistente de configuração, em "Verificar o ambiente", os itens
   *Porta 3570 para as catracas* e *Endereço deste PC para digitar na catraca* têm que estar OK.
   - Porta em uso: se o programa das catracas já está rodando, é normal. Se não está, feche o outro
     programa que usa a porta.
   - Sem endereço: conecte o cabo ou o Wi-Fi e atualize a tela.
2. **Configure a catraca** com os três dados da tabela acima.
3. **Ligue a catraca na rede** e espere até um minuto.
4. **Confira a tela Catracas.**
   - A catraca aparece como **Atendendo**: pronto.
   - Aparece uma linha com as instruções em cima da catraca: a conexão ainda não chegou. Confira os três dados.
   - Aparece "Conexão chegando de: …" no topo, mas nenhuma catraca atende: o IP chegou ao PC, mas o número
     do equipamento não bate com o cadastro. Confira o número Inner na catraca.

## O que o sistema mostra quando algo não bate

| Mensagem | O que fazer |
|---|---|
| "A porta 3570 já está aberta: outro programa das catracas…" | Feche o outro programa, ou reinicie o serviço do Rayzer XAcess. |
| "Não foi possível abrir a porta 3570…" | Confira se outro programa usa a porta e se a rede do PC funciona. |
| "Este PC não tem endereço de rede ativo" | Conecte o cabo ou o Wi-Fi. |
| "Chegou conexão de 192.168.0.50, mas nenhuma catraca atende" | O IP da catraca chegou; confira o número do equipamento (Inner). |
| "Liberar no firewall" (Assistente) | O Windows bloqueou a entrada das catracas. Clique no botão. |

## Limites desta versão

- O sistema **não lê de volta** a configuração da catraca. A função que faria isso (ReceberInformacoesInner)
  não tem assinatura publicada no SDK que temos; sem ela, o sistema não confere sozinho se a catraca
  aponta para o IP certo. A confirmação é a conexão chegando e a catraca aparecendo como atendendo.
- A liberação do firewall é por programa e só para a **sub-rede local**. Uma catraca em outra rede (por
  roteador) não passa sem ajuste no firewall.
- A varredura da rede não existe de propósito: o sistema mostra só o que o Windows já lista sobre este PC
  e as conexões que chegaram à porta. Para achar o IP de uma catraca que ainda não conectou, use o
  programa de varredura de rede, como antes.

## Onde está no código

- `src/Edge.Supervisor/RedeDoPc.cs`: endereços deste PC, conexões na porta, porta livre.
- `src/Edge.Supervisor/Instalacao/AssistenteDeConfiguracao.cs`: as duas verificações do assistente.
- `src/Edge.Worker/DiagnosticoDaPorta.cs`: as frases para o retorno da abertura da porta.
- `src/Desktop.ViewModels/Textos.cs`: a instrução por catraca e o resumo da rede.
- Testes: `tests/Integration/RedeDoPcTests.cs`, `tests/Unit/ConexaoDaCatracaTests.cs`.
