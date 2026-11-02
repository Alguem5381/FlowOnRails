# Revisão para a versão 0.2.0

Registro de 22/09/2026. Avaliação do estado local, incluindo alterações ainda não commitadas. Este documento registra os problemas encontrados e o andamento das correções. Os itens marcados abaixo foram concluídos; os demais permanecem pendentes.

O núcleo tem uma estrutura compreensível, boa separação entre valor e resultado semântico e testes úteis de composição. Os principais problemas estão na composição das APIs assíncronas, na representação do sucesso neutro e na validação da experiência pública de uso.

## Andamento em 23/09/2026

Concluída a primeira etapa do núcleo: neutro observado ou copiado por `with` é normalizado nas factories de `Rail` e `RailFor<T>`, preservando igualdade, hash e composição. O singleton neutro fica em um holder inicializado somente ao observá-lo. Records de domínio, inclusive agregados vazios, continuam sendo sucessos ricos opacos.

O exemplo `GetUser` do README foi corrigido e ganhou uma versão compilada em `tests/FlowOnRails.PublicApi.Tests`, sem acesso aos membros internos da biblioteca. Um teste compara o trecho documentado com o exemplo compilado. Esse projeto também verifica o neutro por todas as observações públicas, recuperação, conversão, factories assíncronas e combinações das leis de `Bind` e `Map`.

Validação da primeira etapa: **127 testes aprovados em Release** (104 existentes e 23 novos). Antes da correção, 18 dos novos testes falharam, reproduzindo as regressões. O pacote 0.2.0 foi gerado localmente e um consumidor separado, usando somente esse pacote, executou o exemplo e verificou a semântica do neutro.

Após a validação do núcleo, também foram corrigidos os dois defeitos assíncronos reproduzidos: as extensões de `ValueTask` preservam essa família, e as duas pontes de `Task<RailFor<T>>` para callbacks de `ValueTask` passaram a se chamar `BindValueTaskAsync`. O README documenta a migração.

Validação da etapa assíncrona: **151 testes aprovados em Release** (104 existentes e 47 no projeto consumidor). Os novos testes incluem lambdas async sem casts, espera pela origem e pelo callback, falhas, cancelamento e fontes ValueTask de consumo único. O pacote final também foi executado em um consumidor independente, incluindo lambdas Task, a ponte explícita e cadeias ValueTask. Isso não representa cobertura completa de todas as sobrecargas. A decisão sobre `Combine` e as novas operações continuam pendentes.

Concluída a etapa de contratos por estado: **760 cenários aprovados em Release** (104 no projeto original e 656 no projeto consumidor). Foram adicionados 609 cenários, sem alterações no código da biblioteca:

- `OperationStateTests.cs`: 576 combinações de estado, operação, formato com/sem valor e execução síncrona, Task, ValueTask ou ponte explícita. Verifica callbacks e predicados separadamente, argumentos recebidos, preservação/substituição do resultado e identidade dos erros, inclusive agregados opacos. A rejeição de `default` é verificada na própria chamada da operação, antes de observar o resultado.
- `LinqStateTests.cs`: 33 cenários para Select, SelectMany, projeções de sequências, filtros, Partition, Combine e interrupção de cadeias. Verifica execução adiada e repetição por enumeração, preservação dos objetos de erro e descarte do enumerador quando Combine encontra a primeira falha.

A matriz usa casos parametrizados: a contagem representa combinações executadas, não centenas de métodos independentes nem uma prova de correção completa. A composição de sucessos ricos em Combine continua pendente; os novos testes não estabelecem uma política para essa composição. Espera de operações pendentes, exceções e cancelamento permanecem cobertos pelos testes assíncronos da etapa anterior.

## Avaliação antes da versão 1.0

