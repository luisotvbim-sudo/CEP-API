# Laboratório local Monday e VR Mais

Protótipo de leitura para validar seleção de perfil, autoria das sessões e associação com atividades antes de implementar o módulo na CEP API. Requer Node.js 24; não instala pacotes.

## Executar

No PowerShell, na raiz do repositório:

```powershell
$env:MONDAY_TOKEN_FILE = Read-Host 'Caminho do arquivo que contém somente o token Monday'
$env:VR_TOKEN_FILE = Read-Host 'Caminho do arquivo que contém somente o token VR Mais'
node tools/monday-lab/server.mjs
```

Abra http://127.0.0.1:4177. Selecione um perfil por nome/e-mail, sincronize e experimente os filtros de situação e período. Pare o servidor com Ctrl+C.

Para manter o laboratório em segundo plano no Windows, depois de definir as variáveis acima, execute na raiz do repositório:

```powershell
$labProcess = Start-Process -FilePath (Get-Command node).Source -ArgumentList 'tools/monday-lab/server.mjs' -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -PassThru
# Para encerrar este processo durante a mesma sessão PowerShell:
# Stop-Process -Id $labProcess.Id
```

Use uma das duas formas de execução, pois ambas ocupam a porta 4177. O processo em segundo plano não inicia automaticamente após reiniciar o Windows. Se o servidor parar, a página aberta perde a conexão; ao reiniciar, é necessário sincronizar novamente, pois os dados ficam em memória.

## Escopo

- Somente o Micro Planejamento e seus subitens, com a permissão da credencial configurada.
- Selecionar um perfil simula o vínculo: não autentica como essa pessoa nem grava uma associação de login.
- A atividade/serviço é o item ou subitem que contém a sessão. Não há cadastro separado de serviços neste teste.
- Autoria pelo identificador de quem iniciou o relógio; não pelo responsável do item ou por quem encerrou.
- Na exploração do Monday, o período filtra a data de início da sessão em America/Sao_Paulo. A página de comparação descrita abaixo relaciona os totais diários com o ponto, sem dividir sessões entre dias.
- Leitura paginada dos itens disponíveis; não comprova cobertura de arquivados, excluídos ou movidos para fora do quadro.
- Sessões em andamento refletem a última coleta; atualizar novamente para consultar mudanças na fonte.
- Dados mantidos em memória. Nenhum token é servido à interface; nenhum dado pessoal é gravado em arquivos.

O servidor é exclusivo para uso local, vinculado a loopback. Não publicar ou incluir no deploy de produção. Este laboratório não substitui a autenticação e o isolamento por organização do módulo definitivo. O token VR é opcional para explorar apenas o Monday; a consulta de ponto depende de `VR_TOKEN_FILE`.

## Horas de ponto (VR Mais)

A tela permite selecionar explicitamente o colaborador VR e consultar um intervalo de até 31 dias. Uma correspondência por nome/e-mail é somente sugestão para esta consulta, sem gravar vínculo de identidade.

O servidor consulta o cadastro mínimo de colaboradores e o relatório de jornada. O POST do relatório extrai dados, sem criar ou alterar batidas. A interface mostra o total diário fornecido pelo VR e as batidas disponíveis, sem descontar intervalos novamente. Datas ausentes ou valores não reconhecidos permanecem desconhecidos, nunca zero inventado.

O significado do total recebido em abonos, ajustes e jornadas especiais ainda precisa de homologação. Esta tela apresenta dados de origem; não fecha folha, não apura horas extras oficiais e não estabelece automaticamente divergência válida com o Monday.

## Comparação das fontes

Abra http://127.0.0.1:4177/comparacao. Escolha os dois perfis, confira a sugestão e confirme que representam a mesma pessoa para esta consulta. A confirmação é temporária; nenhum vínculo é gravado.

O intervalo é comum às duas fontes. A página apresenta os totais diários disponíveis e a diferença `Monday − VR`, com detalhes das atividades e batidas. Dias com relógios abertos, sessões que atravessam a meia-noite, datas ainda em andamento ou fonte ausente não recebem diferença calculada. A política de jornadas especiais ainda não foi homologada.

