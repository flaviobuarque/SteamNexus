# SteamSwitcher — Relatório de Bugs e Problemas

> Classificação de severidade:
> - **Crítica** — perda de dados, corrupção ou funcionalidade principal quebrada
> - **Alta** — comportamento incorreto frequente com impacto significativo
> - **Média** — bug real com workaround ou impacto limitado
> - **Baixa** — inconsistência, código morto ou edge case raro

---

## BUG-001 — Hashes incompatíveis na detecção de mudanças

| Campo | Valor |
|-------|-------|
| **Severidade** | **Crítica** |
| **Arquivos** | `SaveWatcherService.cs`, `SaveSnapshotHasher.cs`, `BackupOrchestrator.cs`, `IBackupStateService.cs` |
| **Classes / Métodos** | `SaveWatcherService.ComputeDirectoryHash`, `SaveSnapshotHasher.Compute`, `BackupOrchestrator.OnSaveChanged`, `BackupOrchestrator.WatchGameAsync` |

**Descrição técnica:**
`LastSaveHash` é escrito com `SaveSnapshotHasher` na inicialização e com `ComputeDirectoryHash` após eventos do watcher. `LastBackupHash` usa sempre `SaveSnapshotHasher`. Os algoritmos diferem em escopo (manifest paths vs caminhos absolutos), inclusão de registry, arquivos ignorados e formato da string pré-hash.

**Impacto no usuário:**
- Status "Modificações pendentes" pode aparecer **sem mudança real** após qualquer evento do watcher
- Backups automáticos redundantes desperdiçando disco
- Ou: status "Sincronizado" incorreto se hashes coincidirem por acaso
- Lógica de `HasUnbackedChanges` fundamentalmente não confiável

**Sugestão de correção:**
Unificar em um único algoritmo (`SaveSnapshotHasher.Compute` sobre o `BackupSourceSet` re-resolvido) tanto no watcher quanto no backup. O watcher deve apenas sinalizar "algo mudou" e delegar o recálculo ao hasher unificado.

---

## BUG-002 — Restauração não implementada

| Campo | Valor |
|-------|-------|
| **Severidade** | **Crítica** |
| **Arquivo** | `RestoreService.cs` |
| **Classe / Método** | `RestoreService.RestoreAsync` |

**Descrição técnica:**
Após validação SHA256 e chamada a `CreateSafetyBackupAsync` (stub), o método retorna erro fixo: *"Restauração ainda precisa ser migrada para o novo formato com sources.json."*

**Impacto no usuário:**
Botão de restaurar na UI **nunca funciona**. Backups são efetivamente write-only. Perda de save local irreversível pelo app.

**Sugestão de correção:**
Implementar parse de `sources.json`, extração seletiva para paths originais, import de registry, safety backup real, e atualização de `backup_state.json`.

---

## BUG-003 — Safety backup antes de restore é stub vazio

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** |
| **Arquivo** | `RestoreService.cs` |
| **Classe / Método** | `RestoreService.CreateSafetyBackupAsync` |

**Descrição técnica:**
Método retorna `Task.CompletedTask` sem criar backup. Chamado antes do restore (que também falha), mas quando restore for implementado, não haverá rede de segurança.

**Impacto no usuário:**
Quando restore for habilitado, falha durante restauração pode **sobrescrever saves sem backup prévio** (dependendo da implementação futura).

**Sugestão de correção:**
Reutilizar pipeline de `CreateBackupZipAsync` + `AddVersionAsync` com label "pre-restore safety".

---

## BUG-004 — Sem lock de concorrência no backup

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** |
| **Arquivos** | `BackupOrchestrator.cs`, `CloudBackupViewModel.cs` |
| **Classes / Métodos** | `BackupOrchestrator.CreateBackupAsync`, `CloudBackupViewModel.BackupNowAsync` |

**Descrição técnica:**
Não há `SemaphoreSlim` ou flag por AppID impedindo backups simultâneos. Auto-backup e manual-backup compartilham o mesmo pipeline sem coordenação.

**Impacto no usuário:**
- ZIPs duplicados no mesmo segundo (colisão de versionId)
- Leitura concorrente de arquivos em uso
- Estado inconsistente entre manifest e backup_state

**Sugestão de correção:**
`SemaphoreSlim` por AppID no orchestrator; expor `CreateBackupAsync` publicamente e fazer manual backup chamá-lo.

---

