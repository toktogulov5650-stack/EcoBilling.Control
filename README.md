# EcoBilling.Control

EcoBilling.Control — отдельный центральный backend системы EcoBilling.

Для приложения он принимает код округа и возвращает адрес соответствующего `EcoBilling.Api`. Для системного администратора он управляет реестром округов и запускает создание первого директора через защищённый внутренний API округа.

## Проекты

- `EcoBilling.Control.Api` — публичные и административные HTTP endpoints.
- `EcoBilling.Control.Application` — сценарии использования и интерфейсы.
- `EcoBilling.Control.Domain` — центральные бизнес-правила и сущности.
- `EcoBilling.Control.Infrastructure` — PostgreSQL, безопасность, кеширование и клиенты округов.

## Зависимости

```text
Api ───────────→ Application ───────────→ Domain
 │                       ↑
 └────────→ Infrastructure ─────────────┘
```

## Команды

```powershell
dotnet restore EcoBilling.Control.slnx
dotnet build EcoBilling.Control.slnx
dotnet test EcoBilling.Control.slnx
dotnet run --project src/EcoBilling.Control.Api/EcoBilling.Control.Api.csproj
```

После запуска техническая проверка API доступна по адресу `/health`.

## Статус

Создан архитектурный каркас. Схема PostgreSQL, аутентификация администратора и бизнес-сценарии будут реализованы отдельными этапами.
