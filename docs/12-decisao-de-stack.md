# 12 — Decisão de stack: qual linguagem, e por quê

## 1. A pergunta certa

"Qual a melhor linguagem?" tem uma resposta desconfortável: **para a maior parte do
sistema, a linguagem é livre; para uma peça pequena e crítica, ela é quase imposta.**

A EasyInner.dll é uma DLL **Win32 x86, bloqueante e não thread-safe**. Qualquer linguagem
que fale FFI e rode como processo 32 bits no Windows consegue chamá-la. O que muda entre
as opções não é "se dá", é **quanto risco de marshalling você aceita numa biblioteca
nativa que derruba o processo inteiro quando você erra um ponteiro**.

Então a decisão se parte em duas:

- **O worker que toca a DLL** — escolha conservadora, guiada pelos exemplos oficiais.
- **Todo o resto** (interface, domínio, banco, sincronização) — escolha livre, guiada por
  produtividade, acessibilidade e manutenção.

## 2. O worker: C# sobre .NET moderno, compilado `win-x86`

### Por quê

1. **Os exemplos oficiais são em C#.** O manual inteiro é escrito em C#, e as assinaturas
   documentadas (`ref byte`, `StringBuilder Cartao`) são **literalmente** as convenções de
   marshalling do P/Invoke do .NET. Traduzir isso para outra linguagem é reescrever, à mão,
   um contrato binário que já vem pronto e testado.
2. **`StringBuilder` não é detalhe.** Várias funções devolvem texto por buffer pré-alocado.
   Em C# isso é uma linha; em Rust, Go ou Python é um ponteiro cru, um tamanho que
   ninguém documenta e um estouro de buffer que vira corrupção de heap silenciosa.
3. **O pacote de exemplos traz o `EasyInner.cs`** — o wrapper P/Invoke pronto. Usar C# é
   partir de um arquivo conferido pelo fabricante em vez de deduzir assinaturas.
4. **O worker é pequeno e descartável.** Ele não precisa da "melhor" linguagem: precisa da
   linguagem com **menos chance de erro de interoperabilidade**.

### O que NÃO recomendo para o worker, e por quê

| Opção | Por que não |
|---|---|
| **Rust** | Excelente para o resto do mundo, mas aqui você troca a segurança de memória do Rust por um bloco `unsafe` em volta de *toda* a DLL. O ganho evapora e o custo de escrever os bindings à mão fica |
| **Go** | `cgo` em x86 no Windows, chamadas bloqueantes prendendo threads do runtime, e nenhum exemplo oficial. Fricção sem contrapartida |
| **Python** | `ctypes` funciona, mas empacotar um serviço Windows x86 confiável com watchdog e sem GIL atrapalhando é mais trabalho do que o C# já entrega pronto |
| **Java** | Tem exemplos oficiais, mas exige JVM 32 bits ou JNA configurado, e dobra o número de runtimes na máquina de campo |
| **Delphi / VB6** | Exemplos oficiais existem, mas o ecossistema de testes, CI e observabilidade é muito inferior ao que este projeto exige |

### ⚠️ O risco técnico nº 1, que precisa ir para a bancada antes de tudo

O manual diz que a DLL exige **.NET Framework 3.5** instalado. Isso sugere que a
EasyInner.dll pode ser **mixed-mode** (C++/CLI) ou depender de um assembly gerenciado
antigo. Se for o caso, carregá-la a partir de um processo **.NET moderno** faria dois CLRs
conviverem no mesmo processo.

Isso é tecnicamente suportado pelo Windows, mas **não está provado para esta DLL**.

**Teste de bancada `HIL-STACK-01`, que precede qualquer decisão de código:**

> Um processo .NET 10 compilado `win-x86` consegue carregar a EasyInner.dll, chamar
> `AbrirPortaComunicacao`, `TestarConexaoInner` e `ReceberDadosOnLine`, e sobreviver 4 h
> de operação contínua?

- **Passou** → worker em .NET 10, stack única no projeto inteiro.
- **Falhou** → o worker (e **só** ele) vira um executável **.NET Framework 4.8 x86**, que
  conversa com o resto por IPC. Custo: um projeto legado pequeno e isolado. Nenhum
  impacto no domínio, na interface ou nos testes.

Este é exatamente o motivo de a arquitetura isolar a DLL em processo separado desde o
início ([ADR-0001](ADR/ADR-0001-isolar-easyinner-em-processo-x86.md)): o pior cenário
custa um projeto pequeno, não o produto.