## BUG-005 — SmartSizeVersioningPolicy deleta ZIPs sem atualizar manifest

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** |
| **Arquivo** | `SmartSizeVersioningPolicy.cs` |
| **Classe / Método** | `SmartSizeVersioningPolicy.ApplyAsync` |

**Descrição técnica:**
Policy deleta arquivos `.zip` diretamente do filesystem baseado em tamanho e pins (lidos do manifest). Não remove entradas correspondentes de `manifest.json`.

**Impacto no usuário:**
- UI mostra versões cujo arquivo não existe mais
- Restore falha com "Versão não encontrada"
- `CompressedSizeBytes` no manifest infla uso de disco reportado

**Sugestão de correção:**
Chamar `IBackupManifestService.DeleteVersionAsync` ou sincronizar manifest após prune.

---

## BUG-006 — Backup permitido com jogo em execução (manual)

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** |
| **Arquivos** | `CloudBackupViewModel.cs`, `LocalFolderProvider.cs` |
| **Classes / Métodos** | `CloudBackupViewModel.BackupNowAsync`, `LocalFolderProvider.CreateBackupZipAsync` |

**Descrição técnica:**
`RestoreService` bloqueia restore se `gameProcessService.IsRunning(appId)`, mas o fluxo de backup não faz verificação equivalente.

**Impacto no usuário:**
Backup manual durante gameplay pode capturar saves **parcialmente escritos ou corrompidos**. Restore futuro propagaria dados inválidos.

**Sugestão de correção:**
Validar `!IsRunning(appId)` antes de backup manual; opcionalmente aguardar debounce extra após fechamento.

---

## BUG-007 — Operação de backup não é transacional

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** |
| **Arquivos** | `BackupOrchestrator.cs`, `CloudBackupViewModel.cs`, `LocalFolderProvider.cs` |
| **Classes / Métodos** | `CreateBackupAsync`, `BackupNowAsync`, `UploadAsync` |

**Descrição técnica:**
Sequência Copy ZIP → AddVersion → UpdateState não é atômica. Falha entre etapas deixa ZIP órfão ou manifest sem state atualizado.

**Impacto no usuário:**
Estado da UI dessincronizado com disco; backups redundantes; versões fantasma.

**Sugestão de correção:**
Staging: escrever ZIP com sufixo `.partial`, renomear, atualizar manifest+state em batch, ou usar journal de operações.

---

## BUG-008 — Mudanças em registry não disparam detecção de alteração

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivos** | `SaveWatcherService.cs`, `SaveSnapshotHasher.cs` |
| **Classes / Métodos** | `SaveWatcherService.Watch`, `SaveSnapshotHasher.Compute` |

**Descrição técnica:**
Jogos com saves em registry (definidos no Ludusavi) são incluídos no hash do `SaveSnapshotHasher`, mas o watcher só monitora diretórios de arquivos. Mudanças em registry não disparam `SaveChanged`.

**Impacto no usuário:**
Saves baseados em registry podem **não ser backupeados** automaticamente após mudança.

**Sugestão de correção:**
Polling periódico de registry para jogos com sources de registry, ou backup full periódico.

---

## BUG-009 — Watcher observa diretório pai, não árvore exata do save

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `BackupOrchestrator.cs` |
| **Classe / Método** | `BackupOrchestrator.WatchGameAsync` |

**Descrição técnica:**
Paths de arquivo individual são convertidos para `Path.GetDirectoryName`, observando o diretório pai inteiro. `ComputeDirectoryHash` hasheia **todos** os arquivos nesse diretório.

**Impacto no usuário:**
- Falsos positivos se outros arquivos no mesmo diretório mudarem
- Backup pode incluir dados não relacionados ao save (se no mesmo diretório)

**Sugestão de correção:**
Hash apenas arquivos do `BackupSourceSet`; watcher com filtros mais específicos.

---

## BUG-010 — Colisão de versionId no mesmo segundo

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivos** | `BackupOrchestrator.cs`, `CloudBackupViewModel.cs` |
| **Classes / Métodos** | `CreateBackupAsync`, `BackupNowAsync` |

**Descrição técnica:**
`versionId = v{yyyyMMdd_HHmmss}` — resolução de 1 segundo. Backups concorrentes ou sequenciais no mesmo segundo sobrescrevem ZIP (`File.Copy overwrite: true`).

**Impacto no usuário:**
Perda silenciosa de versão de backup.

