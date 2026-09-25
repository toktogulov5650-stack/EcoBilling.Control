# EcoBilling.Control

EcoBilling.Control — отдельный центральный backend системы EcoBilling.

Для приложения он принимает код округа и возвращает адрес соответствующего `EcoBilling.Api`. Для системного администратора он управляет реестром округов и запускает создание первого директора через защищённый внутренний API округа.

## Проекты

- `EcoBilling.Control.Api` — публичные и административные HTTP endpoints, точка сборки зависимостей.
- `EcoBilling.Control.Application` — сценарии использования (CQRS-lite: команды/запросы и их обработчики) и интерфейсы.
- `EcoBilling.Control.Domain` — центральные бизнес-правила и сущности; ни от кого не зависит.
- `EcoBilling.Control.Infrastructure` — PostgreSQL (EF Core), аутентификация, кеширование, аудит и клиент округа.
- `EcoBilling.Control.Provisioning` — отдельная консольная команда: единственный способ создать первого системного администратора ([ADR-0007](docs/adr/0007-first-administrator-provisioning.md)).

## Зависимости

```text
Api ───────────→ Application ───────────→ Domain
 │                       ↑
 └────────→ Infrastructure ─────────────┘
```

## Предварительные требования

- .NET 10 SDK.
- Docker + Docker Compose (для PostgreSQL локально и для контейнерного образа `deploy/Dockerfile.api`).
- `dotnet-ef` (глобально: `dotnet tool install --global dotnet-ef`) — только для применения миграций вручную; не требуется для `dotnet build`/`dotnet test`.

## Быстрый старт (Docker Compose)

```powershell
docker compose -f deploy/compose.yml up --build
```

Поднимает PostgreSQL и API в контейнерах. API слушает `http://localhost:8081`, использует `ASPNETCORE_ENVIRONMENT=Development`
внутри контейнера (см. `deploy/compose.yml`) — секреты разработки (JWT signing key, service-assertion PEM) в этом
режиме берутся из `appsettings.Development.json`, уже входящего в образ; для Production все секреты обязаны
приходить через переменные окружения (ниже), а не через закоммиченный файл.

Миграции **не применяются автоматически при старте** ни в контейнере, ни при обычном запуске — примените их
вручную (см. ниже) один раз после первого поднятия свежей базы и заново после каждого добавления новой миграции.

## Локальная разработка без Docker для самого API

```powershell
# Только PostgreSQL в контейнере -- API запускается на хосте напрямую dotnet run.
docker compose -f deploy/compose.yml up postgres -d

dotnet restore EcoBilling.Control.slnx
dotnet ef database update --project src/EcoBilling.Control.Infrastructure --startup-project src/EcoBilling.Control.Api

dotnet run --project src/EcoBilling.Control.Api/EcoBilling.Control.Api.csproj
```

`appsettings.Development.json` уже содержит рабочую строку подключения (`localhost:5433`, порт, который
`deploy/compose.yml` публикует наружу для сервиса `postgres`) и dev-only секреты — никаких дополнительных
переменных окружения для локальной разработки не требуется.

## Применение миграций

```powershell
dotnet ef database update --project src/EcoBilling.Control.Infrastructure --startup-project src/EcoBilling.Control.Api
```

Миграции физически лежат в `EcoBilling.Control.Infrastructure`; `--startup-project` указывает на `Api`, откуда
берётся конфигурация (строка подключения). Требуется переменная `ConnectionStrings__Database`, если не
используется `appsettings.Development.json` по умолчанию (см. таблицу переменных ниже).

## Создание первого системного администратора

Единственный способ создать системного администратора — нет ни публичной регистрации, ни HTTP endpoint для
этого ([ADR-0007](docs/adr/0007-first-administrator-provisioning.md)):

```powershell
$env:ConnectionStrings__Database = "Host=localhost;Port=5433;Database=ecobilling_control;Username=ecobilling_control;Password=ecobilling_control_dev"
dotnet run --project src/EcoBilling.Control.Provisioning -- admin@example.com "Full Name"
```

Пароль запрашивается интерактивно, с маскированным вводом — никогда не передаётся как аргумент командной строки.

## Переменные окружения

