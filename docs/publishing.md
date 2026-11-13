# Publicação no NuGet

O repositório prepara três pacotes independentes. A versão 0.3.0 inclui o
analisador no pacote base, desativado por padrão.
A auditoria e o acompanhamento das correções estão em `docs/reviews`.

| Pacote | Projeto | Versão alvo | Formato da tag |
|---|---|---|---|
| `FlowOnRails` | `src/FlowOnRails/FlowOnRails.csproj` | `0.3.0` | `v<versão>` ou `<versão>` |
| `FlowOnRails.Analysis` | `src/FlowOnRails.Analysis/FlowOnRails.Analysis.csproj` | `0.3.0` | `analysis/v<versão>` |
| `FlowOnRails.AspNetCore` | `src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj` | `0.1.0` | `aspnetcore/v<versão>` |

O pacote base inclui os novos atributos de contrato e metadados. O pacote Analysis
é uma API de desenvolvimento que depende de Roslyn e não da biblioteca de execução.
O pacote base inclui motor, analisador com gerador e correções de editor em
`analyzers/dotnet/cs`, sem dependências NuGet de compilação ou execução.
A análise é ativada com `FlowOnRailsEnableFlowAnalysis=true`.

O pacote ASP.NET atual continua incluindo seu próprio gerador e dependendo da
base. Sua próxima versão ainda será adaptada à API de análise; essa adaptação não
foi incluída nesta etapa. Os projetos internos do analisador, das correções e do
gerador ASP.NET não são publicados separadamente.

## O que dispara cada fluxo

| Evento | CI (`ci.yml`) | Publicação (`cd.yml`) |
|---|---|---|
| Push em `main` ou `develop` | Compila, testa, empacota e verifica consumidores isolados | Não publica |
| Pull request para `main` ou `develop` | Mesma validação | Não publica |
| Tag sem release ou release em rascunho | Não publica | Não publica |
| Release publicada no GitHub | Validação no próprio CD | Publica somente o pacote selecionado pela tag |

O CD faz checkout da tag e executa `scripts/select-release-package.sh` antes da
autenticação. Por exemplo, `v0.3.0` seleciona exclusivamente
`FlowOnRails.0.3.0.nupkg`. A versão da tag deve corresponder exatamente
à versão configurada no projeto, inclusive em pré-releases. Tags desconhecidas e
versões incompatíveis falham. O nome da release não seleciona o pacote.

O fluxo gera os três pacotes para validação, mas envia apenas o arquivo
selecionado, sem curinga. Criar uma release não substitui a auditoria: o workflow
não conhece o resultado dessa revisão, que deve ocorrer antes da publicação.

## Validação dos pacotes

CI e CD executam os testes .NET e os testes do seletor de releases. Depois, geram
os três `.nupkg` e executam `scripts/verify-packages.py`.

O SDK de produção é selecionado pelo `global.json` da raiz: 10.0.1xx, aceitando
atualizações de correção. As ferramentas usam C# 14.0 explicitamente. Os workflows
também instalam SDK 11.0.1xx e executam a suíte no .NET 11 a partir de
`tests/compatibility`, sem mudar os alvos publicados. Veja
[compatibilidade](compatibility.md) para os cenários C# 15 e os requisitos do Roslyn.

O verificador executa estas etapas:

- Confere README, documentação XML, licença, frameworks e dependências reais.
- Confere as DLLs de compilação e a configuração de ativação do analisador.
- Copia os consumidores de `tests/packaging` para fora do repositório e restaura
  em um cache novo, usando apenas `PackageReference`.
- Executa sucesso/falha da base e respostas HTTP/MVC do satélite; um cenário
  inválido deve produzir `FORHTTP004` pelo gerador empacotado.
- Verifica exportação e leitura de resumos entre dois projetos, FORFLOW001/002/003
  com ativação explícita, além da ausência de diagnósticos e metadados no modo padrão.
