# 58 — Recuperação da senha de administrador pelo suporte Rayzer

> Pedido do dono do produto em 14/10/2026: "na falta de outro administrador, devemos ter o acesso
> master do desenvolvedor". Este documento diz o que foi construído no lugar de uma senha mestra, e
> por quê.

## 1. Por que não uma senha mestra

Uma senha mestra seria um usuário escondido que passa por cima da auditoria e das permissões, e
isso é proibido neste projeto. Além disso, o repositório e o instalador são públicos: uma senha
guardada no programa acabaria lida por alguém, e abriria **todos** os eventos de uma vez.

## 2. Como funciona: desafio e resposta

1. Na tela de login, a pessoa toca em **Esqueci minha senha**. Primeiro caminho: outro administrador
   gera uma senha provisória na tela Usuários (já existia).
2. Se ela é o único administrador: digita o usuário e toca em **Gerar código de pedido**. O código
   vale **só para esta instalação, só para esse login e por 30 minutos**.
3. Ela manda o código ao suporte Rayzer (mensagem, telefone).
4. O desenvolvedor roda `Rayzer.Suporte assinar suporte-rayzer-privada.pem`, cola o código, digita a
   senha da chave e devolve a **resposta**.
5. A pessoa cola a resposta, escolhe a senha nova (duas vezes) e grava. O serviço confere a
   assinatura com a chave **pública** que vem no programa e só então grava a senha.

O serviço nunca devolve senha (regra do contrato, ADR-0026). Cinco respostas erradas anulam o
pedido. Só funciona para conta de **administrador ativo**; operador recupera com o administrador.
Cada recuperação fica na trilha de usuários como `senha_recuperada_pelo_suporte`, autor
`suporte-rayzer`.

## 3. A chave: só o desenvolvedor tem a privada

| Peça | Onde fica | Pode ser pública? |
|---|---|---|
| Chave privada (`suporte-rayzer-privada.pem`, cifrada com senha) | Só na máquina do desenvolvedor, com cópia de segurança | **Não.** Nunca no repositório, no e-mail ou no instalador |
| Chave pública | `src/Access.Infrastructure.SQLite/ChavesDoSuporte.cs` | Sim: só confere assinatura |

**Enquanto `ChavesDoSuporte.Publicas` estiver vazio, a recuperação pelo suporte fica desligada** e a
tela diz "peça a outro administrador".

### Para ligar (uma vez)

Na máquina do desenvolvedor, com o .NET 10 SDK:

```powershell
dotnet run --project src/Rayzer.Suporte -- gerar-chaves C:\chaves-rayzer
```

A ferramenta pede uma senha (mínimo 12), grava `suporte-rayzer-privada.pem` e mostra a chave
pública. Cole a chave pública em `ChavesDoSuporte.Publicas` (ou mande-a para quem mantém o código),
publique e gere o instalador. **Não mande o arquivo `.pem`.**

### Para atender um pedido

```powershell
dotnet run --project src/Rayzer.Suporte -- assinar C:\chaves-rayzer\suporte-rayzer-privada.pem
```

### Se a chave privada vazar ou se perder

Gere outra, acrescente a pública nova em `ChavesDoSuporte.Publicas`, publique, e depois tire a
antiga. Instalações antigas só aceitam a chave que conhecem: atualize-as.

## 4. Também nesta entrega (login)

- **Olho na senha:** mostra a senha só enquanto o botão está pressionado (mouse ou barra de espaço);
  some ao soltar, ao tirar o mouse ou ao perder o foco.
- **Esqueci minha senha:** explica os dois caminhos acima, na própria tela de login.