| Переменная | Обязательна | По умолчанию | Назначение |
|---|---|---|---|
| `ConnectionStrings__Database` | Да (Api и Provisioning) | — (только `appsettings.Development.json`) | Строка подключения к PostgreSQL. Никогда не коммитится реальное значение. |
| `Authentication__Jwt__SigningKey` | Да | — (только dev) | Симметричный ключ HMAC-SHA256 для access/refresh токенов администратора. Минимум 32 байта. |
| `ServiceAuth__Jwt__SigningKeyPem` | Да | — (только dev) | Приватный ключ ES256 (PEM, кривая P-256) для подписи service-assertion при вызове округа. Сгенерировать: `openssl ecparam -genkey -name prime256v1 -noout -out service-assertion-key.pem`. |
| `AllowedHosts` | Да в Production | `*` (только Development) | Host-заголовки, которым отвечает этот инстанс. Пустое/`*` в Production останавливает запуск ([раздел 21.10](docs/architecture/ecobilling-architecture.md)). |
| `Districts__AllowedHosts__0`, `__1`, ... | Нет (но пусто = ничего не разрешено) | `[]` | Явный список хостов, на которые может указывать `ApiBaseUrl` округа ([ADR-0008](docs/adr/0008-district-host-allowlist.md)). |
| `Cors__AllowedOrigins__0`, `__1`, ... | Нет | `[]` | Origin'ы, которым разрешён CORS-доступ из браузера. Пусто в Development = разрешено всё; пусто в Production = запрещено всё (раздел 19.1/22 архитектурного документа). |
| `ASPNETCORE_ENVIRONMENT` | Нет | `Production` (вне `dotnet run`) | `Development`/`Production`. Управляет и Development-only поведением (OpenAPI, Swagger UI, permissive CORS-дефолт), и обязательностью `AllowedHosts`. |
| `ASPNETCORE_HTTPS_PORTS` | Нет | не задано | Если задано, включает `UseHttpsRedirection()`. В контейнере не задаётся — TLS terminates на reverse proxy перед контейнером, не внутри него. |
| `POSTGRES_PASSWORD` | Нет (только `deploy/compose.yml`) | `ecobilling_control_dev` | Пароль контейнера PostgreSQL при использовании Docker Compose. |

## API-документация

В Development (`ASPNETCORE_ENVIRONMENT=Development`) доступны:

- `GET /openapi/v1.json` — сгенерированная спецификация OpenAPI (`Microsoft.AspNetCore.OpenApi`), отражает все
  endpoints за исключением `/health`/`/ready` (операционные, не часть версионированного API-контракта).
- `GET /swagger/index.html` — интерактивный Swagger UI поверх той же спецификации.

В Production оба отключены (раздел 22.4 архитектурного документа) — спецификация раскрывает форму запроса/ответа
каждого административного endpoint, а ничего не защищает сам `/swagger` отдельным доступом.

## Health/readiness

- `GET /health` — liveness: процесс запущен. Не проверяет зависимости.
- `GET /ready` — readiness: дополнительно проверяет доступность PostgreSQL. Возвращает `503`, если база недоступна.

## Проверка изменений

```powershell
dotnet build EcoBilling.Control.slnx
dotnet test EcoBilling.Control.slnx
```

Интеграционные и E2E-тесты поднимают PostgreSQL через Testcontainers — отдельно запущенный Docker Compose для
этого не требуется, но Docker должен быть доступен локально.

## Документация

- [`docs/architecture`](docs/architecture/README.md) — границы, зависимости и подробные архитектурные решения.
- [`docs/adr`](docs/adr/README.md) — короткие записи решений по вопросам исходного реестра Q1-Q23.
- [`docs/api`](docs/api/README.md), [`docs/database`](docs/database/README.md), [`docs/business-rules`](docs/business-rules/README.md), [`docs/testing`](docs/testing/README.md), [`docs/deployment`](docs/deployment/README.md).

## Статус

Часть II (Control) технического задания реализована по Этап 18 включительно (Этап 13 — Outbox — осознанно
пропущен, задокументирован как ненужный при текущей нагрузке). Подробный, построчный статус по каждому
разделу — в `docs/architecture/ecobilling-architecture.md`.
