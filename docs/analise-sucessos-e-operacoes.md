# Sucessos semânticos e novas operações

Análise de 22/09/2026. **Proposta de API, sem implementação.** O autor confirmou a intenção de preservar ou descartar separadamente valor e sucesso nas quatro operações. Também confirmou que `Bind` e `BindKeepValue` devem conservar um sucesso rico anterior quando a próxima operação retorna neutro. As assinaturas e prioridades abaixo são recomendações de implementação, não decisões já incorporadas à biblioteca.

Atualização de 23/09/2026: `Success` e `Error` são propositalmente vazios para não impor significados nem coleções ao domínio consumidor. A prioridade confirmada pelo autor é consolidar o núcleo antes de retomar as extensões assíncronas.

O checklist dos defeitos confirmados está em `docs/revisao-0.2.0.md`.

## Decisão em 02/11/2026: manter Success em avaliação até antes da 1.0

O autor decidiu continuar desenvolvendo a biblioteca com `Success`. Quando ela atingir um estado satisfatório para apresentar, pretende consultar uma comunidade ou fórum para avaliar a utilidade dessa abstração. A decisão de manter, reformular ou remover sucessos semânticos deve ser retomada antes de estabilizar a API na versão 1.0.

A dúvida permanece aberta: `Success` pode incentivar o consumidor a colocar informações essenciais do domínio fora de `TValue`, enfraquecendo o contrato do tipo retornado. Até este registro, não foi identificado um caso concreto que justifique claramente esse custo. A coerência da composição e os testes demonstram o comportamento da implementação, mas não comprovam sua necessidade prática. Isso também não estabelece que a funcionalidade seja inútil.

Para essa avaliação futura:

- Apresentar a motivação, a regra de composição e exemplos completos de uso, distinguindo casos reais de exemplos hipotéticos.
- Comparar os exemplos com uma alternativa que represente as possibilidades no próprio `TValue`.
- Perguntar se a informação de sucesso pode ser ignorada sem deixar a resposta incompleta ou tornar incorreto o uso do valor.
- Avaliar se transportar essa informação pela cadeia simplifica o código o suficiente para justificar neutro, composição e operações adicionais.
- Registrar o feedback recebido e a decisão final antes da 1.0, incluindo eventuais mudanças de API e migração.

Esta decisão mantém a funcionalidade durante o desenvolvimento; não aprova automaticamente todas as operações propostas neste documento nem confirma sua permanência na API estável.

## A ideia tem utilidade?

Sim, se `Success` carregar uma informação sobre a operação que não esteja naturalmente representada pelo valor retornado. Por exemplo, duas chamadas podem devolver o mesmo usuário, mas uma tê-lo criado e outra ter reutilizado um cadastro existente. Isso pode ser útil na fronteira entre aplicação e apresentação, sem obrigar cada transformação do usuário a transportar manualmente essa informação.

O modelo separa três perguntas:

| Parte | Pergunta |
|---|---|
| Controle | A operação conseguiu continuar ou falhou? |
| `TValue` | Qual dado foi produzido? |
| `Success` | Que significado adicional teve a execução bem-sucedida? |

