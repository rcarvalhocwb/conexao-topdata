# Acervo Topdata — SDKs, DLLs e manuais

## Por que esta pasta está (quase) vazia no Git

O conteúdo desta pasta é **software e documentação proprietários da Topdata Sistemas de
Automação**. Este repositório é **público**. Portanto:

> **Nenhum binário, instalador, DLL ou PDF da Topdata é versionado aqui.**
> Redistribuir software proprietário num repositório público não é nosso direito.

O que existe aqui é: **o catálogo dos downloads oficiais** e **scripts que os baixam** para
a sua máquina. Rode o script, os arquivos aparecem em `vendor/topdata/sdk/`, e o
`.gitignore` garante que eles não sejam commitados por acidente.

Se o repositório se tornar privado e a Topdata autorizar, a decisão pode ser revista —
mas a conversa precisa acontecer antes, não depois.

## Como materializar o acervo

```bash
# Linux/macOS (para inspecionar os exemplos; a DLL só roda em Windows)
./vendor/topdata/fetch-sdk.sh

# Windows (máquina de desenvolvimento ou de bancada)
powershell -ExecutionPolicy Bypass -File vendor\topdata\fetch-sdk.ps1
```

Os scripts baixam tudo, conferem se o arquivo é mesmo um ZIP/PDF válido e **gravam o
SHA-256 de cada um** em `sdk/CHECKSUMS.txt`. Guarde esse arquivo: ele é a linha de base
para detectar troca de DLL em campo — causa conhecida de falha
([R-05](../../docs/08-riscos-e-validacoes-topdata.md)).

> ℹ️ **Este container não consegue baixar.** A política de rede bloqueia `dropbox.com`
> (403 no túnel do proxy). Rode os scripts na sua máquina, ou libere o domínio nas
> configurações de rede do ambiente.

## Catálogo oficial

Todos os links vêm das páginas públicas de download do portal do integrador.
Verificados em **24/09/2026**.

### SDK Inner Acesso (EasyInner) — catracas e coletores da Linha Inner

| Arquivo | Conteúdo | Essencial? |
|---|---|---|
| `Manual-de-Integracao-SDK-Inner-Acesso.pdf` | Manual completo (base dos docs [11](../../docs/11-capacidades-do-sdk.md)) | já analisado |
| `Instalador-DLLs-SDK-InnerAcesso.zip` | Instalador que registra as DLLs | **sim — para a bancada** |
| `DLLs.zip` | DLLs para registro manual via `regsvr32` | alternativa ao instalador |
| `CSharp-Exemplo-Online.zip` | Exemplo C# on-line (ponto de partida) | **sim — traz o `EasyInner.cs`** |
| `CSharp-Exemplo-Completo.zip` | Exemplo C# completo | **sim** |
| `Delphi-Exemplo-Online.zip` | Exemplo Delphi on-line | referência |
| `Delphi-Exemplo-Completo.zip` | Exemplo Delphi completo | referência |
| `Java-Exemplo.zip` | Exemplo Java (JRE 1.8) | referência |

### SDK Leitor Facial — F4, T4, T4-50k e Catracas Easy

| Arquivo | Conteúdo | Essencial? |
|---|---|---|
| `SDK-Facial.zip` | Pacote do SDK Facial | **sim** |
| `SDK-CSharp-Leitor-Facial.zip` | Exemplo C# | **sim** |
| `SDK-Facial-Delphi.zip` | Exemplo Delphi | referência |
| `Manual-SDK-Leitor-Facial/` | Pasta de manuais (Dropbox) | **sim** |
| `Manual-Comandos-Leitor-Facial-rev03.zip` | Comandos p/ cadastro em Catracas, Inner Ponto e Inner Acesso | **sim** |

Requisitos declarados: Windows e **.NET Core 3.1** para os exemplos faciais;
Windows e **.NET Framework 3.5** para os exemplos EasyInner.

## O que abrir primeiro, e por quê

1. **`CSharp-Exemplo-Online.zip` → `EasyInner.cs`.** É o wrapper P/Invoke oficial. Fecha as
   6 assinaturas que faltam e o enum `Enumeradores.Retorno` com os valores de
   `RET_SEM_BILHETES` e "sem eventos" — as últimas lacunas de
   [`docs/11`](../../docs/11-capacidades-do-sdk.md), seção 5.
2. **`Instalador-DLLs-SDK-InnerAcesso.zip`** na máquina de bancada, para rodar o
   `HIL-STACK-01`: *um processo .NET moderno compilado `win-x86` consegue carregar a
   EasyInner.dll?* Essa resposta define a stack do worker
   ([ADR-0019](../../docs/ADR/ADR-0019-stack-e-linguagem.md)).
3. **`SDK-Facial.zip`**, se o módulo facial estiver no escopo (pergunta B9).

## Registro de conformidade

- Os links são **públicos**, sem login. Nada aqui contorna controle de acesso.
- A Topdata mantém um [cadastro de integrador](https://www.topdata.com.br/cadastro-de-integrador/)
  — recomendado fazê-lo antes de uso comercial, e necessário para suporte e para o
  **NDA do protocolo de baixo nível** ([ADR-0021](../../docs/ADR/ADR-0021-porta-por-worker-e-protocolo-nda.md)).
- Suporte ao desenvolvedor: `desenvolvimento@topdata.com.br`.

---

## SDK 6.0.2.0 recebido em 24/09/2026

O instalador `SDK-EasyInner-6.0.2.0.exe` (Inno Setup, 14 MB) foi fornecido pelo cliente e
extraído **fora do repositório**. Nada dele é versionado aqui, e não deve ser.

### Restrição de licença — leia antes de usar

O cabeçalho de todos os fontes de exemplo diz, palavra por palavra:

> *"este exemplo deve ser utilizado apenas para demonstrar a comunicação com os
> equipamentos da linha inner e não deve ser alterado, por este motivo ele não deve ser
> incluso em suas aplicações comerciais."*

Portanto: **o `EasyInner.cs` da Topdata não entra no produto.** Ele é lido como
documentação da interface — nomes de símbolo, tipos de parâmetro e convenção de chamada são
fatos sobre a ABI da DLL, e é para isso que o SDK existe. As declarações do produto são
escritas por nós, no nosso estilo, com o identificador da matriz em cada uma.

Isso também era regra do projeto desde o início: *preservar os exemplos oficiais como
referência somente leitura*.

### O que o pacote contém

| Caminho | O que é |
|---|---|
| `app/DLLs/EasyInner.dll` | A DLL. I386, 32 bits, **774 funções exportadas por nome** |
| `app/DLLs/Inner2K.dll`, `InnerTCP.DLL`, `InnerTCPLib.dll` | DLLs de apoio — a ausência delas é o retorno 4, 5 ou 6 |
| `app/Exemplos/CSharp/.../COM/EasyInner.cs` | 231 declarações `DllImport`. A pasta se chama COM, mas é P/Invoke |
| `app/Exemplos/CSharp/.../Entity/Enumeradores.cs` | Enums oficiais: retorno, origem, tipo de leitor, função de acionamento |
| `app/DLLs/*.bat` | Registro das DLLs |

### Três números que não batiam

| | Fonte | Valor |
|---|---|---|
| Funções na DLL | Tabela de exportação | **774** |
| Funções declaradas | Exemplo oficial em C# | **231** |
| Funções na nossa matriz | Manual de integração | **58** |

O manual descreve menos de 8% do que a DLL exporta.
