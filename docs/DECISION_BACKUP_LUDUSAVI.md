# Decisão de Arquitetura — Backup igual ao Ludusavi

**Status:** Aprovado  
**Data:** 2025-06-22  
**Substitui:** modelo atual baseado em `SteamID32` único por backup

---

## Decisão

O backup de saves **não será vinculado a uma conta Steam específica**. O comportamento deve espelhar o **Ludusavi**, não o modelo atual do SteamSwitcher.

### Princípio

> O backup é **do jogo nesta máquina**, incluindo **todas as fontes de save que existem no disco** conforme o manifest Ludusavi — independente de qual conta está ativa, associada ou logada.

---

## Como o Ludusavi trata `<storeUserId>` (Steam)

No Ludusavi, para roots Steam, o placeholder `<storeUserId>` **não** é substituído pelo ID do usuário ativo. O path é expandido no filesystem usando padrão **`[0-9]*`** (pastas com nomes só de dígitos).

**Referência:** [ludusavi issue #39](https://github.com/mtkennerly/ludusavi/issues/39) — *"Steam roots match `storeUserId` with `[0-9]*` (digits-only)"*.

### Exemplo

Manifest:
```
<root>/userdata/<storeUserId>/760/remote/<storeGameId>/
```

**Comportamento Ludusavi (e alvo do SteamSwitcher):**
```
C:\Steam\userdata\12345678\760\remote\1245620\   ← existe → incluir
C:\Steam\userdata\87654321\760\remote\1245620\   ← existe → incluir
C:\Steam\userdata\99999999\760\remote\1245620\   ← não existe → ignorar
```

**Comportamento atual do SteamSwitcher (a remover):**
```
Substitui <storeUserId> por UM steamId32 (owner → ativa → primeira conta)
→ só um path; demais contas ignoradas
```

---

## Especificação alvo

### 1. Resolução de paths (`SaveLocatorService`)

| Tipo de path | Resolução |
|--------------|-----------|
| Sem `<storeUserId>` | Placeholders Windows/Steam como hoje (`<winAppData>`, `<base>`, etc.) |
| Com `<storeUserId>` | **Expansão por glob `[0-9]+`** na posição do placeholder — todas as pastas numéricas existentes |
| Globs `*`, `?`, `**` | Mantidos como hoje |
| Constraints `when` | Mantidos como hoje (`os: windows`, `store: steam`) |
| Tag `save` | Mantida — só entradas com tag save |

### 2. O que entra no backup

- Todos os `ResolvedSaveFile` / `ResolvedSaveRegistry` com `Exists == true` após expansão Ludusavi
- **Sem** filtro por `OwnerAccount`, conta ativa ou `loginusers.vdf`
- Deduplicação de paths nested (lógica existente)

### 3. O que NÃO muda

| Área | Comportamento |
|------|---------------|
| Troca de conta | Continua usando SteamID64 / VDF / registry |
| Lançar jogo | Continua exigindo associação jogo→conta (`OwnerAccount`) |
| Estrutura de backup | Uma pasta por jogo: `backups/{NomeJogo}/` |
| Formato ZIP | `sources.json` + `files/{n}/` + `registry/{n}/` |
| Restore (futuro) | Cada arquivo volta ao `resolvedPath` gravado no backup |

### 4. API alvo (implementação futura)

```csharp
// Remover parâmetro steamId32 — resolução não depende de conta
Task<SaveLocationResult> GetSaveLocationsAsync(
    SteamGame game,
    CancellationToken ct = default);
```

Chamadores a atualizar: `BackupOrchestrator`, `BackupDiscoveryService`, `CloudBackupViewModel`, `GamesViewModel` (apenas exibição de paths — não backup).

### 5. Watcher e hash

- Registrar watchers em **todos** os diretórios pai dos paths resolvidos (todas as contas)
- Hash unificado via `SaveSnapshotHasher` sobre o `BackupSourceSet` completo
- Mudança em qualquer conta → `HasUnbackedChanges = true`

---

## Regras de negócio (novas)

| ID | Regra |
|----|-------|
| **LB-01** | Backup não usa `SteamID32` nem `OwnerAccount` |
| **LB-02** | `<storeUserId>` expande para todas as pastas `[0-9]+` existentes (Steam) |
| **LB-03** | Incluir no backup apenas paths com `Exists == true` após expansão |
| **LB-04** | Um backup por jogo na máquina, não por conta |
| **LB-05** | `sources.json` guarda `resolvedPath` absoluto — restore é path-driven |
| **LB-06** | Associação jogo→conta é exclusiva do fluxo **Jogar** |

---

## Trade-offs aceitos (como no Ludusavi)

| Trade-off | Justificativa |
|-----------|---------------|
| Backups maiores com várias contas | Completude > tamanho; igual Ludusavi |
| Saves de contas não logadas também entram | Disco é fonte da verdade, não sessão Steam |
| Possível inclusão de pastas numéricas irrelevantes | Ludusavi aceita isso; raro em paths bem definidos do manifest |
| Mais diretórios no watcher | Necessário para detecção correta multi-conta |

---

## Fora de escopo desta decisão

- Implementação de código (ainda não iniciada)
- Unificação do algoritmo de hash (BUG-001) — recomendado fazer junto
- Backup por conta em pastas separadas
- Refresh automático do manifest Ludusavi

---

## Impacto em bugs documentados

| Bug | Após esta decisão |
|-----|-------------------|
| BUG-012 (SteamID32 incorreto) | **Resolvido por design** — parâmetro removido do pipeline de backup |
| R12–R15 (regras multi-conta antigas) | **Substituídas** por LB-01 a LB-06 |

---

## Checklist de implementação (futuro)

1. [ ] `SaveLocatorService`: expansão `<storeUserId>` → `[0-9]+`
2. [ ] Remover `steamId32` de `ISaveLocatorService` e todos os callers de backup
3. [ ] `GamesViewModel`: manter `steamId32` só se necessário para UI (opcional — paths exibidos serão o resultado da expansão Ludusavi)
4. [ ] Re-scan de watchers após mudança em `loginusers.vdf` (opcional — expansão por filesystem já cobre novas pastas userdata)
5. [ ] Atualizar testes quando existirem
6. [ ] Unificar hash watcher/snapshot (BUG-001)
