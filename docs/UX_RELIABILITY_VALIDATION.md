# Validação das melhorias de confiabilidade e navegação

Escopo: itens 1, 2, 3, 5, 6 e 7. Sem coleções, sem mudar regras de login,
RememberPassword, confirmação de operações, uso recente da Steam ou atalhos globais.

## O que mudou

- Configurações: gravação serializada, snapshot antes de aguardar, arquivo temporário
  no mesmo diretório, flush e substituição atômica. `settings.json.bak` mantém a última
  versão legível substituída. A leitura usa o backup se o principal estiver ausente ou
  inválido; uma recuperação aparece na atividade. Não é um backup dos dados da Steam.
- Componentes: estilos explícitos compartilhados para botões/resultados, cores dinâmicas,
  estados desabilitado/foco/seleção e cursor. Aplicados nos novos diálogos, confirmação e
  captura de atalho. Cabeçalho compartilhado respeita a área útil do monitor.
  Não foi feita uma substituição global dos templates de controles de terceiros.
- Navegação: páginas de contas/jogos reutilizadas durante a sessão; filtros continuam nos
  mesmos view models e a posição de rolagem é restaurada ao retornar. Contas são atualizadas
  incrementalmente ao voltar, para refletir alterações externas. Rolagem lógica usa o
  comportamento nativo; a física respeita a configuração de linhas do Windows.
- Busca: botão de lupa e Ctrl+K **somente na janela principal ativa**. Consulta os jogos e
  contas já carregados e páginas locais. Não chama API, não inicia a Steam nem jogos.
  Enter/duplo clique abre a página correspondente, filtrada pelo nome/login. Em instalações
  com nomes repetidos, pode haver mais de um resultado na página. Fechar/cancelar não altera
  os filtros. Busca sem acentos, até 40 resultados e 10 escolhas recentes em memória.
- Atividade: botão de histórico, últimas 200 operações da sessão, limpar/atualizar/diagnóstico.
  Registra resultados tipados de troca, início confirmado de jogo, reparo e download pronto.
  Registra nomes de exibição/login de origem e destino e nomes dos jogos, apenas em memória.
  Não registra exceções brutas, SteamIDs, caminhos, chaves nem credenciais.
  Não persiste entre reinicializações e não afirma que a instalação de um update foi concluída.
- Monitoramento: conserva 2 s com UI visível, jogo detectado/incerto ou operação pendente.
  Após 3 ciclos ociosos, oculto na bandeja, usa 5 s. A pausa começa após a leitura terminar.
  A descoberta de um jogo externo oculto pode demorar até cerca de 5 s mais a leitura.
  Verificações diretas antes de trocar conta/iniciar jogo não dependem dessa pausa.

## Testes automatizados

Referência local em 19/09/2026: 94 testes antes das alterações; 112 aprovados em Debug
e Release após as alterações. Build Release sem avisos. O teste isolado de busca mediu 43,7 ms
para 50 consultas de 1.000 entradas (0,88 ms/consulta, Debug); varia por máquina/carga.

```powershell
dotnet test SteamSwitcher.Tests/SteamSwitcher.Tests.csproj
dotnet build SteamSwitcher.sln -c Release
dotnet test SteamSwitcher.Tests/SteamSwitcher.Tests.csproj --filter FullyQualifiedName~Search_1000Entries --logger "console;verbosity=detailed"
```

Os testes de persistência usam diretórios temporários, nunca o `settings.json` real.
Cobrem arquivo corrompido, ausência do principal, concorrência, falha de serialização,
arquivo bloqueado e preservação de preferências existentes. Não corrompa o arquivo real
para testar recuperação. O teste WPF usa uma aplicação mínima, sem iniciar o Nexus/Steam,
para carregar os novos diálogos e validar recursos e alteração de cor.

## Roteiro manual / critérios de aceite

| Etapa | Como verificar | Resultado esperado |
| --- | --- | --- |
| Configurações | Alterar uma preferência, fechar normalmente e abrir novamente | Preferência mantida; após a segunda gravação existe backup legível |
| Tema | Usar claro, escuro, roxo e personalizado; abrir busca, histórico, confirmação e captura de atalho | Cores do tema, foco visível e botão desabilitado distinguível |
| Layout | Repetir em janela mínima e maximizada, DPI 100%, 150%, 200% e segundo monitor | Ações acessíveis; conteúdo não cobre rodapé; listas longas rolam |
| Navegação | Filtrar contas/jogos, ordenar, rolar; ir a configurações e voltar | Mesmos filtros/página e posição, salvo ajuste de limite após mudança real da lista |
| Atualização externa | Alterar contas externamente enquanto a página estiver oculta, depois voltar | Dados atualizados sem limpar a busca |
| Scroll | Usar roda e touchpad em lista/poster, filtros e diálogos | Sem salto de vários itens ao início/final; verificar configuração de rolagem por página do Windows |
| Busca | Ctrl+K, buscar sem acento, usar setas, Enter, Esc, Tab e mouse | Resultado correto; Esc não muda estado; nenhum jogo/conta iniciado automaticamente |
| Fora do Nexus | Com outro app ou jogo em foco, usar Ctrl+K (não configurado como atalho global pelo usuário) | Busca rápida do Nexus não abre |
| Proteção existente | Buscar enquanto configurações têm alterações pendentes e tentar abrir outra página | Confirmação existente de saída continua sendo respeitada |
| Histórico | Efetuar uma operação segura normalmente e abrir atividade | Evento no topo; limpar esvazia; cancelamento não aparece como sucesso |
| Segurança | Com jogo rodando, tentar trocar conta pelos controles existentes | Bloqueios/confirmações anteriores preservados |

Não encerrar Steam/jogos nem fazer reparos apenas para gerar eventos de teste sem consentimento.
Use uma instalação de teste para esses cenários.

## Como medir desempenho

1. Use a mesma máquina, versão/configuração de build, biblioteca e estado da Steam.
2. Abra **Atividade recente → Medições do monitoramento** e anote leituras, falhas, média e
   P95. O botão Atualizar renova os números; o painel não cria outro monitor.
3. Compare intervalos de 60 s com janela visível e, depois, oculta na bandeja e ociosa.
   Reabra para ler o contador. Há aproximadamente 30 versus 12 ciclos por minuto, **menos o
   tempo das leituras** e ciclos de transição. Isso é uma expectativa de frequência, não
   uma afirmação de redução medida de CPU.
4. O P95 é o tempo abaixo do qual ficaram 95% das últimas até 300 atualizações completas
   de runtime. Inclui operações assíncronas: não equivale a CPU nem mede toda a aplicação.
5. No Gerenciador de Tarefas, compare CPU e memória durante os mesmos períodos. Repita
   três vezes; não conclua com uma amostra isolada ou durante downloads/carga de capas.
6. Para busca, o teste de 1.000 entradas imprime o tempo médio de 50 consultas. Ele mede
   filtro/ordenação, não o tempo completo para abrir/renderizar a janela.

Meta: nenhum teste anterior regredir, ausência de perda de preferências, ações alcançáveis
no layout e menor frequência ociosa sem enfraquecer verificações de segurança. A validação
automatizada não substitui os testes reais de DPI, touchpad e sessões da Steam.
