# Auditoria independente de FlowOnRails e da API de análise

Estado histórico anterior às correções. O acompanhamento está em
[Correções da auditoria](2026-11-12-analysis-api-fixes.md); o experimento atual
valida a semântica corrigida.

Data: 2026-11-12. Escopo: estado local do repositório, incluindo os projetos de
análise ainda não commitados. Não houve alteração da implementação de produção,
publicação, release ou delegação adicional. O experimento está em
`tests/FlowOnRails.Analysis.AuditExample/`, fora da solução.

**Veredicto: a API tem uma fronteira pública útil e o uso por um analisador
satélite foi demonstrado; a implementação ainda não está pronta para sustentar
essa integração sem corrigir os achados abaixo.** Há dois fechamentos incorretos
que omitem falhas reais, uma falha no tratamento de metadados que interrompe o
analisador e uma ação de editor que transforma código válido em código inválido.
O rótulo de preview e a preservação de fallback são necessários, mas não tornam
essas omissões corretas.

## Achados confirmados

### 1. [P1] Efeitos no receptor de uma atribuição são ignorados

**Local:** `src/FlowOnRails.Analysis/Internal/MethodFlowAnalysis.Operations.cs:92`
(avalia somente `assignment.Value`, depois escreve ou abandona o destino).
**Reprodução:** `tests/FlowOnRails.Analysis.AuditExample/AuditProbes.cs:32`.

```csharp
RailFor<int> rail = new Before();
Change(ref rail).Value = 1; // Change escreve new After() em rail.
return rail;
```

O programa compilado e executado retorna `After`. A consulta pública retorna
somente `Before`, `HasUnknownErrors=false`; o resumo exportado e relido em outro
assembly também retorna somente `Before`. Separar a chamada em
`var box = Change(ref rail); box.Value = 1;` faz a análise reconhecer `After`.
Portanto, não é uma limitação geral declarada de `ref` ou de armazenamento no
heap: a avaliação do receptor desaparece conforme a forma sintática.

**Consequência:** o analisador de mapeamentos pode deixar de avisar sobre uma
falha efetivamente retornada. A exportação perpetua a omissão em consumidores.
Não basta marcar a propriedade como armazenamento desconhecido: os efeitos da
expressão que calcula seu receptor precisam ser executados, na ordem correta,
ou tornar o estado afetado conservador. Revisar também operações de escrita
relacionadas; não foram afirmados resultados de variantes não executadas.

### 2. [P1] Um teste com parâmetro de tipo é tratado como incompatibilidade comprovada

**Locais:** `src/FlowOnRails.Analysis/Internal/ErrorSet.cs:74` e `:115`;
`src/FlowOnRails.Analysis/Internal/MethodFlowAnalysis.Conditions.cs:195`.
**Reprodução:** `tests/FlowOnRails.Analysis.AuditExample/AuditProbes.cs:46`.

```csharp
public static RailFor<int> GenericPattern<TError>() where TError : Error
{
    Error error = new Before();
    return error is TError ? new After() : new Before();
}
```

A consulta da definição genérica informa somente `Before`, com conjunto fechado.
Uma chamada local a `GenericPattern<Before>()` é corretamente especializada como
`After`, e a execução real confirma `After`. Após exportar a definição e consultar
o método construído `GenericPattern<Before>()` por referência de assembly, o
resultado volta a ser apenas `Before`, fechado.

`IsSubtype(Before, TError)` devolve falso por falta de igualdade na cadeia de
bases; `Restrict` interpreta esse falso como impossibilidade. O parâmetro de tipo
ainda pode ser substituído por `Before`. A incerteza precisa preservar os dois
caminhos compatíveis até a especialização. O gerador não pode recuperar depois
uma alternativa já eliminada. A partição de parâmetros de valor em
`SummaryBuilder.cs:25` não corrige esse problema com parâmetros de tipo.

**Consequência:** mudar um handler da compilação local para outro projeto pode
alterar quais erros conhecidos são informados, omitindo uma falha válida.

### 3. [P2] Resumo de Task nulo viola a forma de retorno e provoca AD0001 no consumidor

**Locais:** `src/FlowOnRails.Analysis/Internal/MethodFlowAnalysis.cs:101`;
`src/FlowOnRails.Analysis/Internal/MetadataSummaries.cs:65`;
`src/FlowOnRails.Analysis/Internal/SummaryCodec.cs:141`.
**Reprodução:** `tests/FlowOnRails.Analysis.AuditExample/AuditProbes.cs:45` e `:101`.

```csharp
public static Task<RailFor<int>> NullTask() => null!;
```

