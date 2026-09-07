# Changelog

Todas as mudanças relevantes do SteamNexus são registradas neste arquivo.

## [Unreleased]

### Novidades

- Selo Última Usada abaixo da data da conta, na grade e lista, exibido somente quando não há uma conta ativa; utiliza a última sessão observada ou a data registrada pela Steam.
- Tray exibe o ícone do executável do jogo, sem usar o pôster, com ícone genérico quando indisponível.
- Ordenação Jogados recentemente baseada em LastPlayed da Steam e no início dos processos observados; jogos em execução aparecem primeiro em todas as ordenações.
- Ícones neutros à esquerda dos selos de login: engrenagem para Padrão, globo para Online e globo riscado para Offline.
- Selos de login compactos (Padrão, Online ou Offline), com menu ancorado de seleção e salvamento imediato. Tooltips acompanham o tema do aplicativo e mostram a origem do modo herdado.
- Cards de contas e jogos exibem a preferência Ao entrar (Padrão com modo herdado, Online ou Offline), com acesso direto à edição e atualização ao mudar o padrão global ou a conta associada.
- Contagem regressiva de cinco segundos ao iniciar um jogo sem outro em execução, com cancelamento antes de trocar a conta ou abrir o jogo.
- Tela de jogos com monitoramento de execução e ações Ver jogo e Parar.
- Jogos em execução exibidos no tray somente enquanto estão abertos, preservando as opções existentes e sem exigir uma visita à tela de jogos.
- Estados de início, execução e encerramento nos cards e na lista compacta; menu de opções com desinstalação pela Steam.
- Diálogo de troca de jogo com capas, conta de destino, progresso e confirmação antes de encerrar o jogo atual. Encerramento forçado exige uma confirmação adicional.
- Desinstalação acompanhada pela biblioteca local, com aviso sobre arquivos compartilhados entre contas e opção de cancelar o acompanhamento após cancelar na Steam.

### Correções

- Maior espaçamento entre a data de uso da conta e o selo Última Usada.
- Menus dos jogos seguem o tema do aplicativo, preservando ícones e contraste; durante a execução, somente as ações ficam desabilitadas e o aviso permanece legível.
- Botões da troca de jogo centralizados, com Não como ação principal e padrão e Sim como secundária.
- Todas as opções do menu de contexto do jogo bloqueadas durante a execução, com aviso para parar o jogo antes; ordenação renomeada para Uso Recente.
- Conta do jogo em execução identificada pela sessão ativa da mesma instalação Steam, inclusive para jogos já abertos; leituras temporariamente indisponíveis preservam a última conta identificada.
- Removido o título duplicado Em execução do tray; o status permanece somente abaixo do nome do jogo.
- Ao abrir outro jogo com um já ativo, o diálogo exige confirmação explícita em Sim ou Não, sem contagem regressiva nem ação automática.
- Removido o texto de progresso acima da biblioteca; a abertura do jogo exibe o andamento somente no diálogo, sem espaço duplicado na lista.
- Removida a faixa superior de jogos em execução. O jogo ativo recebe destaque verde no card, preservado no hover e na lista compacta.
- Alteração da conta associada e desinstalação bloqueadas enquanto o jogo estiver em execução, com orientação para pará-lo primeiro e nova verificação antes de aplicar a ação.
- Opção Jogados recentemente padronizada com o menu de ordenação: ícone à esquerda e seleção à direita, com alinhamento uniforme.
- Botões de cancelar e contagem ocultos após os cinco segundos; removida a mensagem de sucesso ao encerrar um jogo.
- Removido o filtro Em execução da tela de jogos e o espaço vazio quando não há atividade; monitoramento e indicação de jogos abertos preservados.
- Selo de preferência de login movido para o rodapé do card de jogo, abaixo das informações de instalação e fora da capa e do botão Jogar.
- Padrão global de login alterado somente por edição manual: salvar outros ajustes ou aplicar um tema preserva o valor atual, e mudanças pelo rodapé são sincronizadas com Configurações.
- Corrigido descarte concorrente de CancellationTokenSource no monitor de jogos, buscas e carregamento de capas; o monitor não é duplicado ao recarregar a instalação.
- A conta já ativa é reutilizada ao abrir jogos; se a abertura exigir Online e a sessão estiver Offline, a Steam é reiniciada em Online, sem alterar o padrão global.
- A abertura pela tela de jogos aguarda a confirmação da sessão e da execução, em vez de assumir que uma espera fixa significa que o jogo iniciou.
- Validação da instalação da Steam antes de encerrar um jogo para desinstalação; processos ambíguos ou protegidos exigem fechamento manual.
- Status padrão do rodapé independente da sessão Steam: o monitor não sobrescreve a preferência, e o primeiro clique alterna Online para Offline.
- Conta escolhida permanece visível nos cartões e no tray durante o reinício da Steam após uma troca pelo aplicativo.
- Conta offline reconhecida nos cartões e no tray quando a Steam mantém ActiveUser=0 no Registro.

## [1.0.18] - 2026-09-07

### Novidades

- Notas da atualização exibidas no aplicativo, com versões instalada e disponível.
- Opção independente de status ao entrar por jogo, separada da conta e da capa.
- Aviso de recurso em beta junto ao editor de tema personalizado.

### Correções

- Preservação de RememberPassword ao alternar contas.
- Troca permitida somente para contas com RememberPassword=1, validado antes de fechar a Steam, sem modificar essa preferência.
- Restauração da janela do tray ao abrir novamente o executável.
- Atualização automática da sessão Steam nos cartões, rodapé e tray.


## [1.0.9] - 2026-08-26

### Adicionado

- Lista unificada de contas de todas as instalações Steam cadastradas.
- Identificação da instalação nos cartões e na visualização em lista.
- Filtro de contas por instalação.
- Nomes automáticos e nomes personalizados para instalações Steam.
- Suporte a jogos instalados em diferentes instalações e bibliotecas.

### Alterado

- A troca de conta agora escolhe automaticamente o `Steam.exe` e o
  `loginusers.vdf` vinculados ao cartão selecionado.
- Contas e jogos repetidos são separados por instalação.
- Favoritos, personalizações, associações de jogos e limpeza de contas usam
  identidades compostas por instalação.
- A conta ativa é identificada pela instalação Steam realmente em execução.

### Corrigido

- Uma instalação indisponível ou com VDF inválido não impede o carregamento das
  demais instalações.
- Jogos não são mais encaminhados para a instalação padrão quando nenhuma Steam
  está em execução.
- Alterações atômicas no `loginusers.vdf` agora também disparam atualização da
  lista de contas.

[1.0.9]: https://github.com/flaviobuarque/SteamNexus/releases/tag/v1.0.9
