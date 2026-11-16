# API de consulta e limitações estruturadas

A API FlowOnRails.Analysis ganhou TryCreate, TryAnalyzeMethod, LimitationDetails e
Origin. CanAnalyze, o construtor, AnalyzeMethod e a lista textual Limitations
continuam disponíveis. Não houve publicação nesta etapa.

## Contrato

- TryCreate retorna false/null quando os tipos necessários da biblioteca estão
  ausentes ou ambíguos. Uma Compilation nula continua sendo argumento inválido.
- TryAnalyzeMethod retorna false/null para símbolos nulos, estrangeiros ou retornos
  não suportados. Uma consulta aberta ou degradada por metadados inválidos retorna
  true com um resumo; cancelamento e falhas inesperadas não são ocultados.
- Os métodos Try e CanAnalyze têm anotações de fluxo de nulidade, inclusive quando
  consumidos do assembly netstandard2.0.
- LimitationDetails contém Code, do enum ErrorFlowLimitationCode, e Message para
  apresentação. Os códigos são atribuídos onde o limite é detectado e propagados
  pelo motor, incluindo chamadas, variantes condicionadas e saídas ref/out.
- Limitations preserva a visão textual distinta, sem exigir migração do código que
  apenas apresenta mensagens. As mensagens não são identificadores estáveis.
- Origin informa como a consulta principal foi resolvida: SourceBody,
  AssemblyMetadata ou OpenContract. Não é a origem individual de cada erro ou
  limite; um corpo local pode incorporar metadados e continuar SourceBody.

## Metadados

O formato de exportação passa de 2 para 3, acrescentando o ID inteiro de cada
limitação junto à mensagem. O leitor preserva suporte a 1 e 2, incluindo as
alternativas condicionadas do formato 2. Textos legados recebem Unclassified;
nenhuma classificação é inferida pelo conteúdo da mensagem. IDs ainda não
reconhecidos no formato 3 também mantêm a mensagem como Unclassified.

As situações de metadados ausentes, inválidos, com versão não suportada ou com
entradas conflitantes têm códigos distintos e mantêm o resultado aberto. Limites
por orçamento, corpo ausente, despacho aberto, tipos inacessíveis e efeitos não
suportados também têm classificação estável. HasUnknownErrors e os demais estados
continuam determinando as possibilidades do retorno; a classificação não amplia
o domínio de interpretação do motor.

## Verificação

A suíte da solução passou com **1.309 testes**, dos quais **351** são do
analisador/API. Foram adicionados **14 casos** em StructuredSummaryApiTests.cs:
contratos Try, cancelamento, origem principal, metadados ausentes/inválidos/
conflitantes, limites propagados, tipos inacessíveis, orçamento e formatos 1/2/3.
O teste legado com guardas verifica que motivos de variantes não selecionadas não
vazam para o resultado especializado. As mensagens são deliberadamente diferentes
dos códigos em alguns testes, para comprovar que a API não depende de seu texto.

O exemplo de controllers foi migrado para as consultas Try e continuou funcionando
com seis avisos de mapeamento ausente e um contrato aberto. O consumidor do
compilador real produziu os dois AUDIT001 intencionais, sem falha interna.

Os quatro consumidores isolados dos pacotes locais também passaram, incluindo
o consumidor da API com TryCreate, TryAnalyzeMethod, Origin e LimitationDetails,
compilado com nulidade habilitada e avisos como erros. O consumidor produtor/cliente
verificou o gerador v3, os diagnósticos e a desativação explícita. Os pacotes
permanecem locais, sem publicação.

O README da API e o guia de integração dos satélites foram atualizados. A API
continua consultando métodos com entradas desconhecidas; não foi adicionada
interpretação de transformações HTTP ou consulta contextual pública de expressões.
