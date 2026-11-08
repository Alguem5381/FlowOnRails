# Como funcionam as convenções de endpoints

Este guia explica a integração do projeto `FlowOnRails.AspNetCore` com o ASP.NET
Core. Ele complementa o [contrato de visibilidade](design/aspnetcore-visibility.md).

## Primeiro, registrar uma rota

```csharp
var route = app.MapGet("/ping", () => "pong");
```

O retorno de `MapGet` é um `RouteHandlerBuilder`. Ele permite acrescentar
configurações à rota. Esse objeto implementa `IEndpointConventionBuilder`.

A interface define como registrar ações que personalizam os endpoints durante
sua construção. Uma convenção é uma função `Action<EndpointBuilder>`; neste
contexto, não significa uma regra inferida pelo nome de uma classe.

```csharp
IEndpointConventionBuilder conventions = route;

conventions.Add(endpoint =>
{
    endpoint.DisplayName = "Ping da aplicação";
});
```

`Add` registra a ação. Quando o ASP.NET constrói o endpoint, entrega um
`EndpointBuilder` mutável à ação. Neste exemplo, a ação altera seu nome de
exibição. O código não executa para cada requisição e não altera o texto `pong`
retornado pelo handler.

A interface também oferece `Finally`, para ações executadas depois das convenções
registradas por `Add`. A biblioteca usa `Add`, pois apenas acrescenta seus metadados.

## Os builders têm responsabilidades diferentes

| Tipo | Responsabilidade |
|---|---|
| `IEndpointRouteBuilder` | Permitir registrar rotas, por exemplo com `MapGet` e `MapGroup` |
| `IEndpointConventionBuilder` | Permitir registrar personalizações dos endpoints associados |
| `EndpointBuilder` | Representar o endpoint mutável durante a construção |
| `Endpoint` | Representar o endpoint construído, com os metadados consultados na requisição |

O objeto retornado por `MapGroup` é um `RouteGroupBuilder`. Ele implementa as duas
interfaces: permite registrar rotas dentro do grupo e configurar os endpoints
desse grupo. Por isso uma extensão sobre `IEndpointConventionBuilder` pode funcionar
tanto numa rota individual quanto num grupo.

## O que WithErrorVisibility faz

```csharp
var users = app.MapGroup("/users")
    .WithErrorVisibility(scope =>
    {
        scope.ForTree<PublicError>().TranslateEachType();
    });
```

A extensão faz duas coisas em momentos diferentes:

1. Ao chamar `WithErrorVisibility`, executa sua configuração, valida as regras e
   prepara um índice imutável.
2. Registra uma ação com `Add`. Na construção de cada endpoint afetado, essa ação
   acrescenta o índice aos metadados, encapsulado em `EndpointScopeMetadata`.

A classe de metadados é interna. Ela não faz parte do contrato público de erros,
não é serializada e não guarda um HttpContext. Ela contém apenas a identificação
do builder para detectar configurações duplicadas e o índice das regras.

O parâmetro genérico da extensão preserva o tipo de builder recebido. Uma chamada
em `RouteGroupBuilder` continua retornando `RouteGroupBuilder`, permitindo encadear
outras configurações do grupo.

## Depois, quando chega uma requisição

O roteamento do ASP.NET escolhe o endpoint da requisição. A policy pode consultar
esse endpoint por `httpContext.GetEndpoint()` e ler os metadados acrescentados
anteriormente. As convenções não precisam ser executadas novamente.

```csharp
var policy = httpContext.RequestServices
    .GetRequiredService<ErrorPolicy>();

var decision = policy.ResolveForRequest(error, httpContext);
```

`ErrorPolicy` usa as regras do endpoint, dos grupos, do controller quando houver,
e do escopo global. `ExposureDecision` devolve o comportamento e os contratos
permitidos para tradução. Nenhuma dessas operações escreve uma resposta HTTP.
A futura integração de resultados poderá consumir essa decisão para gerar
ProblemDetails.

## Onde começar a leitura

- `Integration/ErrorPolicyEndpointExtensions.cs`: configuração e chamada a `Add`.
- `Integration/EndpointScopeMetadata.cs`: os dados associados ao endpoint.
- `Resolution/ErrorPolicy.cs`: leitura dos metadados e resolução das regras.

Referências oficiais:

- [IEndpointConventionBuilder](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.iendpointconventionbuilder?view=aspnetcore-10.0)
- [RouteGroupBuilder](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.routing.routegroupbuilder?view=aspnetcore-10.0)
