# Publicação no NuGet

O repositório produz dois pacotes independentes:

| Pacote | Projeto | Versão configurada | Formato da tag |
|---|---|---|---|
| `FlowOnRails` | `src/FlowOnRails/FlowOnRails.csproj` | `0.2.0` | `v<versão>` ou `<versão>` |
| `FlowOnRails.AspNetCore` | `src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj` | `0.1.0` | `aspnetcore/v<versão>` |

`FlowOnRails.AspNetCore` inclui o gerador em `analyzers/dotnet/cs` e declara sua
dependência de `FlowOnRails`. O projeto do gerador não é publicado separadamente.
A solução compartilhada permite compilar e testar todos os projetos; cada pacote
mantém seu próprio identificador e sua própria versão.

## O que dispara cada fluxo

| Evento | CI (`ci.yml`) | Publicação (`cd.yml`) |
|---|---|---|
| Push em `main` ou `develop` | Compila, testa, empacota e verifica consumidores isolados | Não publica |
| Pull request para `main` ou `develop` | Mesma validação | Não publica |
| Tag sem release ou release em rascunho | Não publica | Não publica |
| Release publicada no GitHub | Validação no próprio CD | Publica somente o pacote selecionado pela tag |

O CD usa `release: types: [published]` e faz checkout da tag. O script
`scripts/select-release-package.sh` seleciona o projeto e confere sua versão antes
da autenticação. Por exemplo, `aspnetcore/v0.1.0` seleciona exclusivamente
`FlowOnRails.AspNetCore.0.1.0.nupkg`; uma tag `v0.3.0` exige que a base esteja
configurada como `0.3.0`. Tags desconhecidas ou versões incompatíveis falham.

Pré-releases seguem a mesma regra: para `aspnetcore/v0.1.0-beta.1`, a versão do
projeto deve ser `0.1.0-beta.1`. O nome da release não é usado para escolher o pacote.
O fluxo gera os dois pacotes para validação, mas envia apenas o arquivo selecionado,
sem um curinga que possa publicar a base junto com o satélite.

## Validação dos pacotes

CI e CD executam os testes .NET e os testes do seletor de releases. Depois, geram
os dois `.nupkg` e executam `scripts/verify-packages.py`:

- Confere README, documentação XML, licença, framework e dependências dos pacotes.
- Confere que o SG está na pasta de analyzers, sem Roslyn como dependência de execução.
- Copia os consumidores de `tests/packaging` para um diretório temporário fora do repositório.
- Restaura em um cache novo e compila apenas com `PackageReference`, sem referências aos projetos locais.
- Executa estados de sucesso/falha da base e respostas HTTP/MVC do satélite, incluindo XML e problema genérico.
- Provoca um status inválido e exige o diagnóstico `FORHTTP004` do gerador empacotado.

Na publicação do satélite, uma segunda execução com `--published-base` permite
restaurar `FlowOnRails` exclusivamente do NuGet público. Assim, uma dependência que
só existe no código local impede a publicação antes da autenticação. Se o satélite
passar a depender de uma nova versão da base, publique a base primeiro.

Para repetir a validação localmente, a partir da raiz:

```sh
dotnet restore FlowOnRails.slnx
dotnet build FlowOnRails.slnx -c Release --no-restore
dotnet test FlowOnRails.slnx -c Release --no-build --no-restore
python3 -m unittest discover -s tests/packaging -p 'test_*.py'
dotnet pack src/FlowOnRails/FlowOnRails.csproj -c Release --no-build --no-restore --output ./artifacts/packages
dotnet pack src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj -c Release --no-build --no-restore --output ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages --published-base
bash scripts/select-release-package.sh aspnetcore/v0.1.0
```

Use um diretório de saída sem versões antigas dos mesmos pacotes. A validação
requer .NET 10, Python 3 e permissão para abrir uma porta temporária de loopback.
Somente a variante `--published-base` acessa o NuGet público para restaurar o
consumidor; a variante local usa exclusivamente os pacotes gerados.

## Configuração do acesso

O workflow usa [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
com `NuGet/login@v1`, obtendo uma credencial temporária após as validações.

A política na conta NuGet deve corresponder a:

| Campo | Valor |
|---|---|
| Conta proprietária | `alguem5381` |
| GitHub Repository Owner | `Alguem5381` |
| Repository | `FlowOnRails` |
| Workflow File | `cd.yml` |
| Environment | Em branco, pois o job não declara um environment |
| Pacotes permitidos | `FlowOnRails` e `FlowOnRails.AspNetCore`, por exemplo com o padrão `FlowOnRails*` |
| Operações | Novas versões e, para a primeira publicação do satélite, criação de pacote |

O job mantém `id-token: write`. Uma política limitada ao pacote base ou apenas a
novas versões não autoriza a primeira publicação do satélite. Confira o escopo na
conta antes da release; esta configuração de empacotamento não altera a política
nem confirma seu estado atual. O login no navegador é usado para administrar essa
política, enquanto o workflow autentica cada publicação por conta própria.

## Procedimento de publicação

1. Atualizar a versão do pacote que será publicado e revisar seu guia e changelog.
2. Enviar a branch e obter CI aprovado, depois integrar os commits revisados à `main`.
3. Conferir o escopo da política NuGet e a disponibilidade da versão desejada.
4. Criar uma release apontando para o commit revisado, usando a tag do pacote escolhido.
5. Publicar a release para disparar o CD e conferir o resultado no GitHub e no NuGet.

Para o primeiro satélite, a tag preparada é `aspnetcore/v0.1.0`. A versão `0.2.0`
da base não precisa ser republicada. O envio de uma versão que já existe no NuGet
falha: o workflow não ignora esse conflito. A configuração local e os testes de
consumo não substituem a execução real de autenticação e envio no GitHub Actions.

Referência do evento: [GitHub Actions — release](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release).
