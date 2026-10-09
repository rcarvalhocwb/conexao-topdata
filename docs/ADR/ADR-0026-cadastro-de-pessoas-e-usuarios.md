# ADR-0026: Cadastro local de pessoas e usuários do sistema com login próprio

**Status:** Aceito
**Data:** 2026-10-09
**Decidido por:** o dono do produto. As decisões C1 a C8 estão no [docs/43](../43-cadastro-de-pessoas-e-credenciais.md) §10.

**Relação com outras ADRs:**

- Complementa a ADR-0025, que trata do cadastro de cartões de evento e define quando a nuvem manda.
- Complementa a ADR-0014 (cifra e biometria).
- Substitui, para o painel, o "nome digitado sem login" do docs/27 §11.

## Contexto

O XAcess vai para portarias, academias, condomínios empresariais e para o credenciamento de eventos. Nesses lugares, quem pode passar é uma **pessoa cadastrada no local**, com empresa, sala, horários e portões, e não um ingresso vendido.

Esse cadastro guarda dado pessoal. Sem login, não há como dizer quem viu ou mudou o quê.

## Decisões

1. **Pessoas da portaria são só locais.** Elas nunca sobem para a nuvem nem descem dela. Ingressos de evento continuam na ADR-0025.
2. **Dois modos convivem.** A catraca procura primeiro o **ingresso**, com a regra de hoje, e só depois a **credencial de pessoa**, com a regra do docs/43 §6.
3. **Dados pessoais cifrados por campo.** Valem para nome, documento, contato, data de nascimento, veículo e foto:
   - cifra AES-256-GCM;
   - chave por instalação, guardada no cofre DPAPI da máquina;
   - o identificador do registro entra como dado associado.

   A busca por nome usa HMAC das partes normalizadas, sem guardar o nome em claro. Registros (logs) nunca levam esses dados.
4. **Login próprio.**
   - O sistema instala com **um usuário administrador padrão e senha padrão**.
   - No **primeiro acesso**, a troca é obrigatória: a pessoa informa o nome do administrador real e uma senha nova. O serviço recusa qualquer outra função até a troca.
   - Depois disso, o administrador cria os outros usuários e define **o que cada papel pode fazer** (permissões por função) e quem tem cada papel.
5. **Senhas.**
   - Hash PBKDF2-SHA256 com sal por usuário e 600 mil iterações (recomendação OWASP 2023 para PBKDF2-HMAC-SHA256).
   - Mínimo de 8 caracteres.
   - Bloqueio de 5 minutos depois de 5 erros seguidos.
   - A senha padrão é pública (está no manual) e por isso não abre nada além da troca.
6. **Sessão.**
   - O painel entra com login e senha e recebe uma sessão, que vale enquanto o painel está aberto e expira depois de 12 h sem uso.
   - O token da instalação (ADR-0004) continua valendo como primeira barreira.
   - **Cada RPC exige uma permissão**, e o serviço confere. A tela só esconde o que o serviço já recusaria.
7. **Autoria.** Toda ação passa a gravar **o usuário da sessão**. O nome digitado nas telas antigas fica como complemento, não como identidade.
8. **Biometria fica em fase própria.** Depende da bancada e da regra da ANPD (prevista para 2026). Vai para arquivo separado (`biometrics.db`) e sempre com alternativa: cartão, QR ou senha.
9. **Perfis de pessoa prontos e editáveis:** Colaborador, Prestador, Visitante, Morador, Aluno, Staff, Imprensa, Fornecedor e Artista.
   - Os campos obrigatórios variam por perfil.
   - O CPF só é obrigatório onde o perfil exigir.
   - Retenção sugerida: visitante, 90 dias depois da visita; demais perfis, até inativar mais 180 dias. Cada cliente define a sua.
10. **"Fechar a catraca" pelo software.** A decisão nega tudo com o motivo `CatracaFechada`, com usuário e motivo registrados. Não depende de função da DLL.
11. **Academia:** "inadimplente" é marcado à mão ou vem por planilha. A integração com sistemas de academia fica para depois.

## Consequências

- **Testes e ferramentas:** o serviço sem usuários configurados mantém o comportamento atual. Na instalação, o login passa a ser obrigatório.
- **Painel:** ganha a janela de login e as telas Usuários e Papéis, Pessoas, Empresas e salas, Horários e feriados, e Perfis.
- **Senha padrão:** é um risco conhecido (CWE-1392). Fica mitigado pela troca obrigatória antes de qualquer função, pelo canal só local (named pipe) e pelo bloqueio por tentativas.
- **Perda da chave de cifra:** acontece se o disco for restaurado em outra máquina, porque a chave DPAPI é da máquina. Nesse caso os dados pessoais ficam ilegíveis. Há duas saídas: exportação cifrada com senha do administrador (proposta) ou cadastrar de novo. A operação da catraca segue, porque a decisão não precisa do nome.
