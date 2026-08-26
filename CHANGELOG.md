# Changelog

Todas as mudanças relevantes do SteamNexus são registradas neste arquivo.

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