A consulta local produz `MayProduceResult=true`, `MayReturnNull=true`, nenhum erro
e conjunto fechado. Isso não representa um resultado após aguardar a Task: a Task
é nula. `MayReturnNull` está documentado para métodos que retornam Error.
A validação da análise raiz aceita qualquer valor de espécie Rail, Error ou
Bottom após desembrulhar, sem verificar se corresponde à forma de retorno.

O gerador exporta esse resumo. O leitor detecta a incompatibilidade e lança
`InvalidDataException`; o filtro do `catch` inclui `IOException`, mas
`InvalidDataException` não deriva dela. A exceção escapa da API pública. O
analisador satélite executado pelo host Roslyn produz **AD0001**, em vez de
continuar com um resumo aberto. A referência foi produzida pelo gerador normal,
sem fabricar nem interpretar payload no experimento.

**Consequência:** um retorno inválido, porém compilável, num produtor pode derrubar
a consulta do consumidor. Corrigir tanto a forma do resumo na origem quanto a
contenção de erros de leitura; capturar a exceção sozinho evita o crash, mas deixa
a incoerência local. A documentação promete degradação para resultado aberto
quando metadados são inválidos, e este caso não a cumpre.

### 4. [P2] Completar switch não reconhece cobertura por interface

**Locais:** `src/FlowOnRails.Analysis/Internal/ErrorSwitchAnalysis.cs:68` e
`src/FlowOnRails.Analysis/Internal/ErrorSet.cs:115`.
**Reprodução:** `tests/FlowOnRails.Analysis.AuditExample/AuditProbes.cs:137`.

Um erro `Missing : Error, IRetryable` já é coberto por:

```csharp
error switch { IRetryable => "retry", _ => "fallback" }
```

O diagnóstico FORFLOW003 oferece a ação, que acrescenta `Missing => throw new
NotImplementedException()` depois de `IRetryable`. O resultado gera **CS8510**,
braço inalcançável. A reprodução aplica o `CodeAction` real em `AdhocWorkspace`
e recompila o documento atualizado. O fallback permanece, mas o código deixa
de compilar.

`Covers` usa um predicado que só percorre classes-base. A cobertura de padrões
deve considerar interfaces e outras relações válidas pertinentes ao padrão;
quando não puder comprovar que a inserção será válida, a ação deve se abster.

## Experimento prático da API

O contrato local `[AuditController]` seleciona classes; `[MapsError(typeof(T))]`
declara cobertura de T e seus descendentes. O analisador publica AUDIT001 para
tipo conhecido sem mapeamento e AUDIT002 para conservar fallback quando há
alternativas desconhecidas. Um limite conhecido de família não é tratado como
enumeração exaustiva. A ausência de AUDIT002 tampouco autoriza remover tratamento
geral de erros.

O assembly `Analyzer/AuditMappingAnalyzer.csproj` é `netstandard2.0`, não é amigo
da implementação e não referencia o projeto dos analisadores FlowOnRails. Usa
exclusivamente a fachada pública e símbolos Roslyn. O executor referencia o
gerador e o code fix públicos apenas para montar os cenários e reproduzir a ação.
Não há leitura de Infos, nomes extraídos de mensagens ou parsing de payloads.

| Cenário | Resultado do exemplo |
| --- | --- |
| Erro conhecido com mapeamento | Sem AUDIT001 |
| Erro conhecido sem mapeamento | AUDIT001 |
| Erro recuperado | Sem AUDIT001 |
| Error como valor de sucesso | Sem AUDIT001 |
| Interface com ResultsWith de erro mapeado | Sem AUDIT001; AUDIT002 preservado |
| Task e ValueTask locais | Tipo conhecido reconhecido |
| Rail, Task e ValueTask de outro assembly com resumos | Tipo remoto reconhecido |
| Erro remoto mapeado | Sem AUDIT001 |
| Retorno arbitrário como string | Fora do escopo, sem consulta inválida |

São actions que **retornam Rail**, inclusive wrappers assíncronos e actions que
encaminham a chamada de um handler. Não foi implementada análise de actions
`IActionResult`/`IResult` que chamam e transformam um handler. A API não fornece
consulta contextual de invocação; consultar só o símbolo do handler perde os
argumentos concretos e a recuperação feita na action. Isso é uma limitação
deliberada, documentada, e precisa ficar explícito na futura integração HTTP.

O teste roda com `Compilation.WithAnalyzers`, ações de símbolos e concorrência
habilitada. Um consumidor separado também carrega o satélite no compilador real.
O primeiro ensaio desse consumidor revelou a exigência de implantação: apenas
referenciar o projeto do satélite como analyzer causou AD0001 por ausência de
`FlowOnRails.Analysis.dll` no carregador. Registrar também essa DLL como asset de
analyzer resolveu; a aplicação não ganhou referência de execução à API ou ao
Roslyn. Isso confirma a orientação de empacotamento existente, não constitui um
bug adicional. Não foi produzido um pacote NuGet novo.

