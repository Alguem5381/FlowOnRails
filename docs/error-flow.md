# Análise de erros do FlowOnRails

O analisador informa quais tipos derivados de Error podem alcançar o retorno de
uma função como falha de Rail/RailFor. Ele acompanha tratamentos e transformações,
propaga resultados entre métodos e exporta resumos para outros assemblies.
Um Error usado como valor de sucesso continua sendo sucesso.

O recurso está em prévia. A auditoria independente e suas correções estão
registradas em `docs/reviews`. O conjunto é
uma aproximação dos fluxos suportados, não uma garantia de exaustividade em execução.

## Qual pacote usar

| Pacote | Uso |
| --- | --- |
| `FlowOnRails` 0.3.0 | Biblioteca base, atributos, diagnósticos, exportação e correções de editor |
| `FlowOnRails.Analysis` 0.3.0 | API Roslyn para desenvolver geradores/analisadores |

## Instalação em aplicações e bibliotecas produtoras

```xml
<ItemGroup>
  <PackageReference Include="FlowOnRails" Version="0.3.0" />
</ItemGroup>
```

Ative a análise em cada projeto que deve exportar ou consumir resumos. Ativar
a análise apenas na aplicação não recupera os corpos dos DLLs de
serviços já compilados. Os atributos exportados ficam no assembly produtor e
sobrevivem à geração de assemblies de referência.

O pacote base inclui o analisador e expõe a configuração ao Roslyn. A análise
e a exportação ficam desativadas por padrão. Para ativá-las:

```xml
<PropertyGroup>
  <FlowOnRailsEnableFlowAnalysis>true</FlowOnRailsEnableFlowAnalysis>
</PropertyGroup>
```

Sem o pacote, uma referência direta ao projeto do analisador continua exigindo a
propriedade true e `CompilerVisibleProperty`; o pacote já fornece essa configuração.
A aplicação usa .NET 10. O host do analisador precisa de Roslyn 5.0 ou compatível;
uma IDE antiga pode compilar a biblioteca e ainda não conseguir carregar a ferramenta.

## Onde aparecem as informações

| Diagnóstico | Local | Conteúdo |
| --- | --- | --- |
| FORFLOW001 | Nome da declaração do método ou contrato | Resumo para entradas desconhecidas |
| FORFLOW002 | Nome da chamada | Resultado naquele ponto, antes do tratamento posterior |
| FORFLOW003 | Palavra-chave switch | Ação opcional para inserir casos conhecidos; oculto por padrão |

001 e 002 são Info, não warnings. A ferramenta não afirma que toda possibilidade
listada ocorrerá. As mensagens agrupam tipos por namespaces e separam o conjunto
de erros das limitações. `Error` base não aparece como entrada redundante na árvore.

```ini
[*.cs]
dotnet_diagnostic.FORFLOW001.severity = suggestion
dotnet_diagnostic.FORFLOW002.severity = suggestion
# Necessário para oferecer a correção no ReSharper para VS Code testado:
dotnet_diagnostic.FORFLOW003.severity = suggestion
```

O compilador normalmente não imprime Info no terminal. Para conferir a emissão,
use `<ErrorLog>$(MSBuildProjectDirectory)/obj/error-flow.sarif,version=2.1</ErrorLog>`.
A apresentação das quebras de linha depende da IDE. Depois de trocar DLLs locais,
recarregar a janela evita manter um provedor antigo em memória.

Ocultar FORFLOW001 não desativa a exportação, mas retira a ação ResultsWith que
está associada a esse Info. `Rail.Ok` comum não recebe Info na chamada; quando seu
valor de sucesso é Error, há uma mensagem específica. `Rail.Fail` mantém o resumo.

## Interpretando os estados

- Erros conhecidos: tipos ou limites de famílias que o fluxo pode produzir.
- Parcela aberta: outros subtipos podem aparecer. Interfaces, despacho virtual não
  fechado, ausência de metadados e construções não suportadas podem causá-la.
- Condições indeterminadas: caminhos que talvez não sejam executáveis também foram
  considerados. A lista pode ser mais ampla, sem necessariamente ficar aberta.
- Rail não inicializado: `default` não é um erro de domínio e aparece separadamente.
- Limitações: explicam por que parte da análise precisou ser conservadora.

Não existe uma lista finita de todos os descendentes de uma família aberta.
Mesmo um conjunto fechado nos fluxos suportados não autoriza remover um fallback
ou suprimir o diagnóstico de exaustividade do compilador.

## Contratos de interface

```csharp
public interface IUserReader
{
    [ResultsWith<UserNotFound, AccessDenied>]
    RailFor<User> Read(int userId);
}
```