**Sugestão de correção:**
Adicionar sufixo GUID ou contador: `v{timestamp}_{guid:N}`.

---

## BUG-011 — Pasta de backup por nome do jogo, não AppID

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `PathHelper.cs` |
| **Classe / Método** | `PathHelper.BackupGameDir` |

**Descrição técnica:**
Diretório de backup usa `SanitizeName(game.Name)`. Jogos com nomes similares após sanitização podem colidir. Renomear jogo na Steam não migra backups.

**Impacto no usuário:**
Backups de jogos diferentes podem ir para mesma pasta; histórico perdido após rename.

**Sugestão de correção:**
Usar `{appId}_{sanitize(name)}` como chave de pasta.

---

## BUG-012 — SteamID32 incorreto em multi-conta

| Campo | Valor |
|-------|-------|
| **Severidade** | **Alta** (legado) → **Resolvido por decisão de arquitetura** (código pendente) |
| **Arquivos** | `SaveLocatorService.cs`, `BackupOrchestrator.cs`, `CloudBackupViewModel.cs`, `BackupDiscoveryService.cs` |
| **Classes / Métodos** | `GetSaveLocationsAsync(game, steamId32)` e cadeia de fallback de conta |
| **Decisão** | [DECISION_BACKUP_LUDUSAVI.md](DECISION_BACKUP_LUDUSAVI.md) |

**Descrição técnica (legado):**
Fallback chain: `OwnerAccount` → conta ativa → primeira conta. Paths com `<storeUserId>` resolvem para uma única conta.

**Alvo (Ludusavi):**
Remover `steamId32` do pipeline de backup. Expandir `<storeUserId>` com padrão `[0-9]+` no filesystem — incluir todos os paths existentes.

**Impacto no usuário (legado):**
Backup de saves vazios ou de outra conta; restauração futura para paths errados.

**Correção:**
Implementar LB-01/LB-02 — não exigir owner para backup; paridade com Ludusavi.

---

## BUG-013 — `async void` em event handlers críticos

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivos** | `BackupOrchestrator.cs`, `SaveWatcherService.cs` (via Task.Run) |
| **Classes / Métodos** | `OnSaveChanged`, `OnGameStateChanged` |

**Descrição técnica:**
Handlers `async void` engolem exceções após o primeiro await (apenas LogDebug no catch). Caller não pode aguardar conclusão.

**Impacto no usuário:**
Backup automático pode falhar silenciosamente sem feedback.

**Sugestão de correção:**
Usar `async Task` com fire-and-forget seguro (`_ = HandleAsync()` com try/catch global).

---

## BUG-014 — Edição de loginusers.vdf via regex

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `SteamAccountService.cs` |
| **Classe / Método** | `UpdateLoginUsersVdfAsync` |

**Descrição técnica:**
VDF é editado linha a linha com regex em vez de parse/serialize via ValveKeyValue. Variável `content` lida mas não usada; escrita via `ReadAllLines`/`WriteAllLines`.

**Impacto no usuário:**
Atualizações do formato VDF pela Valve podem quebrar troca de conta; risco de corrupção do arquivo de login.

**Sugestão de correção:**
Parse com ValveKeyValue, modificar objeto, re-serializar.

---

## BUG-015 — ForgetAccount não remove conta

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `SteamAccountService.cs` |
| **Classe / Método** | `ForgetAccount` |

**Descrição técnica:**
TODO no código — apenas copia VDF para `.bak`, não remove entrada.

**Impacto no usuário:**
Botão "esquecer conta" não funciona; conta reaparece ao recarregar.

**Sugestão de correção:**
Implementar remoção do bloco SteamID no VDF.

---

## BUG-016 — FileSystemWatcher para após 3 erros

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `SaveWatcherService.cs` |
| **Classe / Método** | `OnWatcherError` |

**Descrição técnica:**
Após `MaxWatcherRetries = 3`, erros são logados mas watcher não é mais re-registrado.

**Impacto no usuário:**
Detecção de mudanças para de funcionar permanentemente até reiniciar o app.

**Sugestão de correção:**
Fallback para polling periódico; reset de contador após sucesso.

---

## BUG-017 — Falhas de leitura silenciosas no hash

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivos** | `SaveSnapshotHasher.cs`, `SaveWatcherService.cs` |
| **Classes / Métodos** | `HashFile`, `ComputeDirectoryHash` |