- Usa a API pública em uma compilação Roslyn, incluindo um contrato aberto.
- Repete os consumidores em .NET 10 e .NET 11; no 11, verifica recursos C# 15
  e a API com o compilador do SDK. Uma execução adicional ativa `runtime-async`.
- Confere que os consumidores de aplicação não recebem Roslyn ou o motor como
  dependências de execução. O consumidor da API é uma ferramenta e precisa de Roslyn.

Para releases do ASP.NET, uma segunda execução com `--published-base`
restaura a base exclusivamente do NuGet público. Assim, depender de uma versão
que só existe localmente impede a publicação antes da autenticação. A base 0.3.0
precisa ser publicada primeiro. A API Analysis pode ser publicada independentemente;
ela é destinada aos desenvolvedores dos satélites.

Para repetir a validação localmente, a partir da raiz:

```sh
dotnet restore FlowOnRails.slnx
dotnet build FlowOnRails.slnx -c Release --no-restore
dotnet test FlowOnRails.slnx -c Release --no-build --no-restore
python3 -m unittest discover -s tests/packaging -p 'test_*.py'
dotnet pack src/FlowOnRails/FlowOnRails.csproj -c Release --no-build --no-restore --output ./artifacts/packages
dotnet pack src/FlowOnRails.Analysis/FlowOnRails.Analysis.csproj -c Release --no-build --no-restore --output ./artifacts/packages
dotnet pack src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj -c Release --no-build --no-restore --output ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages --framework net10.0 --framework net11.0
python3 scripts/verify-packages.py ./artifacts/packages --framework net11.0 --runtime-async
bash scripts/select-release-package.sh v0.3.0
# Depois que a base 0.3.0 estiver publicada:
python3 scripts/verify-packages.py ./artifacts/packages --published-base
```

Use um diretório de saída sem versões antigas dos mesmos pacotes. A validação
requer os SDKs .NET 10.0.1xx e 11.0.1xx, Python 3 e uma porta temporária de loopback.
Os pacotes FlowOnRails
vêm da pasta local no modo padrão; o consumidor da API restaura dependências Roslyn
do NuGet. `--tooling-dependency-feed <fonte>` permite indicar um feed alternativo,
inclusive uma pasta de arquivos `.nupkg` para uma execução sem rede. O modo
`--published-base` exige acesso ao NuGet público.

## Configuração do acesso

O workflow usa [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
com `NuGet/login@v1`, obtendo uma credencial temporária após as validações.

| Campo da política NuGet | Valor esperado |
|---|---|
| Conta proprietária | `alguem5381` |
| GitHub Repository Owner | `Alguem5381` |
| Repository | `FlowOnRails` |
| Workflow File | `cd.yml` |
| Environment | Em branco, pois o job não declara um environment |
| Pacotes permitidos | Os três identificadores acima, por exemplo com `FlowOnRails*` |
| Operações | Novas versões e criação dos novos pacotes |

O job mantém `id-token: write`. Uma política limitada à base ou apenas a novas
versões não autoriza a criação dos novos pacotes. Esta preparação não alterou nem
verificou a política externa da conta NuGet. Confira seu escopo antes da release.

## Procedimento após a auditoria

1. Resolver os achados, revisar API pública, documentação e versões finais.
2. Enviar os commits revisados, obter CI aprovado e integrar à `main`.
3. Conferir o escopo da política NuGet e a disponibilidade das versões.
4. Publicar a base com a tag `v0.3.0` e conferir sua disponibilidade no NuGet.
5. Publicar a API separadamente com `analysis/v0.3.0`, quando desejado.
6. Publicar o satélite separadamente, quando sua própria versão estiver revisada.

Cada publicação exige sua própria release. O envio de uma versão já existente
falha; o workflow não ignora o conflito. A validação local não substitui a execução
real da autenticação e do envio no GitHub Actions.

Referência: [evento release no GitHub Actions](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release).