## Ergonomia e coerência do contrato público

O desenho tem bons fundamentos: contexto por compilação, símbolos comparáveis
com `SymbolEqualityComparer`, resultados imutáveis, cancelamento, separação entre
erros conhecidos, abertura, condições, sucesso e Rail inválido. `ErrorCases`
mantém exatidão e exclusões que uma lista de nomes perderia. Os exemplos normais
mostram que não é necessário recorrer aos internals para criar um satélite útil.

Oportunidades, distintas dos bugs:

1. **Oferecer teste de capacidade.** Sem `CanAnalyze`/`TryAnalyze` e eventualmente
   `TryCreate`, o satélite replica os nomes dos tipos da biblioteca e a regra de
   desembrulhar Task/ValueTask (`ControllerMappingAnalyzer.cs:35` e `:58`) ou usa
   exceções para fluxo normal. Uma descrição pública da forma de retorno evitaria
   divergência com a evolução dos wrappers suportados.
2. **Documentar concorrência explicitamente.** O uso compartilhado por compilação
   funcionou no host concorrente; caches internos são concorrentes, mas a
   documentação pública não afirma o contrato de segurança para esse uso.
3. **Separar necessidade de fallback de classificação de limites.** As strings
   de `Limitations` são corretamente declaradas instáveis. Se satélites precisarem
   escolher mensagens ou políticas conforme corpo ausente, orçamento, metadado
   inválido ou efeito não modelado, acrescentar códigos estáveis e dados de origem,
   mantendo a explicação humana. Não incentivar parsing das strings.
4. **Consulta contextual como extensão futura, não requisito do exemplo atual.**
   Uma API de invocação/expressão permitiria alcançar actions HTTP sem confundir
   erro do handler com erro que sobrevive à recuperação. A API atual atende ao
   escopo de métodos com retorno rastreado; não promete isso.
5. **Distinguir origem e extensão do resumo.** Informação estruturada de fonte,
   metadado ou contrato facilitaria explicar ao autor do satélite por que um caso
   ficou aberto, sem expor o interpretador. Isso é uma melhoria de diagnóstico,
   não uma condição para consumir os tipos já retornados.

Não foi feita medição de desempenho. O cache de grafos evita reconstrução, mas
não equivale a cache dos resultados de consultas. Evitar prometer custo constante
ou recomendar persistência de resumos entre compilações.

## Clareza, comentários e experiência do editor

Os comentários de `MethodFlowAnalysis.ControlFlow.cs` explicam a ordem de finally,
o tratamento conservador de exceções e a diferença entre exceção e Error.
`MethodFlowAnalysis.cs:76` descreve o ponto fixo recursivo; `SummaryBuilder.cs:14`
explica partições completas em vez de amostragem; `FlowState` explica por que
ligações são descartadas nas junções. Esses são comentários de estratégia úteis,
não mera tradução das instruções.

Faltam duas premissas explícitas que os bugs expuseram: avaliar os filhos com
efeitos de um destino de escrita na ordem da linguagem; e distinguir "não consigo
provar subtipo" de "tipos não podem coincidir" quando há parâmetros genéricos.
`ErrorSet.IsSubtype` também tem nome amplo para uma relação que ignora interfaces,
o que favoreceu sua reutilização indevida no editor. Explicitar o domínio da
relação ou separar as consultas semânticas ajudaria a manutenção.

As mensagens estão majoritariamente claras e separam erros conhecidos de outros
possíveis erros. A nota de Error como sucesso comunica corretamente a intenção.
As ações anunciam que preservam outros casos; a ação ResultsWith declara o
contrato sem prometer fechá-lo. Melhorias de apresentação, sem prioridade de bug:

- `ErrorFlowMessage.cs:64` usa "This method" também para FORFLOW002. Em chamadas
  especializadas por argumentos, preferir "This call may return..."; o conjunto
  daquela chamada pode ser menor que o do método.
- `ErrorFlowMessage.cs:70` e `:75` repetem a ausência de erros em duas frases.
  Uma frase curta bastaria nos casos fechados vazios.
- O texto vazio não diferencia sucesso de ausência de retorno normal. Considerar
  mensagem própria para esta última, aproveitando o estado já calculado.
- `ErrorFlowMessage.cs:89` diz "may also be returned" mesmo quando o único
  resultado é um Rail não inicializado. Usar "may be returned" nesse caso.