`ResultsWith` aceita de um a dezesseis tipos derivados de Error e é repetível.
Ele documenta possibilidades conhecidas, mantendo o contrato aberto. A ausência
de um atributo não proíbe a implementação de retornar outro erro.

Na chamada à interface, o usuário vê os erros declarados pelo contrato e a parcela
aberta. Na implementação concreta, pode ver outros tipos, inclusive os de uma camada
que o contrato não referencia. O analisador não deduz domínio ou infraestrutura pelo
nome do assembly.

Com o cursor no nome do método da implementação, Alt+Enter pode oferecer
**Declare known errors on interface contracts (ResultsWith)**. A ação edita a
interface fonte, inclusive em outro projeto editável da solução. Só adiciona tipos
acessíveis e resolvidos para o mesmo símbolo na compilação do contrato; não adiciona
referências de projetos, não duplica famílias já declaradas e não edita um DLL.
Não existe um warning separado por atributo ausente na interface.

## Casos de switch

Com o cursor na palavra `switch`, **Add cases for known errors (keep other cases)**
insere tipos conhecidos ainda não cobertos. Funciona em statement e expression,
preserva os casos existentes e o fallback, e gera `NotImplementedException` como
corpo a ser preenchido pelo usuário. Casos com condição não cobrem necessariamente
toda uma família. Padrões compostos não suportados podem retirar a ação.

A ferramenta não oferece supressor e não transforma contratos abertos em fechados.
As duas correções são individuais; não há Fix All para evitar alterações concorrentes
em contratos compartilhados ou geração de vários tratamentos não implementados.

## Precisão atual

O motor usa operações semânticas e CFG. Acompanha atribuições, cópias locais,
padrões de tipo, retornos antecipados, loops, operadores da biblioteca e métodos
concretos com substituição genérica. Task e ValueTask são desembrulhados.

Funções locais, lambdas e grupos de métodos com destinos conhecidos são analisados.
Escritas capturadas e saídas ref/out são propagadas nas saídas normais, depois de
finally. Aliases locais de referência compartilham a célula; destinos ambíguos usam
união conservadora. Getters e indexadores concretos podem usar o corpo disponível.

Try/catch/finally distinguem exceptions de erros retornados. Uma exception não vira
Error automaticamente; um catch que retorna uma falha pode acrescentá-la. Filtros
e tipos de exceções de chamadas arbitrárias continuam aproximados.

Recursão de retornos estabiliza aproximações dentro de orçamento. Efeitos mutáveis
recursivos, closures que escapam, capturas atravessando suspensão async, heap/arrays,
ref returns e destinos arbitrários de delegates continuam conservadores.

Limites atuais: 4096 visitas de blocos, profundidade 32, 256 contextos de chamadas,
16 iterações recursivas e até 16 destinos de delegate. Metadados condicionais têm
até 16 alternativas; grafos com mais de 128 blocos usam o resumo amplo. Limites
atingidos nunca são tratados como prova de que não há mais erros.

## Metadados entre assemblies

`FlowAnalysisSummaryAttribute` contém versão do formato, identificador documental
do método original e payload opaco. O gerador exporta versão 3; o leitor aceita
versões 1, 2 e 3. A versão 2 preserva alternativas condicionadas, projeções de
parâmetros e saídas ref/out; a versão 3 acrescenta os códigos estáveis das limitações.
Tipos genéricos são substituídos na chamada; tipos privados indisponíveis ampliam
a parcela afetada. Dados inválidos, versões desconhecidas ou ausência de manifesto
mantêm o resultado aberto.

Os atributos não são uma API para decodificação manual. Desenvolvedores de satélites
devem usar a [API de análise](../src/FlowOnRails.Analysis/README.md) e o
[guia de integração](design/analysis-integration.md).

## Organização e verificação

`ErrorFlowAnalyzer` registra diagnósticos; `ErrorFlowGenerator` exporta metadados;
`FlowOnRails.CodeFixes` implementa as ações da IDE. O motor interno da API está
separado em operações, condições, operadores Rail, chamadas e fluxo excepcional.
A divisão em arquivos melhora a navegação, mas o interpretador continua sendo uma
classe parcial coesa, não cinco serviços independentes.

Os exemplos do repositório estão em `samples/FlowOnRails.Analysis.Sample`, incluindo
Study, EditorActions, ExceptionStudy e CallStudy. Os testes usam compilações reais,
assemblies de referência e documentos Roslyn. A verificação de pacotes usa
consumidores fora do repositório, apenas com PackageReference.

A auditoria com GPT-6 Astra, raciocínio alto e sem histórico está registrada em
[relatório](reviews/2026-11-12-analysis-api-audit.md), junto ao
[acompanhamento das correções](reviews/2026-11-12-analysis-api-fixes.md).
A validação dessas correções não constitui uma nova auditoria independente.
