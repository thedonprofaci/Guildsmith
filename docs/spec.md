# Discord MCP Server (.NET) – Specifikáció

> Verzió: 0.1 (draft) · Dátum: 2026-10-26 · Státusz: fejlesztés indítására kész

## 1. Cél

Egy nyílt forráskódú, C#/.NET alapú **MCP (Model Context Protocol) szerver**, amelyen keresztül Claude (Desktop, Code, illetve remote connectorként web/mobil) természetes nyelven kezelhet egy Discord szervert: csatornákat, szerepköröket, jogosultságokat és üzeneteket.

**Differenciáló pont:** natív .NET implementáció (a meglévő Discord MCP szerverek többsége Python/TypeScript), biztonság-központú alapbeállításokkal.

### 1.1 Nem célok (v1)
- Discord bot üzemeltetése általános célra (parancsok, eventek, moderációs automatika)
- Hangcsatorna-kezelés, stream, voice
- Több tenant / SaaS hostolás mások számára
- Saját UI

---

## 2. Fázisok

| Fázis | Tartalom | Transport |
|---|---|---|
| **1. MVP** | Csatorna + szerepkör + üzenet tool-ok, biztonsági kapcsolók, README | stdio |
| **2. Csomagolás** | `dotnet tool`, self-contained binárisok, `.mcpb` Desktop Extension | stdio |
| **3. Remote** | Streamable HTTP, OAuth 2.1, Docker, reverse proxy minta | HTTP |

---

## 3. Tech stack

| Terület | Választás |
|---|---|
| Runtime | .NET 8 LTS (vagy .NET 9) |
| Host | Generic Host (`Microsoft.Extensions.Hosting`) |
| MCP | `ModelContextProtocol` NuGet (stdio), `ModelContextProtocol.AspNetCore` (HTTP, 3. fázis) |
| Discord | `Discord.Net` (REST kliens elég, gateway csak ha kell) |
| Konfiguráció | `Microsoft.Extensions.Configuration` (env var + user secrets) |
| Logolás | `Microsoft.Extensions.Logging` → **stderr** (stdio módban a stdout a protokollé!) |
| Teszt | xUnit + NSubstitute/Moq, MCP Inspector |
| CI | GitHub Actions |

> ⚠️ Az MCP C# SDK még preview/gyorsan változó lehet – a pontos API-t (attribútumok, regisztráció) a hivatalos repóból ellenőrizni kell.

---

## 4. Architektúra

```
Claude (Desktop / Code / Web)
        │  MCP (stdio | Streamable HTTP)
        ▼
┌───────────────────────────────┐
│ DiscordMcp.Server             │  Host, transport, DI, config
│  ├─ Tools/                    │  MCP tool osztályok (vékony réteg)
│  ├─ Guards/                   │  Guild allowlist, read-only, destructive gate
│  └─ Services/                 │  Discord műveletek (üzleti logika)
│        ▼                      │
│  IDiscordGateway              │  Absztrakció a Discord.Net fölött (mockolható)
└───────────────────────────────┘
        │  Discord REST API (bot token)
        ▼
     Discord
```

### 4.1 Projektstruktúra

```
discord-mcp-dotnet/
├─ src/
│  ├─ DiscordMcp.Core/          # Services, Guards, modellek, IDiscordGateway
│  ├─ DiscordMcp.Discord/       # Discord.Net alapú IDiscordGateway implementáció
│  └─ DiscordMcp.Server/        # Program.cs, Tools, transport választás
├─ tests/
│  └─ DiscordMcp.Tests/
├─ packaging/
│  ├─ mcpb/                     # manifest.json + ikon (2. fázis)
│  └─ docker/                   # Dockerfile, docker-compose.yml, Caddyfile (3. fázis)
├─ docs/
│  ├─ discord-bot-setup.md
│  └─ tools.md
├─ .github/workflows/ci.yml
├─ .env.example
├─ .gitignore
├─ README.md
├─ SECURITY.md
├─ CONTRIBUTING.md
└─ LICENSE (MIT)
```

---

## 5. Konfiguráció

Minden beállítás env varból jön (fejlesztéshez user secrets). Fájlba írt token tilos.

| Változó | Kötelező | Alapérték | Leírás |
|---|---|---|---|
| `DISCORD_TOKEN` | ✅ | – | Bot token |
| `ALLOWED_GUILD_IDS` | ✅ | – | Vesszővel elválasztott guild ID-k. Üres → a szerver nem indul el |
| `DISCORD_MCP_MODE` | | `ReadWrite` | `ReadOnly` / `ReadWrite` |
| `ALLOW_DESTRUCTIVE` | | `false` | Törlés, jogmódosítás, kick/ban engedélyezése |
| `MAX_MESSAGES_PER_READ` | | `50` | Üzenetolvasás felső korlátja (max 100) |
| `MCP_TRANSPORT` | | `stdio` | `stdio` / `http` (3. fázis) |
| `LOG_LEVEL` | | `Information` | |

