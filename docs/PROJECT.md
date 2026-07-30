# SteamSwitcher — Documentação do Projeto

## Informações Gerais

| Item | Valor |
|------|-------|
| Nome | SteamSwitcher |
| Plataforma | Windows (.NET 10, WPF) |
| Tipo | Aplicativo desktop (WinExe) |
| Repositório local | `c:\PROJECTS\SteamSwitcher` |
| Solução | `SteamSwitcher.sln` |

---

## Projetos

### 1. SteamSwitcher (UI)

**Caminho:** `SteamSwitcher/`
**Target:** `net10.0-windows`
**Output:** WinExe WPF

Responsável por toda interface, ViewModels, temas, onboarding e integração WPF-UI.

**Arquivos principais:**

| Arquivo | Função |
|---------|--------|
| `App.xaml.cs` | Entry point, DI host, tema, CLI |
| `MainWindow.xaml.cs` | Shell, navegação, system tray |
| `ViewModels/*.cs` | Lógica de apresentação |
| `Views/Pages/*.xaml` | Páginas principais |
| `Views/Onboarding/` | Wizard primeira execução |
| `Themes/Light.xaml`, `Dark.xaml` | Paleta customizada |

### 2. SteamSwitcher.Core (Domínio)

**Caminho:** `SteamSwitcher.Core/`
**Target:** `net10.0-windows` (class library)

Contém toda lógica reutilizável: Steam, backup, Ludusavi, sistema.

**Namespaces:**

```
SteamSwitcher.Core
├── Extensions/          → ServiceCollectionExtensions
├── Helpers/             → AtomicFile, PathHelper, ZipExtractHelper
├── Models/              → AppSettings, SteamAccount, SteamGame, ...
└── Services/
    ├── Steam/           → Contas, jogos, saves, processos
    ├── Backup/          → Backup, restore, Ludusavi
    ├── Cache/           → ImageCacheService
    ├── System/          → Mutex, watchdog, mods
    └── Onboarding/      → Primeira execução
```

### 3. SteamSwitcher.Tests

**Caminho:** `SteamSwitcher.Tests/`
**Estado:** Scaffold apenas — referencia xUnit, FluentAssertions, NSubstitute e Core, mas **não contém arquivos de teste**.

---

## Mapa de Arquivos por Funcionalidade

### Troca de Contas

| Arquivo | Classe |
|---------|--------|
| `Services/Steam/SteamAccountService.cs` | `SteamAccountService` |
| `Services/Steam/SteamLocatorService.cs` | `SteamLocatorService` |
| `Services/Steam/AccountOverrideService.cs` | `AccountOverrideService` |
| `Services/System/WatchdogService.cs` | `WatchdogService` |
| `ViewModels/AccountsViewModel.cs` | `AccountsViewModel` |
| `Models/SteamAccount.cs` | `SteamAccount` |
| `Models/LoginState.cs` | `LoginState` |

### Jogos

| Arquivo | Classe |
|---------|--------|
| `Services/Steam/SteamGameServices.cs` | `SteamGameService` |
| `Services/Steam/GameProcessService.cs` | `GameProcessService` |
| `Services/Steam/AchievementService.cs` | `AchievementService` |
| `ViewModels/GamesViewModel.cs` | `GamesViewModel` |
| `Models/SteamGame.cs` | `SteamGame` |

### Backup / Restore

| Arquivo | Classe |
|---------|--------|
| `Services/Backup/BackupOrchestrator.cs` | `BackupOrchestrator` |
| `Services/Backup/SaveWatcherService.cs` | `SaveWatcherService` |
| `Services/Backup/SaveSnapshotHasher.cs` | `SaveSnapshotHasher` |
| `Services/Backup/BackupStateService.cs` | `BackupStateService` |
| `Services/Backup/LocalFolderProvider.cs` | `LocalFolderProvider` |
| `Services/Backup/BackupManifestService.cs` | `BackupManifestService` |
| `Services/Backup/SmartSizeVersioningPolicy.cs` | `SmartSizeVersioningPolicy` |
| `Services/Backup/RestoreService.cs` | `RestoreService` |
| `Services/Backup/BackupDiscoveryService.cs` | `BackupDiscoveryService` |
| `Services/Backup/RegistryBackupHelper.cs` | `RegistryBackupHelper` |
| `Services/Backup/BackupSources.cs` | DTOs de save/backup |
| `ViewModels/CloudBackupViewModel.cs` | `CloudBackupViewModel` |

### Ludusavi

| Arquivo | Classe |
|---------|--------|
| `Services/Backup/LudusaviManifestService.cs` | `LudusaviManifestService` |
| `Services/Backup/ILudusaviManifestService.cs` | Interfaces + DTOs Ludusavi |
| `Services/Steam/SaveLocatorService.cs` | `SaveLocatorService` |

### Configuração e Sistema

