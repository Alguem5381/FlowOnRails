# Publicação no NuGet

A versão em preparação é **0.2.0**. O identificador do pacote é `FlowOnRails`, de propriedade da conta NuGet `alguem5381`.

## O que dispara cada fluxo

| Evento | CI (`ci.yml`) | Publicação (`cd.yml`) |
|---|---|---|
| Push em `main` ou `develop` | Restore, build, testes e geração local do pacote, em Release | Não publica |
| Pull request para `main` ou `develop` | Mesma validação | Não publica |
| Criação de uma tag sem release | Não dispara por esse evento | Não publica |
| Release salva como rascunho | Não dispara por esse evento | Não publica |
| Release publicada no GitHub | Validação no próprio CD | Valida a tag, restaura, compila, testa, gera e envia o pacote ao NuGet |

O CD usa `release: types: [published]` e faz checkout da tag da release. Esse evento também inclui pré-releases publicadas; nesse caso, a versão no projeto deve ter o sufixo correspondente. A tag `v0.2.0` ou `0.2.0` corresponde à versão atual. Uma tag diferente falha antes da autenticação e do envio.

Não há gatilho de publicação por push nem execução manual. A configuração existente de `concurrency` serializa execuções para a mesma tag. Reexecutar uma release já publicada no NuGet pode falhar por versão duplicada: o fluxo não silencia esse conflito.

## Configuração única do acesso

O workflow usa [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) com [NuGet/login](https://github.com/NuGet/login). Não exige guardar uma API key permanente no repositório.

Na conta NuGet, conferir ou criar uma política com estes dados:

| Campo | Valor |
|---|---|
| Proprietário da política/pacote | `alguem5381` |
| GitHub Repository Owner | `Alguem5381` |
| Repository | `FlowOnRails` |
| Workflow File | `cd.yml` (somente o nome do arquivo) |
| Environment | Em branco: o job atual não declara um GitHub Environment |
| Escopo de pacote | `FlowOnRails` |
| Operação necessária | Publicar novas versões de um pacote existente |

Se já existir uma política compatível, verificar se está ativa. Não é necessário criar outra. O nome de usuário fornecido a `NuGet/login` deve corresponder à conta proprietária da política. O job solicita `id-token: write`; o NuGet troca o token OIDC por uma credencial temporária, usada somente no passo de envio.

O login no navegador serve para configurar e conferir essa política. Depois disso, o workflow autentica por conta própria a cada release.

## Procedimento para uma versão

1. Atualizar `Version` em `src/FlowOnRails/FlowOnRails.csproj`, se necessário, e revisar as alterações incompatíveis documentadas no README.
2. Enviar a branch de desenvolvimento e obter CI aprovado para o código que será lançado.
3. Integrar o código e os workflows à `main`. O CD ainda não está presente na `main` no momento desta revisão; publicar uma release da `main` antiga não disponibiliza o fluxo novo.
4. Confirmar a política ativa no NuGet e que a versão ainda não existe no feed.
5. Criar uma release no GitHub apontando para o commit revisado, com tag correspondente à versão do projeto; para esta versão, `v0.2.0`.
6. Publicar a release quando quiser disparar o envio. Conferir o resultado do workflow e a disponibilização/indexação do pacote no NuGet.

A publicação da release é o passo que autoriza o envio automático. Não é necessário fazer um push manual do pacote pelo computador.

## Estado verificado em 02/11/2026

- A 0.1.0 está disponível no [NuGet](https://www.nuget.org/packages/FlowOnRails).
- A [issue #1 — Add cd for nuget deploy](https://github.com/Alguem5381/FlowOnRails/issues/1) continua aberta.
- O GitHub exibe duas execuções antigas bem-sucedidas de CI para `main`; isso não valida as alterações atuais da 0.2.0.
- Os workflows atualizados incluem testes antes da publicação e conferência da tag.
- O login da conta `alguem5381` e a política Trusted Publishing `FlowOnRails` foram conferidos no navegador: política ativa, proprietário GitHub `alguem5381`, repositório `FlowOnRails`, workflow `cd.yml`, sem ambiente declarado. O escopo existente permite novos pacotes e versões com padrão `*`; nenhuma permissão foi criada ou alterada nesta revisão.
- Restore, build em Release, 547 testes e geração do pacote 0.2.0 passaram localmente, sem avisos no build.
- O passo de conferência de versão aceitou `v0.2.0` e `0.2.0` e rejeitou `v0.1.0`. YAML, comandos de shell e gatilhos foram validados.
- O envio da branch atual, o CI remoto e a execução real do CD ainda precisam ocorrer antes de considerar a publicação concluída. A troca do token OIDC só pode ser validada em uma execução do GitHub Actions.

Referência do gatilho: [GitHub Actions — release](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release).
