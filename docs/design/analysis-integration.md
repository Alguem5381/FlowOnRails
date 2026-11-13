# Integração da análise nos projetos satélites

Este é o ponto de partida para a próxima versão de `FlowOnRails.AspNetCore`.
O pacote HTTP ainda não foi adaptado nesta etapa. A auditoria e o acompanhamento das
correções estão em `docs/reviews`; não tratar os resultados como prova de exaustividade.

## Fronteiras dos componentes

| Componente | Responsabilidade | Dependência de execução da aplicação |
| --- | --- | --- |
| FlowOnRails | Rail, RailFor, Error, atributos e ferramentas de análise opcionais | Sim |
| FlowOnRails.Analysis | API Roslyn, motor e leitor dos resumos | Não; ferramenta de compilação |
| FlowOnRails.AspNetCore | Tradução HTTP e seu gerador | Sim para o adaptador; gerador roda na compilação |

O pacote base inclui as DLLs de `FlowOnRails.Analyzers` e `FlowOnRails.CodeFixes`
como ferramentas de compilação, desativadas por padrão. O motor
mora em `src/FlowOnRails.Analysis/Internal`. Essas classes são internas, mesmo
quando um projeto amigo consegue acessá-las. O contrato público é a fachada
`ErrorFlowAnalysis`, `ErrorFlowSummary` e `ErrorFlowCase`, junto aos modelos
`ErrorFlowLimitation`, `ErrorFlowLimitationCode` e `ErrorFlowSummaryOrigin`.

## Como um gerador consulta os erros

```csharp
if (!ErrorFlowAnalysis.TryCreate(compilation, out var analysis))
    return;

if (!analysis.TryAnalyzeMethod(handlerMethod, out var summary, cancellationToken))
    return;

foreach (var errorType in summary.KnownErrorTypes)
    CollectVisibleMapping(errorType);

if (summary.HasUnknownErrors)
    KeepGenericErrorMapping();
```

O exemplo é conceitual: `handlerMethod` precisa ser um `IMethodSymbol` cujo retorno
seja Rail/RailFor/Error, opcionalmente Task/ValueTask. A API ainda não consulta
retornos arbitrários como `IResult`, nem especializa uma chamada com argumentos
concretos. Uma função que envolve o handler deve ser identificada pelo gerador
HTTP; o motor não infere as regras de endpoints do ASP.NET.

Criar o contexto por compilação, reaproveitá-lo durante aquela execução e passá-lo
para as consultas; consultas concorrentes são suportadas. `CanAnalyze` verifica
o tipo de retorno e a origem do símbolo sem executar a análise. Os métodos Try
retornam false quando o contexto/método não é suportado; consultas abertas ainda
retornam true com um resumo. Cancelamento continua lançando OperationCanceledException.
Não guardar símbolos ou resumos num singleton entre compilações.
A API não depende de Workspaces e pode ser usada num gerador incremental. Ela não
exige `FlowOnRailsEnableFlowAnalysis` para a consulta explícita; a propriedade
controla a execução automática dos diagnósticos e da exportação.

## Fonte, referência e ordem dos geradores

Para código da compilação atual, a API interpreta o corpo disponível. Para outro
assembly, lê `FlowAnalysisSummaryAttribute`. Para interfaces e despacho virtual
não fechado, combina `ResultsWith` com uma parcela aberta. Métodos indisponíveis,
metadados ausentes/incompatíveis e tipos inacessíveis não viram resultados vazios.

Os geradores veem a compilação de entrada, sem a saída de outros geradores na
mesma rodada. Portanto, o gerador HTTP não deve esperar que o manifesto do gerador
FlowOnRails já esteja disponível para métodos locais. Deve consultar a API. Os
manifestos de assemblies já compilados podem ser usados normalmente. Esta é uma
restrição do [modelo de source generators do Roslyn](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md).

Também não se deve interpretar o texto de FORFLOW001/FORFLOW002, coletar strings
de nomes de namespaces, nem implementar uma segunda versão do codec binário.
Os diagnósticos são apresentação; a API retorna símbolos e estados estruturados.

## Interpretação do resultado

- `ErrorCases` representa tipos exatos ou famílias abertas com exclusões.
- `KnownErrorTypes` omite o marcador Error base, mas ainda pode conter o limite de
  uma família. Não significa que seus descendentes foram enumerados.
- `HasUnknownErrors` exige preservar a alternativa desconhecida.
- `HasUnresolvedConditions` indica que o conjunto pode incluir caminhos inviáveis;
  isso não implica, por si só, tipos desconhecidos.
- `MayBeInvalid` distingue Rail não inicializado de um erro de domínio.
- `MaySucceed` e `MayProduceResult` descrevem o resultado após desembrulhar async.
- `LimitationDetails` fornece `Code` estável e `Message` para apresentação.
  Compare `ErrorFlowLimitationCode`, por exemplo MetadataMissing ou OpenDispatch.
- `Limitations` preserva a lista de mensagens para apresentação e compatibilidade.
- `Origin` descreve a consulta principal: SourceBody, AssemblyMetadata ou OpenContract.
  Um corpo local pode incorporar resumos externos e continuar sendo SourceBody.

Mesmo um resultado fechado no modelo suportado não deve remover a resposta HTTP
genérica nem suprimir avisos do compilador. A análise é aproximada e tem limites.
Tipos encontrados pelo motor também não estabelecem automaticamente sua
visibilidade HTTP, o status, a exposição da mensagem ou a autorização de uma camada.
Essas decisões continuam pertencendo ao contrato e à política do satélite.

## Empacotamento do gerador HTTP futuro

No projeto do gerador, referenciar `FlowOnRails.Analysis` com `PrivateAssets="all"`.
No NuGet do satélite, colocar a DLL da análise junto à DLL do gerador em
`analyzers/dotnet/cs`. Não tornar `Microsoft.CodeAnalysis.*` ou a API de análise
uma dependência de execução da aplicação. Usar a mesma versão da API embutida
pelas ferramentas FlowOnRails para evitar duas versões no carregador de analyzers.

O satélite não precisa distribuir as correções de editor do pacote de análise.
Os projetos produtores precisam ativar `FlowOnRailsEnableFlowAnalysis` para exportar seus
resumos. Instalar o analisador só no consumidor não reconstitui corpos dos DLLs.

## Metadados e evolução

O atributo público tem versão, identificador documental do método original e
payload opaco. O gerador exporta versão 3; o leitor aceita versões 1, 2 e 3.
A versão 2 inclui alternativas condicionadas e saídas ref/out; a versão 3 acrescenta
os códigos das limitações. A fachada retorna o resumo agregado para entradas que
não são conhecidas. Limitações armazenadas só como texto nos formatos antigos
recebem Unclassified; códigos futuros desconhecidos também preservam sua mensagem
como Unclassified. Nenhum código é deduzido pela redação da mensagem.
A origem da consulta é resolvida no consumidor e não copiada do produtor.
A versão do payload é independente da versão NuGet.
Não persistir ou analisar esse formato fora da biblioteca de análise.

## Escopo da próxima tarefa ASP.NET

1. Identificar onde o gerador HTTP encontra o símbolo do handler Rail.
2. Consultar a API e combinar os tipos com a visibilidade/mapeamentos do contrato.
3. Manter tratamento genérico para erros desconhecidos e não inicialização.
4. Testar implementações concretas, interfaces, Task/ValueTask e assemblies separados.
5. Empacotar a dependência do gerador e testar um consumidor instalado por NuGet.

A auditoria independente e as correções estão documentadas em `docs/reviews`.
Esta etapa preserva a análise aproximada e o fallback de erros desconhecidos.