## 3. O resto do sistema: .NET 10 (LTS)

Domínio, aplicação, banco, sincronização, contratos e testes em **C# / .NET 10**, x64.

Não por inércia — por três razões concretas:

- **Uma stack só.** Duas linguagens significam dois toolchains, dois pipelines de CI, duas
  culturas de teste e uma fronteira de serialização a mais para depurar às 19h de um
  sábado. O ganho teórico de usar Rust no núcleo não paga isso num sistema cujo gargalo é
  uma DLL bloqueante de 32 bits.
- **O domínio roda em CI Linux.** Nada em `Access.Domain` toca Windows; os testes de
  regra, máquina de estados e normalização de credencial rodam em contêiner. Isso é o que
  mantém a separação honesta.
- **Suporte até nov/2028** ([ADR-0016](ADR/ADR-0016-runtime-dotnet.md)).

### Interface: WPF

Mantida a decisão do [ADR-0012](ADR/ADR-0012-wpf-versus-winui.md). A alternativa moderna
seria **Avalonia** (visual mais atual, roda em Linux para desenvolvimento), mas:

- o sistema inteiro já é **preso ao Windows pela DLL** — portabilidade de UI não vale nada
  aqui;
- **acessibilidade** (WCAG, leitor de tela, alto contraste, teclado) é requisito duro deste
  projeto, e o suporte a UI Automation do WPF é o mais maduro da plataforma;
- WPF não exige runtime externo além do .NET — uma peça a menos para falhar na instalação
  de campo.

Trocar por Avalonia continua barato: as ViewModels não dependem do framework gráfico.

## 4. Plano B, que vale abrir agora: o protocolo sob NDA

O manual, na seção 6.7, diz textualmente:

> *"a integração direta via protocolo TCP/IP (sem a DLL, conforme documentação de baixo
> nível e **solicitação de NDA**) pode ser uma alternativa. Essa abordagem permite maior
> controle sobre o gerenciamento de conexões e threads"*

**É a informação mais valiosa desta análise.** Se a Topdata liberar o protocolo:

| Restrição hoje | Com o protocolo |
|---|---|
| Processo x86 obrigatório | Some |
| Windows obrigatório | Some — o gateway pode rodar em Linux |
| Thread única, DLL não thread-safe | Some — I/O assíncrono real |
| ~30 equipamentos por processo | Some — o limite passa a ser a máquina |
| Uma porta TCP por worker | Some — um servidor, milhares de conexões |
| Erro 8 / GPF / registro de DLL | Some |
| Linguagem quase imposta | Livre — **aí sim** Rust ou Go fazem sentido |

Para um evento de 50.000 pessoas com dezenas de catracas, isso é a diferença entre
orquestrar uma dúzia de processos frágeis e rodar **um serviço**.

**Recomendação:** solicitar o NDA **agora**, em paralelo à Fase 1. Não é caminho crítico —
a Fase 1 avança com mock e simulador de qualquer forma — mas o prazo de resposta de
fabricante é longo, e essa porta precisa estar aberta antes da Fase 2.

Se vier, a migração é contida: só o `Topdata.EasyInner.Adapter` é substituído. O domínio,
a interface, o banco e os testes não mudam **uma linha** — que é precisamente o que a
interface `ITopdataInnerAdapter` existe para garantir.

## 5. Decisão consolidada

| Camada | Stack | Alvo |
|---|---|---|
| `Edge.Worker.X86` | C# / .NET 10 (fallback: .NET FW 4.8) | `win-x86` |
| `Edge.Supervisor` | C# / .NET 10 | `win-x64`, Windows Service |
| `Desktop.App` | C# / .NET 10 + WPF (MVVM) | `win-x64` |
| `Access.Domain` / `Application` | C# / .NET 10, sem dependência de SO | multiplataforma, CI Linux |
| `Access.Infrastructure.SQLite` | C# + SQLite (WAL) | multiplataforma |
| `Sync.*` / `Contracts` | C# + gRPC/Protobuf | multiplataforma |
| `Topdata.Facial.Adapter` | C# + WebSocket/JSON | x64, **não** precisa ser x86 |
| Futuro gateway sob NDA | Rust ou Go, a decidir | Linux ou Windows |

Registrado em [ADR-0019](ADR/ADR-0019-stack-e-linguagem.md).