**Indulási validáció:** hiányzó token vagy üres allowlist → értelmes hibaüzenet stderr-re, exit code ≠ 0.

---

## 6. Tool-ok

### 6.1 Konvenciók
- Név: `snake_case`, igei kezdet (`create_channel`)
- Minden tool első paramétere a `guild_id` (allowlisten ellenőrizve). Ha csak egy guild engedélyezett, opcionális.
- ID-k stringként (Discord snowflake > `long` JS-oldali pontosság miatt)
- Válasz: tömör JSON (id, név, típus) – ne teljes Discord objektum
- Tool leírás: rövid, egyértelmű, jelzi ha destruktív
- MCP annotációk: `readOnlyHint`, `destructiveHint`, `idempotentHint` kitöltése

### 6.2 Jelölések
- 🟢 Read – `ReadOnly` módban is elérhető
- 🟡 Write – `ReadWrite` mód kell
- 🔴 Destructive – `ReadWrite` + `ALLOW_DESTRUCTIVE=true` kell

### 6.3 Szerver
| Tool | Szint | Paraméterek | Leírás |
|---|---|---|---|
| `list_guilds` | 🟢 | – | Engedélyezett guildek, amelyekben a bot tag |
| `get_guild_info` | 🟢 | guild_id | Név, tagszám, csatorna/szerepkör szám |

### 6.4 Csatornák
| Tool | Szint | Paraméterek |
|---|---|---|
| `list_channels` | 🟢 | guild_id, type? |
| `create_channel` | 🟡 | guild_id, name, type (`text`/`voice`/`category`/`announcement`/`forum`), parent_id?, topic?, position? |
| `edit_channel` | 🟡 | channel_id, name?, topic?, parent_id?, position?, slowmode_seconds?, nsfw? |
| `delete_channel` | 🔴 | channel_id |

### 6.5 Szerepkörök és jogosultságok
| Tool | Szint | Paraméterek |
|---|---|---|
| `list_roles` | 🟢 | guild_id |
| `create_role` | 🟡 | guild_id, name, color?, permissions?, hoist?, mentionable? |
| `edit_role` | 🔴 | role_id, name?, color?, permissions? |
| `delete_role` | 🔴 | role_id |
| `assign_role` / `remove_role` | 🔴 | guild_id, user_id, role_id |
| `get_channel_permissions` | 🟢 | channel_id |
| `set_channel_permissions` | 🔴 | channel_id, target_id, target_type (`role`/`member`), allow[], deny[] |
| `clear_channel_permissions` | 🔴 | channel_id, target_id |

**Jogosultságok formátuma:** emberi nevek listája (`["ViewChannel","SendMessages"]`), nem bitmask. A szerver ismeretlen nevet elutasít, és visszaadja az érvényes listát.

### 6.6 Üzenetek
| Tool | Szint | Paraméterek |
|---|---|---|
| `read_messages` | 🟢 | channel_id, limit?, before_id? |
| `send_message` | 🟡 | channel_id, content, reply_to_id? |
| `edit_message` | 🟡 | channel_id, message_id (csak a bot saját üzenete) |
| `delete_message` | 🔴 | channel_id, message_id |
| `pin_message` / `unpin_message` | 🟡 | channel_id, message_id |
| `add_reaction` | 🟡 | channel_id, message_id, emoji |

### 6.7 Tagok (opcionális, v1.1)
| Tool | Szint |
|---|---|
| `list_members` / `get_member` | 🟢 (Server Members Intent kell) |
| `timeout_member` / `kick_member` / `ban_member` | 🔴 |

---

## 7. Biztonsági követelmények

1. **Token védelem:** soha nem kerül logba, hibaüzenetbe, tool válaszba. Exception üzenetek szűrése.
2. **Guild allowlist:** minden tool hívás előtt guard; a csatorna/szerepkör/üzenet ID-ből feloldott guild is ellenőrizve (ne lehessen más guild csatorna ID-jával megkerülni).
3. **Defense in depth:** read-only mód és destructive gate szerveroldalon, nem csak a kliens jóváhagyására építve.
4. **Hierarchia védelem:** a bot nem adhat ki magánál magasabb vagy vele egyenlő szerepkört; `Administrator` jog kiosztása csak külön flaggel (`ALLOW_ADMIN_GRANT`, alapból tiltva).
5. **Prompt injection:** a Discord üzenetek tartalma nem megbízható input. `read_messages` válaszban az üzenettartalom egyértelműen adatként jelölve (pl. `"untrusted_content"` mező). README-ben figyelmeztetés.
6. **Rate limit:** Discord.Net beépített kezelése + értelmes hibaüzenet a modellnek (`retry_after`).
7. **Minimális bot jogok:** a README az OAuth URL-t csak a szükséges jogokkal adja meg, `Administrator` nélkül.
8. **Audit log:** minden write/destructive művelet logolva (stderr / fájl), és Discord audit log `reason` mezőben jelölve (`"via Discord MCP"`).