- "This called method storage is not supported for body analysis"
  (`MethodFlowAnalysis.Calls.cs:101`) é inglês pouco natural e expõe o mecanismo.
  Preferir "This call uses reference storage whose effects cannot be tracked."
- "Call depth limit or context budget requires an open result" (`:111`) pode
  virar "This call could not be fully analyzed; additional errors may be returned."
  Detalhes técnicos continuam úteis num campo de limitação para autores de tools,
  mas o primeiro texto ao usuário deve dizer o efeito observável.

A ação ResultsWith foi lida e a suíte do editor executada: resolve o contrato no
projeto de destino, verifica identidade/acessibilidade dos tipos, evita parâmetros
de tipo em atributos e não oferece fix-all que sobreponha edições concorrentes.
Não foi encontrado outro defeito confirmado nessa ação. Isso não certifica todos
os grafos de dependências ou configurações do workspace.

## Verificações e limites

Comandos a partir da raiz:

```sh
dotnet run --project tests/FlowOnRails.Analysis.AuditExample/FlowOnRails.Analysis.AuditExample.csproj --no-restore
dotnet build tests/FlowOnRails.Analysis.AuditExample/CompilerConsumer/CompilerConsumer.csproj --no-restore --verbosity minimal
dotnet test tests/FlowOnRails.Analyzers.Tests/FlowOnRails.Analyzers.Tests.csproj --no-restore --verbosity quiet
```

Resultados: suíte existente **315 aprovados, zero falhas**; consumidor do
compilador **build bem-sucedido, dois AUDIT001 intencionais, zero erros e nenhum
AD0001**. O executável verifica seis AUDIT001 e um AUDIT002 no cenário normal e
confirma isoladamente os quatro defeitos. A saída capturada é
`tests/FlowOnRails.Analysis.AuditExample/run-output.before-fixes.txt`. Sucesso do executável
significa que as reproduções do estado atual se mantêm; asserções que esperam os
bugs devem ser invertidas ao produzir correções.

As execuções iniciais usaram SDK 10.0.100/Roslyn 5.0.0. Ao retomar, o ambiente
iniciou SDK 10.0.115 e tentou gravar seu sentinel na pasta pessoal somente para
leitura. O build comum também terminou sem diagnóstico e com código 1. A execução
final passou usando pasta de CLI temporária, build serial e servidores desativados:

```sh
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet build tests/FlowOnRails.Analysis.AuditExample/FlowOnRails.Analysis.AuditExample.csproj --no-restore --disable-build-servers -m:1 --verbosity minimal
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet tests/FlowOnRails.Analysis.AuditExample/bin/Debug/net10.0/FlowOnRails.Analysis.AuditExample.dll > tests/FlowOnRails.Analysis.AuditExample/run-output.txt
```

Esse build final teve zero avisos e zero erros, e o executável terminou com código
0, incluindo a asserção de AD0001 no cenário defeituoso. Não foi necessária mudança
de produção para acomodar o ambiente. Os resultados dos 315 testes e do consumidor
de compilador são das execuções anteriores; não foram atribuídos à nova versão
do SDK sem repetição.

Foram examinados fluxo, chamadas, operadores Rail, refinamentos, estados,
metadados, exportação, ponto fixo e limites, além de diagnósticos, fixes e fachada.
A suíte cobre recursão mútua, efeitos ref/out, aliases, callbacks, exceções/finally,
metadados inválidos/ausentes, tipos inacessíveis e casos de editor. Os testes
existentes passam apesar dos novos achados: sua aprovação não os refuta.

São limitações deliberadas: interfaces e despacho virtual aberto; corpo ou
metadado ausente; efeitos não modelados; orçamento finito; ausência de consulta
contextual pública. A aproximação deve ampliar os resultados nesses casos. Os
achados 1 e 2 fazem o contrário e por isso são bugs, não apenas precisão limitada.

Não houve prova formal, fuzzing, benchmark, matriz completa de versões Roslyn/IDE,
teste do carregador por NuGet, teste em Visual Studio/Rider, nem execução de toda
a solução. Não foi implementado um adaptador ASP.NET ou avaliadas políticas HTTP.
O formato binário não foi exercitado com um corpus malicioso. Não há supressor de
exaustividade na superfície inspecionada; nenhuma recomendação desta revisão
autoriza remover fallback ou tratar um resumo fechado como prova do runtime.

**Próximo passo recomendado:** corrigir os quatro achados e converter suas
reproduções em regressões da semântica esperada antes de publicar ou integrar a
API ao gerador HTTP. A superfície pública pode ser mantida; as melhorias de
ergonomia podem ser incrementais.