**Descrição técnica:**
Arquivos ilegíveis retornam `"unreadable"` ou são ignorados no catch vazio. Hash resultante pode marcar como "sem mudança" ou mudança falsa.

**Impacto no usuário:**
Backup incompleto sem aviso; usuário acredita que save está protegido.

**Sugestão de correção:**
Registrar arquivos falhos; marcar backup como parcial; alertar na UI.

---

## BUG-018 — ExtractBackupZipAsync extrai tudo para primeiro target

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `LocalFolderProvider.cs` |
| **Classe / Método** | `ExtractBackupZipAsync` |

**Descrição técnica:**
Método legado extrai todas as entradas do ZIP para `destRoot` derivado do primeiro path em `targetPaths`, ignorando estrutura `files/{n}/content/`.

**Impacto no usuário:**
Se usado para restore, corromperia layout de arquivos. Atualmente não chamado pelo restore (que está stub).

**Sugestão de correção:**
Remover ou reescrever para novo formato com `sources.json`.

---

## BUG-019 — Manifest Ludusavi nunca atualizado após cache inicial

| Campo | Valor |
|-------|-------|
| **Severidade** | **Média** |
| **Arquivo** | `LudusaviManifestService.cs` |
| **Classe / Método** | `LoadAsync`, `RefreshAsync` |

**Descrição técnica:**
`RefreshAsync` só é chamado se cache não existe. Não há TTL, verificação de versão ou refresh em settings.

**Impacto no usuário:**
Novos jogos ou paths corrigidos no Ludusavi não aparecem até deletar cache manualmente.

**Sugestão de correção:**
Refresh periódico ou botão em Settings; ETag/If-Modified-Since.

---

## BUG-020 — Watchdog não usado em troca via CLI/tray/game launch

| Campo | Valor |
|-------|-------|
| **Severidade** | **Baixa** |
| **Arquivos** | `App.xaml.cs`, `MainViewModel.cs`, `SteamGameService.cs` |
| **Classes / Métodos** | `HandleCliSwitchAsync`, `SwitchFromTrayAsync`, `LaunchGameAsync` |

**Descrição técnica:**
`BeginSwitch`/`EndSwitch` só em `AccountsViewModel.SwitchAccountAsync`.

**Impacto no usuário:**
Crash durante troca por CLI/tray não é detectado pelo watchdog.

**Sugestão de correção:**
Centralizar troca com wrapper que sempre usa watchdog.

---

## BUG-021 — BackupFilter.CloudOnly não implementado

| Campo | Valor |
|-------|-------|
| **Severidade** | **Baixa** |
| **Arquivos** | `BackupFilter.cs`, `CloudBackupViewModel.cs` |
| **Classes / Métodos** | `ApplyFilter` |

**Descrição técnica:**
Enum `CloudOnly` existe; `CloudOnlyGame` model existe; filtro não tratado no switch.

**Impacto no usuário:**
Funcionalidade prometida no modelo não disponível.

---

## BUG-022 — BackupVersion.Files nunca populado

| Campo | Valor |
|-------|-------|
| **Severidade** | **Baixa** |
| **Arquivos** | `BackupOrchestrator.cs`, `CloudBackupViewModel.cs` |
| **Classes / Métodos** | `AddVersionAsync` calls |

**Descrição técnica:**
Campo `Files` com `RelativePath` e `Sha256` por arquivo definido no modelo mas sempre lista vazia.

**Impacto no usuário:**
Sem verificação granular de integridade; sem restore seletivo futuro.

---

## BUG-023 — Crash handler escreve em C:\crash.txt

| Campo | Valor |
|-------|-------|
| **Severidade** | **Baixa** |
| **Arquivo** | `App.xaml.cs` |
| **Classe / Método** | `OnStartup` catch block |

**Descrição técnica:**
Caminho hardcoded `C:\crash.txt` pode falhar sem permissão; não é configurável.

**Impacto no usuário:**
Debug de crash pode falhar em ambientes restritos.

---

## Resumo por Severidade

| Severidade | Quantidade |
|------------|------------|
| Crítica | 2 |
| Alta | 5 |
| Média | 12 |
| Baixa | 4 |

### Top 5 prioridades de correção

1. **BUG-001** — Unificar algoritmo de hash
2. **BUG-002** — Implementar restore
3. **BUG-004** — Lock de backup concorrente
4. **BUG-005** — Sincronizar manifest com versioning policy
5. **BUG-006** — Bloquear backup com jogo em execução