---

## 8. Hibakezelés

Egységes hibaválasz a modell felé:

```json
{ "error": "forbidden_guild", "message": "Guild 123 is not in ALLOWED_GUILD_IDS." }
```

| Kód | Eset |
|---|---|
| `forbidden_guild` | Guild nincs az allowlisten |
| `read_only_mode` | Write tool ReadOnly módban |
| `destructive_disabled` | 🔴 tool `ALLOW_DESTRUCTIVE=false` mellett |
| `not_found` | Csatorna/szerepkör/üzenet nem létezik |
| `missing_permission` | A botnak nincs Discord joga (melyik hiányzik) |
| `role_hierarchy` | Szerepkör a bot fölött van |
| `rate_limited` | `retry_after` másodperccel |
| `invalid_argument` | Validációs hiba, érvényes értékekkel |

---

## 9. Tesztelés

- **Unit:** Services + Guards, `IDiscordGateway` mockolva. Kötelező esetek: allowlist megkerülési kísérlet, read-only, destructive gate, hierarchia, token nem szivárog hibaüzenetben.
- **Integrációs (manuális / opcionális CI secret-tel):** dedikált teszt Discord szerver.
- **Protokoll:** MCP Inspector (`npx @modelcontextprotocol/inspector`) – tool lista, sémák, annotációk.
- **E2E:** Claude Desktop config-gal, forgatókönyv-lista (lásd 12.).

---

## 10. CI/CD

- **CI (PR + main):** restore, build, test, `dotnet format --verify-no-changes`
- **Release (tag `v*`):**
  - NuGet `dotnet tool` csomag
  - Self-contained binárisok: `win-x64`, `linux-x64`, `osx-arm64`
  - `.mcpb` csomag (2. fázis)
  - Docker image GHCR-re (3. fázis)
- Dependabot + CodeQL bekapcsolva
- Secret scanning + push protection a repón

---

## 11. Dokumentáció (README vázlat)

1. Mi ez, mit tud (tool lista röviden)
2. Biztonsági figyelmeztetés (token, prompt injection, destructive mód)
3. Discord bot létrehozása lépésről lépésre (Developer Portal, intents, OAuth URL)
4. Telepítés: `dotnet tool install`, bináris, `.mcpb`
5. Claude Desktop / Claude Code konfiguráció példa
6. Konfigurációs változók táblázata
7. Példa promptok
8. Remote hostolás (3. fázis, külön doc)
9. Contributing, license

---

## 12. Elfogadási kritériumok (MVP)

- [ ] Claude Desktopban megjelennek a tool-ok, MCP Inspector hiba nélkül listázza
- [ ] „Hozz létre egy *Projektek* kategóriát, alatta #general és #dev csatornával” → működik
- [ ] „Hozz létre Moderator szerepkört, és a #announcements-be csak ő írhasson” → működik (destructive engedélyezve)
- [ ] `ALLOW_DESTRUCTIVE=false` mellett törlés → `destructive_disabled` hiba
- [ ] `ReadOnly` módban csak 🟢 tool-ok futnak
- [ ] Nem engedélyezett guild csatorna ID-jával hívás → `forbidden_guild`
- [ ] Token sehol nem jelenik meg logban/hibában (teszttel lefedve)
- [ ] README alapján egy új felhasználó 15 percen belül működésre bírja
- [ ] CI zöld, release workflow bináris + NuGet csomagot készít

---

## 13. 3. fázis – Remote (vázlat)

- `MCP_TRANSPORT=http` → `ModelContextProtocol.AspNetCore`, Streamable HTTP, `/mcp` végpont
- **OAuth 2.1** az MCP authorization spec szerint (külső IdP: Entra ID / Keycloak / Auth0); auth nélküli indítás csak `localhost`-on engedélyezett
- Docker + `docker-compose.yml` + Caddy (automatikus Let's Encrypt)
- Health check végpont, strukturált log
- Claude-ban: *Settings → Connectors → Add custom connector*
- Mindenki a saját példányát hostolja (a token a saját szerverén marad)

---

## 14. Nyitott kérdések

- [ ] Projekt név: `discord-mcp-dotnet` vagy más?
- [ ] .NET 8 LTS vagy .NET 9/10?
- [ ] Discord.Net vagy DSharpPlus? (javaslat: Discord.Net)
- [ ] Kell-e gateway kapcsolat (pl. valós idejű események), vagy REST elég? (javaslat: v1-ben REST)
- [ ] Tag-kezelő tool-ok bekerülnek-e az MVP-be?
- [ ] Létezik-e már érett C# Discord MCP szerver? (kezdés előtt ellenőrizni)