Os resumos somam somente a mesma base de dias com cálculo disponível. Horas a menos, a mais e soma das diferenças absolutas são separadas para que diferenças em dias distintos não desapareçam no saldo. Dias sem registros nas duas fontes não são declarados conciliados. Sem base, os totais da comparação ficam indisponíveis.

Avisos globais de qualidade da coleta permanecem visíveis; cálculos com esses avisos são referências exploratórias sujeitas a conferência. Não há tolerância aprovada nem conclusão automática sobre produtividade, falta ou hora extra.

## Verificação

### Comparação por turno

Nesta experiência, turno é cada par consecutivo de entrada e saída nas batidas do VR (por exemplo, 08:00–12:00 e 13:00–17:00), não o horário contratual previsto. O ponto do turno é a duração entre essas duas batidas. O Monday do turno soma somente a parte das sessões que coincide com esse intervalo, mantendo a autoria pelo iniciador. O detalhe informa também sobreposição entre sessões e tempo Monday fora dos intervalos de ponto.

Batidas ímpares, inválidas ou fora de ordem não são reorganizadas nem completadas automaticamente. Jornadas noturnas do ponto exigem confirmação da data de cada batida e ficam indisponíveis neste protótipo. Dados incompletos, relógios abertos e dias ainda em andamento suspendem a diferença por turno. O total calculado entre batidas pode diferir do total diário informado pelo VR por ajustes ou abonos; ambos permanecem distintos na tela.

### Comandos

```powershell
node --test tools/monday-lab/server.test.mjs
node --test tools/monday-lab/vr.test.mjs
node --test tools/monday-lab/comparison.test.mjs
node --test tools/monday-lab/shift-comparison.test.mjs
# Com servidor aberto e coleta concluída:
node tools/monday-lab/smoke.mjs
node tools/monday-lab/vr-smoke.mjs
node tools/monday-lab/comparison-smoke.mjs
```

Conferir seleção por nome/e-mail, troca de perfil, situações em andamento/encerradas, intervalos de datas, estado vazio, mensagens de falha e associação da sessão ao item de origem. Uma carga incompleta deve ser indicada, nunca interpretada como ausência de horas.

### Resultado observado em 17/09/2026

- Os 16 testes automatizados passaram, incluindo falha de uma página posterior sem perda do snapshot anterior, leitura VR e comparação diária.
- Smoke test com dados reais passou: filtros por iniciador, situação e dia de início em São Paulo; associação ao item; ausência de chaves duplicadas; rejeição de data inválida e origem externa.
- Consulta direta ao Monday por relógios rodando retornou HTTP 200 e somente itens com `running=true`.
- A leitura encontrou sessões indeterminadas/relógio aberto sem sessão identificável; a interface informa esses diagnósticos. Não pressupor que o total de relógios abertos seja igual ao total de sessões abertas atribuíveis.
- A coleta é um retrato das páginas lidas em sequência, não uma transação consistente de toda a conta. A fonte pode mudar durante a leitura.
- A página de comparação possui testes para a base comum de dias, sinais, diferenças absolutas, sessão atribuída ao iniciador, fonte ausente, dia atual, relógio aberto, travessia de meia-noite e encerramento exatamente à meia-noite (fim exclusivo). A exibição preserva segundos quando presentes.

### Comparação por turno validada em 18/09/2026

- Os 24 testes automatizados passaram, incluindo recorte das sessões nos intervalos, sobreposições, batidas incompletas e conversão histórica do fuso.
- A consulta real de sete dias validou oito turnos calculados, com diferenças aritméticas consistentes e ausência de cálculo quando não havia base.
- A interface foi conferida com a coluna por turno e o detalhamento dos intervalos, horas fora dos turnos e cobertura sem duplicar sobreposições.

Resultados brutos, nomes, e-mails e tokens não são incluídos nesta documentação. As contagens da UI representam a coleta em memória e mudam quando uma nova sincronização é solicitada.
