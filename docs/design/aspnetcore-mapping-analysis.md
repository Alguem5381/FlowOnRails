# Verificação de mapeamentos HTTP

O analisador HTTP verifica erros conhecidos no contexto de cada endpoint. Uma
action de controller e uma rota de Minimal API usam a mesma resolução de
visibilidade e cobertura; a descoberta de seus escopos é diferente.

## Responsabilidades

A configuração é lida estaticamente, sem executar callbacks da aplicação.
O modelo da compilação associa registros de visibilidade e tradução aos
endpoints cuja aplicação pode ser identificada. Regras do controller exato
são compartilhadas pelas actions desse controller. Regras de um grupo são
compartilhadas pelas rotas desse grupo, respeitando sua hierarquia.

O contexto `ErrorFlowAnalysis` pertence à compilação. A consulta pública fornece
os valores das expressões Rail que alimentam a conversão HTTP, por meio de
`AnalyzeExpressions`. Todas as entradas de conversão de um mesmo corpo são
consultadas juntas, e seus resultados são reutilizados quando o handler atende
mais de uma rota. Cada rota continua resolvendo sua própria visibilidade. O satélite não
interpreta mensagens de diagnósticos, não lê payloads de metadados e não
implementa outro interpretador de fluxo Rail.

## Quando há um aviso

Para um erro exato conhecido, resolve-se a política na seguinte ordem:
endpoint, grupos internos para externos, controller exato e global. Dentro de
um escopo, a raiz mais próxima vence; na mesma raiz, seleção exata precede
descendentes, que precedem árvore.

| Política efetiva | Verificação |
| --- | --- |
| Genérico explícito ou padrão | Nenhum mapa específico é exigido |
| `TranslateEachType` | Deve existir um mapa exato para o erro |
| `UseNearestAncestorMapping` | Deve existir um mapa para algum ancestral autorizado |

A busca por ancestral respeita o intervalo excluído pela seleção original e
as barreiras genéricas. Um candidato sem mapa não impede tentar o próximo.
Mapear um ancestral não autoriza automaticamente seu contrato nem substitui
o mapa concreto quando a política exige `TranslateEachType`.

`FORHTTP009` é um warning: o erro conhecido usará a resposta genérica porque
o contrato selecionado não tem mapa. O usuário pode configurar um mapa
aplicável ou escolher uma resposta genérica na política.

## Configuração e fluxo desconhecidos

A configuração aceita callbacks diretos. Condições de configuração, helpers e
aliases não resolvidos não são executados nem presumidos.

O fluxo do handler pode usar variáveis locais, recuperação, chamadas com argumentos
e Task/ValueTask, conforme o motor base. A consulta considera o valor no ponto de
conversão para `ToHttpResult` ou `ToActionResult`, inclusive em retornos de ramos
e expressões condicionais. Parâmetros e capturas começam desconhecidos. Conversões
inalcançáveis não exigem mapa. Conversões escondidas em helpers que retornam HTTP,
ou armazenadas como resultados HTTP antes do retorno, ficam indisponíveis porque
a API de fluxo não representa a proveniência de valores HTTP.
As formas concretas suportadas estão descritas no README do pacote.

`FORHTTP010` é informativo e indica que a verificação ficou indisponível ou
incompleta. Uma configuração desconhecida de um controller não torna desconhecida
a configuração dos outros. Quando a associação à aplicação ou a configuração
global não puder ser determinada, o alcance da incerteza pode ser maior.

O analisador precisa distinguir uma política desconhecida de uma política
conhecida que escolhe o genérico. Encontrar declarações de mapas também não
comprova que esses mapas foram registrados na aplicação do endpoint.

## Limites dos resumos

Casos exatos conhecidos podem ser verificados mesmo quando o resumo contém
erros adicionais desconhecidos. O limite de uma família aberta não deve ser
tratado como um tipo concreto retornado. Ausência de aviso não comprova cobertura
de todos os erros em execução.

O fallback genérico permanece necessário em todos os casos. Nenhum resumo
autoriza removê-lo, escolher status pelo nome do erro ou expor propriedades que
a política não autorizou. Rail não inicializado continua sendo um erro de
programação separado dos erros de domínio.

## Compilação e distribuição

As consultas explícitas à API independem de `FlowOnRailsEnableFlowAnalysis`.
Essa propriedade continua necessária no projeto produtor para exportar resumos
de handlers consumidos por outro assembly. Um consumidor não recupera os corpos
ausentes de um DLL apenas por instalar o analisador HTTP.

A distribuição do analisador ASP.NET requer a API de análise junto à ferramenta,
como asset de analyzer. Roslyn e `FlowOnRails.Analysis` não são dependências de execução da
aplicação. A versão distribuída deve coincidir com a distribuída pelas ferramentas
da base.