| Arquivo | Classe |
|---------|--------|
| `Services/AppSettingsService.cs` | `AppSettingsService` |
| `Models/AppSettings.cs` | `AppSettings` |
| `Services/System/SystemService.cs` | `SystemService` |
| `Services/System/ModMonitorService.cs` | `ModMonitorService` |
| `ViewModels/SettingsViewModel.cs` | `SettingsViewModel` |

---

## Configuração do Usuário (`AppSettings`)

Arquivo: `%LocalAppData%\SteamSwitcher\settings.json`

```json
{
  "theme": "System | Light | Dark",
  "useGridView": true,
  "afterAccountSwitch": "MinimizeToTray | Close | KeepOpen",
  "afterGameLaunch": "MinimizeToTray | Close | KeepOpen",
  "defaultLoginState": "Offline | Online | ...",
  "startSilent": true,
  "startAsAdmin": false,
  "steamApiKey": null,
  "steamInstallPath": null,
  "avatarCacheExpiryDays": 7,
  "coverCacheExpiryDays": 30,
  "steamGridDbApiKey": null,
  "autoBackupEnabled": true,
  "backupOnGameClose": true
}
```

---

## Argumentos de Linha de Comando

| Argumento | Efeito |
|-----------|--------|
| `--minimized` | Abre sem exibir janela principal |
| `--switch <steamid64>` | Troca conta e encerra app |
| `--state online\|offline\|invisible\|away` | Estado de login na troca CLI |

---

## Diretórios de Dados em Runtime

```
%LocalAppData%\SteamSwitcher\
├── settings.json
├── backup_state.json
├── overrides.json
├── game_owners.json
├── playtime_baseline.json
├── watchdog.json
├── onboarding.json
├── mods_cache.json
├── ludusavi_manifest.yaml
├── cache\                    # imagens
├── meta\                     # metadados de cache
└── backups\
    └── {NomeDoJogo}\
        ├── manifest.json
        └── v{timestamp}.zip
```

---

## Arquivos Steam Modificados (externos ao app)

| Arquivo / Chave | Operação |
|-----------------|----------|
| `{Steam}\config\loginusers.vdf` | Leitura + edição regex na troca |
| `{Steam}\config\loginusers.vdf_last` | Backup antes de editar |
| `HKCU\Software\Valve\Steam\AutoLoginUser` | Escrita na troca |
| `HKCU\Software\Valve\Steam\RememberPassword` | Escrita na troca |

---

## Páginas da UI

### AccountsPage
- Lista contas do `loginusers.vdf`
- Troca de conta com feedback otimista
- Edição de override (nome, avatar, estado)
- Adicionar conta (abre login Steam)
- Esquecer conta (stub)
- FileSystemWatcher no VDF para refresh automático

### GamesPage
- Jogos instalados de todas as bibliotecas Steam
- Filtro por conta, busca por nome
- Associação manual jogo→conta
- Lançamento com troca de conta automática
- Indicador de jogo em execução
- Exibição de caminhos de save (Ludusavi)

### CloudBackupPage
- Lista jogos com saves existentes e suporte Ludusavi
- Status: sincronizado / pendente / sem backup
- Backup manual, histórico de versões
- Pin, label, delete de versões
- Restaurar (chama `RestoreService` — não funcional)
- Abrir pasta de backups no Explorer

### ModsPage
- Monitora `plugins`, `millennium`, `skins` do Steam
- Cache em `mods_cache.json`

### SettingsPage
- Tema, comportamento pós-troca, backup automático
- API keys (Steam, SteamGridDB)
- Cache, iniciar com Windows

### Onboarding
- Steps 0-5: tema, avisos, instalar Steam, import TcNo (stub), conclusão

---

## Roadmap Implícito no Código

Funcionalidades com estrutura preparada mas incompletas:

1. **Restauração** com `sources.json` (mensagem explícita no código)
2. **Cloud sync** via `ICloudBackupProvider`
3. **Jogos cloud-only** (`CloudOnlyGame`, `BackupFilter.CloudOnly`)
4. **Backup diferencial** (campos no modelo `BackupVersion`)
5. **Import TcNo** completo
6. **Forget/Restore account** no VDF
7. **Safety backup** antes de restore
8. **Registry restore** (export existe, import não)

---

## Como Buildar

```powershell
cd c:\PROJECTS\SteamSwitcher
dotnet build SteamSwitcher.sln
```

Output: `SteamSwitcher\bin\Debug\net10.0-windows\SteamSwitcher.exe`

---

## Convenções de Código Observadas

- Primary constructors (C# 12+) em serviços e ViewModels
- `[RelayCommand]` e `[ObservableProperty]` do CommunityToolkit.Mvvm
- Logging via `ILogger<T>` com nível Debug para erros operacionais
- `async void` em event handlers (`OnSaveChanged`, `OnGameStateChanged`)
- Catch vazio ou LogDebug em muitos blocos de I/O
- Comentários em português no código-fonte