Sucessos ricos já têm precedente em [FluentResults](https://github.com/altmann/FluentResults/blob/master/README.md), que mantém objetos de sucesso e oferece `MapSuccesses`. A FlowOnRails mantém uma única referência de sucesso ativo e uma regra de composição. O objeto referenciado pode representar um resultado simples ou um agregado definido pela aplicação. A existência de um precedente apoia a utilidade da ideia, mas não valida automaticamente essa regra.

Eu manteria `Success` opcional. Um resultado neutro deve continuar sendo um caminho completo e simples de uso da biblioteca.

## O que pertence à biblioteca e o que pertence ao domínio

**A biblioteca define como manipula sucessos; o domínio define o significado e a estrutura deles.** `Success` e `Error` podem continuar como records base vazios. Não é necessário impor mensagens, eventos, listas ou uma classificação universal do resultado.

Com a política atual, `Bind` conserva o sucesso anterior quando o próximo é neutro e substitui o objeto inteiro quando o próximo é rico. `Map` preserva o sucesso enquanto transforma o valor. Essas regras são independentes da interpretação dos records pelo consumidor.

Por exemplo, `UserCreated` seguido de `EmailSent` termina como `EmailSent` usando `Bind`. `UserCreated` seguido de `Map(user => user.Id)` continua com `UserCreated`. Esses resultados não são erros de modelagem por si mesmos: a aplicação decide se a política corresponde à intenção do fluxo.

Uma única referência pode conter `SuccessList(List<Success> Successes) : Success`, caso o consumidor escolha esse modelo. O núcleo deve tratar esse record como um sucesso rico opaco. A política padrão o substitui pelo próximo sucesso rico, exatamente como faria com qualquer outro subtipo. Nem o nome nem a presença de uma lista permitem inferir se o domínio quer concatenar, substituir, eliminar duplicatas ou rejeitar determinada combinação.

Algumas consequências continuam relevantes:

- Se a distinção entre sucessos determina dados obrigatórios, permissões ou passos diferentes do algoritmo, considerar modelá-la no próprio `TValue`, por exemplo um resultado de domínio com variantes `Created` e `Existing`. `RailFor<User>` não expressa quais subclasses de `Success` podem aparecer.
- Se é necessário conservar vários significados, a aplicação pode representá-los em um agregado próprio, inclusive um subtipo de `Success`. A política padrão de `Bind` não acumula automaticamente esse agregado; a construção ou combinação deve ser explícita.
- Uma falha descarta o sucesso anterior no modelo atual. Isso não desfaz efeitos já executados nem informa tudo que ocorreu antes da falha. A biblioteca não fornece atomicidade ou compensação por composição de resultados.
- `readonly struct` não torna o objeto de domínio nem seus dados referenciados profundamente imutáveis. Preservar o valor significa preservar o valor ou referência armazenados, não uma fotografia imutável do estado.
- Muitos métodos `Keep...` usados em quase toda cadeia podem indicar que o sucesso pertence ao resultado final do caso de uso, em vez de precisar atravessar todos os passos internos.

## Coleções de rails e agregados do domínio

São duas capacidades distintas:

| Capacidade | Informação de que precisa |
|---|---|
| Projetar erros ou sucessos de `IEnumerable<RailFor<T>>` | Estado e outcome de cada rail |
| Combinar um `SuccessList` com outro sucesso | Regra de agregação escolhida pela aplicação |

Uma extensão LINQ para uma sequência recebida explicitamente não obriga todo domínio a usar coleções. `SelectErrors` já extrai os objetos de erro dos rails que falharam, sem conhecer seus subtipos. Uma projeção correspondente de sucessos pode seguir a mesma separação. `SelectOks`, que já existe, extrai os valores; não substitui uma projeção dos records de sucesso.

Com a API pública atual, a projeção pode ser escrita assim:

```csharp
var successes = results
    .Where(rail => rail.IsSuccess)
    .Select(rail => (Success)rail.Outcome);
```

Essa consulta inclui os neutros materializados. Antes de acrescentar uma extensão como `SelectSuccesses`, especificar se ela inclui neutros; uma operação que projete somente sucessos explícitos deve comunicar essa filtragem. Não achatar automaticamente agregados de domínio: um `SuccessList` continua sendo um elemento, assim como um eventual agregado de erros em `SelectErrors`.

A aplicação pode então usar LINQ, `Aggregate` ou um método próprio para construir seu resumo. Se optar por `SuccessList`, ela mesma define essa classe e a regra de construção. Isso não exige um `SuccessList` fornecido pelo núcleo.

Se aparecer a necessidade de combinar significados durante cada `Bind`, uma opção futura é uma função de composição fornecida explicitamente pela aplicação. Ela pode tratar pares de sucessos ricos enquanto a biblioteca mantém neutro como identidade e falhas como interrupção. Essa função poderia concatenar um agregado, selecionar um sucesso ou produzir outro record, sem obrigar `Success` a implementar uma coleção.

Essa é uma candidata a avaliar, não uma API aprovada. Evitar detecção por reflexão, configuração global ou concatenação implícita. Uma política personalizada também precisaria definir retorno nulo, propagação de exceções e suas propriedades de composição. Se não for associativa, reagrupamentos deixam de ter a garantia da política padrão; não prometer as mesmas leis sem essa condição.

## A composição atual é coerente

Se `I` representa o neutro:

```text
s ⊕ I = s
I ⊕ s = s
s1 ⊕ s2 = s2, quando s2 é rico
```

Essa regra tem identidade e é associativa: agrupar uma sequência de sucessos de maneiras diferentes preserva o último rico. Ela não é comutativa: trocar a ordem pode mudar o resultado. É análoga à seleção do último elemento presente documentada em [`Data.Monoid.Last`](https://hackage.haskell.org/package/base/docs/Data-Monoid.html#t:Last).

Isso sustenta as leis usuais de `Bind` para resultados válidos e callbacks puros. Um modelo abstrato finito com dois valores, dois erros e três estados de sucesso verificou 32.768 combinações de associatividade e as identidades. Essa exploração verifica a regra modelada; não substitui testes da implementação C#, nem prova propriedades de efeitos colaterais.

O defeito do neutro materializado era uma divergência da representação em relação a essa intenção. A normalização foi corrigida em 23/09/2026, incluindo cópias por `with`, com testes de regressão pela API pública. As novas operações sobre `Success` continuam como propostas.

Também evitaria uma configuração global para mudar a regra de composição. O mesmo código poderia passar a se comportar de forma diferente dependendo de quem configurou a biblioteca.

## A matriz proposta pelo autor

| Valor final | Preservar exatamente o sucesso anterior | Compor com o sucesso da próxima operação |
|---|---|---|
| Valor anterior | `Check` | `BindKeepValue` |
| Valor da próxima operação | `BindKeepSuccess` | `Bind` |

As quatro posições têm significado próprio. Elas descrevem o caminho em que a operação seguinte dá certo; em todas, uma falha continua interrompendo o fluxo.

Na coluna da direita, “compor” conserva o sucesso anterior quando o próximo é neutro e usa o novo quando ele é rico. Essa regra foi confirmada pelo autor. Não adotar incondicionalmente o próximo sucesso: isso mudaria o contrato de `Bind` e quebraria sua identidade à direita, pois `m.Bind(Rail.Ok)` apagaria o sucesso rico de `m`.

`MapSuccess` fica fora dessa matriz: atribui ou transforma o significado explicitamente, sem executar uma operação que devolve outro rail. Pode ser usado para definir o sucesso final do caso de uso.

## Contratos das quatro operações

Considere a entrada `S(v, s1)` e um callback que retorna `S(u, s2)`. A notação separa o valor do sucesso semântico.

| Operação | Valor final se tudo der certo | Sucesso final | Intenção |
|---|---|---|---|
| `Bind` existente | `u` | `s1 ⊕ s2` | Continuar a computação |
| `Check` | `v` | `s1` | Validar uma condição falível e preservar o resultado anterior |
| `BindKeepValue` | `v` | `s1 ⊕ s2` | Executar um passo falível, preservando o dado anterior |
| `BindKeepSuccess` | `u` | Exatamente `s1` | Trocar o dado, preservando o significado anterior |
| `MapSuccess` | `v` | Sucesso devolvido pelo mapper | Transformar somente o significado |

Para os três métodos com callback que retorna um rail: uma falha na entrada não executa o callback; uma falha devolvida pelo callback passa a ser o resultado, sem valor nem sucesso anterior. O nome `KeepSuccess` não deve permitir ocultar uma falha.

### Check

É a adição de maior utilidade imediata: permite validações que já devolvem `Rail`, com seu próprio erro de domínio, sem perder o dado validado. Difere de `Ensure`, que usa um predicado e um erro fornecido, e de `Tap`, que executa uma ação sem interpretar um rail devolvido.

Há um precedente direto no [`Check` de CSharpFunctionalExtensions](https://raw.githubusercontent.com/vkhorikov/CSharpFunctionalExtensions/master/CSharpFunctionalExtensions/Result/Methods/Extensions/Check.cs): uma verificação bem-sucedida preserva o resultado anterior; uma falha é propagada. Para FlowOnRails, ainda é necessário explicitar a política de sucesso semântico.

Assinatura inicial suficiente em `RailFor<TValue>`: `Check(Func<TValue, Rail> check)`. Uma forma em `Rail` pode receber `Func<Rail>`. Adicionar callbacks com valores de retorno que serão descartados somente se houver necessidade concreta.

Conforme a intenção confirmada pelo autor, até um sucesso rico devolvido pela verificação é descartado. Isso deve aparecer em um exemplo, não ficar escondido na implementação. `Check` também serve para executar um passo obrigatório do qual só interessa a eventual falha, não apenas para predicados de validação.

Não implementar simplesmente como `Bind(...).Map(...)` sem tratar os sucessos: a composição normal pode incorporar o sucesso que `Check` deveria descartar. Com os contratos desta proposta, `Check(f)` pode ser derivado de `BindKeepSuccess(value => f(value).Map(() => value))`, quando `f` retorna `Rail`.

### BindKeepValue

É útil para persistir ou executar outro passo falível e continuar trabalhando com o objeto inicial. Por exemplo, uma função de persistência pode devolver `Rail` com `UserSaved`, mas o próximo passo ainda precisa do usuário.

Assinatura inicial: `BindKeepValue(Func<TValue, Rail> bind)`. Uma forma genérica que recebe `RailFor<TNext>` pode existir posteriormente, se houver uso real para descartar `TNext`.

Não duplica `Check` enquanto incorporar o novo sucesso rico. Quando o callback retorna neutro, as duas operações preservam o sucesso anterior; quando retorna um sucesso rico diferente, seus resultados divergem. Com a semântica confirmada, `BindKeepValue(f)` pode ser derivado de `Bind(value => f(value).Map(() => value))`, quando `f` retorna `Rail`.

### BindKeepSuccess

Tem um lugar mais específico: uma transformação falível do valor cujo sucesso semântico deve continuar pertencendo à operação anterior.

Assinatura candidata: `BindKeepSuccess<TNext>(Func<TValue, RailFor<TNext>> bind)`. Para transformações infalíveis, `Map` já preserva o sucesso; não há necessidade de usar um bind especial.

Eu interpretaria “keep” literalmente: se o sucesso anterior é neutro, ele continua neutro mesmo que o próximo seja rico. Escolher o primeiro sucesso rico seria outra política.

Esse método não substitui `Bind` nas leis monádicas: `Ok(v).BindKeepSuccess(f)` pode descartar o sucesso rico de `f(v)`, portanto não satisfaz a mesma identidade à esquerda. Isso não o torna inválido; significa que ele é uma operação especializada. Refatorações também não devem trocar livremente agrupamentos que misturem políticas de preservação diferentes.

### MapSuccess

O autor a descreveu como uma forma explícita de atribuir um sucesso ao rail. Essa intenção é útil. Uma assinatura com mapper também permite transformar o significado anterior: `MapSuccess(Func<Success, Success> map)`. Para simplesmente atribuir, o callback pode ignorar a entrada: `MapSuccess(_ => new OperationCompleted())`.

Eu avaliaria uma sobrecarga com valor, `Func<TValue, Success, Success>`, para construir o significado final do caso de uso a partir do dado produzido. Se a operação receber apenas um objeto pronto, `WithSuccess(Success)` comunica atribuição de forma mais direta. Não é necessário oferecer ambos os nomes na primeira versão.

Regras sugeridas:

- Invocar o mapper uma vez em qualquer resultado bem-sucedido, inclusive neutro.
- Passar uma representação válida de neutro, sem expor `null` ao mapper.
- Substituir o sucesso pelo retorno do mapper, em vez de combiná-lo com o anterior.
- Preservar valor e estado de sucesso; uma falha passa intacta, sem invocar o mapper.
- Rejeitar retorno nulo e normalizar um retorno neutro.
- Propagar exceções do mapper, como nas demais operações; não convertê-las implicitamente em `Error`.

Substituição é relevante: mapear para neutro deve poder remover um significado anterior. Usar a combinação de `Bind` nessa implementação impediria essa remoção. Testar identidade e composição de `MapSuccess`, inclusive com neutro.

## Outras operações com utilidade concreta

| Candidata | Contrato sugerido | Prioridade |
|---|---|---|
| `Ensure(predicate, errorFactory)` | Criar o erro somente quando o predicado falhar; pode receber o valor para produzir o erro | Alta: evita construção desnecessária de erros e facilita mensagens contextualizadas |
| `TapError(Action<Error>)` | Observar uma falha sem recuperá-la nem trocar seu erro | Alta se há logging recorrente nas cadeias |
| `WithSuccess(Success)` | Em sucesso, substituir explicitamente o significado; em falha, preservar o erro | Conveniência; pode ser expresso por `MapSuccess` |
| `WithoutSuccess()` | Em sucesso, remover o significado rico e usar neutro; preservar valor e falhas | Útil em fronteiras de camadas; decidir após normalizar o neutro |
| `TryGetSuccess<TSuccess>(out ...)` | Retornar `true` somente quando o resultado for sucesso daquele tipo, com anotação de nulabilidade correta | Conveniência para consumidores; pattern matching já resolve muitos usos |
| `Tap` recebendo valor e sucesso | Observar o significado sem substituí-lo | Avaliar somente após exemplos reais; evitar sobrecargas ambíguas |
| `Traverse` sequencial | Aplicar uma função falível a cada item e parar antes de invocar os itens seguintes após uma falha | Posterior, depois de fechar o contrato de coleções |

`WithSuccess` e `WithoutSuccess` seriam operações de substituição local. Um resultado tornado neutro por `WithoutSuccess` ainda é identidade para um `Bind` externo: isso não apaga automaticamente um sucesso rico de etapas anteriores fora daquele resultado.

`Traverse` sobre uma função não é equivalente, em efeitos, a combinar tarefas que já foram iniciadas. Uma eventual API paralela precisa de contrato próprio para ordem, falhas e cancelamento.

Eu adiaria `BindSuccess`, recuperação por subtipo, `Zip` paralelo e acumulação de múltiplos erros. Cada uma dessas ideias exige novas decisões; a existência de uma simetria de nomes não é suficiente para justificá-las. `OrElse` também é uma conveniência sobre `RecoverWith` quando o fallback ignora o erro.

## Decisão pendente sobre Combine

Hoje `Combine` conserva os valores e descarta sucessos ricos. Recomendo especificar isso antes da 0.2.0, mesmo que a decisão final seja manter o descarte.

Uma opção coerente com `Bind` é reduzir os sucessos com `⊕` na ordem da enumeração: sequência vazia produz lista vazia com neutro; qualquer falha interrompe a enumeração e devolve o primeiro erro; todos os sucessos produzem a lista e o último significado rico. Essa é uma mudança proposta, não o comportamento atual.

Essa opção não agrega os significados individuais. Se o consumidor precisa associar cada valor a seu sucesso, pode conservar os rails individuais ou construir um agregado próprio. Uma única referência `Success` pode representar esse agregado; a biblioteca não precisa conhecer sua estrutura. A regra padrão não deve interpretá-la ou preenchê-la implicitamente.

## Escopo recomendado

1. Consolidar o núcleo, começando pela normalização do neutro e pelos contratos de preservação e substituição. Corrigir também os exemplos públicos que não compilam.
2. Converter a matriz confirmada em exemplos e testes, incluindo neutro e falha, e fechar as assinaturas públicas.
3. Implementar primeiro `Check` e `MapSuccess`, com testes do contrato.
4. Acrescentar `BindKeepValue` se os exemplos exigirem conservar um dado e incorporar o sucesso de outro passo.
5. Acrescentar `BindKeepSuccess` somente com um caso concreto que não seja resolvido por `Map` ou pela definição explícita do sucesso na saída.
6. Retomar as pendências de `Task` e `ValueTask` após estabilizar o núcleo; corrigir composição e sobrecargas antes de expandir essas extensões, usando testes de compilação e tarefas realmente pendentes.

Não é necessário implementar todas as candidatas para publicar a 0.2.0. Um conjunto pequeno e previsível vale mais do que uma matriz completa de nomes e sobrecargas sem cenários que a justifiquem.