Decisão de 02/11/2026: continuar desenvolvendo com `Success` e, quando a biblioteca estiver em um estado satisfatório para apresentar, consultar uma comunidade ou fórum sobre sua utilidade. A permanência dessa abstração ainda está em avaliação. A motivação e os critérios estão em [Sucessos semânticos e novas operações](analise-sucessos-e-operacoes.md#decisão-em-02112026-manter-success-em-avaliação-até-antes-da-10).

- [ ] Reunir exemplos e comparar `Success` com alternativas modeladas em `TValue`.
- [ ] Consultar uma comunidade ou fórum e registrar o feedback recebido.
- [ ] Decidir se `Success` será mantido, reformulado ou removido antes de estabilizar a API na 1.0.

## Correções prioritárias

### 1. Preservar a semântica das cadeias com ValueTask

- [x] Corrigir os retornos das extensões de `ValueTask`: agora todas preservam `ValueTask`.
- [x] Definir uma política coerente para callbacks de `Task` e `ValueTask` ao longo de toda a cadeia.
- [x] Impedir que um `MapAsync` subsequente encapsule um awaitable por seleção acidental da sobrecarga síncrona.
- [x] Cobrir o caso em que `TapAsync` seleciona `Action<T>` e descarta o awaitable devolvido pela expressão.

Reprodução original, agora corrigida, usando `FlowOnRails.Extensions.Tasks`:

```csharp
var result = await ValueTask.FromResult(Rail.Ok(1))
    .MapAsync(value => value + 1)
    .MapAsync(value => ValueTask.FromResult(value + 1));

// Tipo após o await: RailFor<int>. Antes, era RailFor<ValueTask<int>>.
```

Também foi reproduzida uma cadeia de dois `TapAsync` que terminou sem aguardar o segundo callback de `ValueTask`. Nesse caso o compilador emitiu `CS4014`; o aviso não deve ser ignorado. A transição implícita para a família de extensões de `Task` era a origem da armadilha e foi removida. Misturas explícitas de famílias ainda exigem adaptação dos callbacks, conforme o README.

**Critério de conclusão:** cadeias representativas aguardam os callbacks, propagam exceções e cancelamento e produzem o tipo esperado. Incluir tarefas ainda incompletas, não apenas `FromResult` e `CompletedTask`.

### 2. Remover a ambiguidade de BindAsync

- [x] Revisar as sobrecargas que diferem apenas entre `Func<T, Task<...>>` e `Func<T, ValueTask<...>>`.
- [x] Escolher uma solução para a API pública antes de multiplicar essas sobrecargas em novas operações.

Este uso natural produzia `CS0121`; agora compila e faz parte dos testes públicos:

```csharp
await Rail.OkAsync(1).BindAsync(async value =>
{
    await Task.Yield();
    return Rail.Ok(value + 1);
});
```

**Critério de conclusão:** exemplos com lambdas `async`, method groups e delegates tipados compilam sem casts artificiais. A solução adotada foi manter `BindAsync` para a mesma família e nomear as pontes existentes como `BindValueTaskAsync`.

### 3. Normalizar o sucesso neutro ao reintroduzi-lo na API

- [x] Tratar o sucesso neutro materializado e a representação interna `null` como o mesmo significado.
- [x] Centralizar essa normalização nas entradas apropriadas, abrangendo `Rail` e `RailFor<T>`.
- [x] Preservar igualdade, hash e composição após observar e reconstruir um resultado.

Reprodução:

```csharp
var neutral = (Success)Rail.Ok().Outcome;

bool same = Rail.Ok() == Rail.Succeed(neutral);       // Corrigido: true.
bool sameValue = Rail.Ok(1) == Rail.Ok(1, neutral);    // Corrigido: true.
```

Antes da correção, `Rail.Succeed(rich).Bind(() => Rail.Succeed(neutral))` perdia `rich`. As factories agora normalizam o neutro observado e suas cópias, conservando a representação interna esperada pela composição.

**Critério de conclusão:** os dois testes acima retornam `true`; combinar com neutro reconstruído preserva o sucesso anterior. Cobrir `Outcome`, `TryUnwrap`, factories, conversões e futuras operações como `MapSuccess`. Considerar também cópias do record neutro via `with`, para não depender exclusivamente da identidade de uma referência.

### 4. Compilar os exemplos de uso público

- [x] Corrigir os retornos de erro de `GetUser` no README.
- [x] Manter o exemplo `GetUser` compilável e sincronizado com o README em um projeto consumidor, sem `InternalsVisibleTo`.

Na revisão original, o exemplo retornava `RailFor<User>`, mas usava `return Rail.Fail(error)`, cujo tipo é `Rail`. A reprodução produziu `CS0029`. Os dois retornos agora usam `Rail.Fail<User>(error)`.

**Critério de conclusão:** os exemplos compõem e compilam contra a API pública. O projeto de testes acessa membros internos e, sozinho, não verifica a experiência de um consumidor externo.

## Testes e validação

Na revisão inicial, os **104 testes passaram**. O Coverlet mediu **48% de cobertura de linhas** e aproximadamente **51% de ramificações**. Esses números são o registro daquela execução, não uma medição automaticamente atualizada.

| Arquivo de extensões assíncronas | Cobertura de linhas medida |
|---|---:|
| `TaskExtensions.cs` | 23,8% |
| `TaskOfTExtensions.cs` | 6,3% |
| `ValueTaskExtensions.cs` | 0% |
| `ValueTaskOfTExtensions.cs` | 7,1% |

- [x] Testar interrupção por falha, preservação de erro e execução única dos callbacks aplicáveis por chamada (ou por elemento, a cada enumeração).
- [x] Usar `TaskCompletionSource` para verificar espera real, sem depender de atrasos arbitrários.
- [x] Testar tarefas que falham e cancelamento, tanto na origem quanto nos callbacks.
- [x] Testar `ValueTask` apoiado em uma fonte de consumo único; não consumi-lo duas vezes.
- [x] Testar neutro, sucesso rico, falha e estado `default` nas famílias de operações existentes, incluindo as pontes assíncronas e LINQ; a composição de sucessos em `Combine` continua como decisão separada.
- [x] Ampliar os testes das leis de `Bind` e `Map`, cobrindo combinações de estados e tipos.
- [x] Verificar tipos inferidos e compilação das cadeias públicas, além do comportamento em execução.

Cobertura de linhas é um indicador de lacunas, não uma prova de correção. A prioridade são os contratos e os cenários acima.

## Decisões antes da publicação

- [x] Documentar a independência dos records de domínio e a regra padrão de preservação e substituição de sucessos. As novas operações ainda precisam ser implementadas.
- [ ] Especificar o comportamento de `Combine` com sucessos ricos. Hoje eles são descartados e o contrato deixa essa parte indefinida.
- [ ] Definir a semântica e o conjunto mínimo das novas operações antes de criar versões assíncronas.
- [ ] Documentar alterações incompatíveis com a versão anterior, incluindo remoções da API pública.
- [x] Executar build e testes em Release e validar um consumidor do pacote gerado.

As proteções atuais contra `default(Rail)` e `default(RailFor<T>)` já fazem parte das alterações locais. A revisão do último commit encontrou uma falha sem erro nesse estado; preservar os testes que agora impedem essa regressão.

Os arquivos principais a revisar são `src/FlowOnRails/Extensions/Tasks/`, `src/FlowOnRails/Base/`, `src/FlowOnRails/SuccessComposition.cs`, `src/FlowOnRails/Extensions/Linq/LinqExtensions.cs` e `README.md`.

A matriz de operações esclarecida pelo autor, os limites do modelo e as candidatas adicionais estão em `docs/analise-sucessos-e-operacoes.md`. Ficou confirmado que `Bind` e `BindKeepValue` mantêm o sucesso rico anterior quando a próxima operação retorna neutro.
