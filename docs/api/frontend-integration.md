# EcoBilling Control API — интеграция frontend

## Адреса production

- Base URL: `https://ecobilling-control-339153005822.us-east1.run.app`
- Swagger UI: `https://ecobilling-control-339153005822.us-east1.run.app/swagger`
- OpenAPI JSON: `https://ecobilling-control-339153005822.us-east1.run.app/openapi/v1.json`
- Health: `GET /health`

Все request/response body используют JSON и `camelCase`. Даты передаются как ISO 8601
`DateTimeOffset`. Идентификаторы имеют формат UUID/GUID.

## Аутентификация администратора

Access token передаётся во всех защищённых запросах:

```http
Authorization: Bearer <accessToken>
```

- access token действует 15 минут;
- refresh token действует 14 дней;
- каждый успешный refresh возвращает новую пару токенов и делает старый refresh token
  недействительным;
- logout идемпотентен и возвращает `204`;
- после `401` frontend должен один раз попробовать refresh, повторить исходный запрос и,
  если refresh тоже вернул `401`, очистить сессию.

### Login

`POST /api/v1/admin/auth/login`

```json
{
  "email": "admin@example.com",
  "password": "administrator-password"
}
```

Успех — `200`:

```json
{
  "accessToken": "eyJ...",
  "accessTokenExpiresAt": "2026-09-29T14:15:00+00:00",
  "refreshToken": "...",
  "refreshTokenExpiresAt": "2026-10-13T14:00:00+00:00"
}
```

Неверные данные, заблокированный или неактивный аккаунт намеренно дают одинаковый
ответ `401` с кодом `administrator.invalid_credentials`.

### Refresh

`POST /api/v1/admin/auth/refresh`

```json
{ "refreshToken": "..." }
```

Успех — `200`, response такой же, как у login. Ошибка — `401` с кодом
`administrator.invalid_refresh_token`.

### Logout

`POST /api/v1/admin/auth/logout`

```json
{ "refreshToken": "..." }
```

Ответ — `204 No Content`.

### Текущий пользователь

`GET /api/v1/admin/whoami` — Bearer обязателен.

```json
{
  "administratorId": "0199...",
  "email": "admin@example.com"
}
```

## Публичный resolve округа

`POST /api/v1/districts/resolve`

```json
{ "districtCode": "BISHKEK-01" }
```

Успех — `200`:

```json
{
  "districtCode": "BISHKEK-01",
  "apiBaseUrl": "https://api.example.com",
  "expiresInSeconds": 3600
}
```

Frontend может кешировать соответствие кода и URL на `expiresInSeconds`. Код
нормализуется через trim + uppercase и должен соответствовать шаблону
`^[A-Z]{2,10}-[0-9]{2,4}$`. Неизвестный и неактивный округ возвращают одинаковый `404`.

## Управление округами

Все маршруты требуют Bearer token.

### Создать округ

`POST /api/v1/admin/districts`

```json
{
  "code": "BISHKEK-01",
  "name": "Бишкек",
  "apiBaseUrl": "https://api.example.com"
}
```

Успех — `201 Created`:

```json
{
  "districtId": "0199...",
  "normalizedCode": "BISHKEK-01"
}
```

Название обязательно, максимум 200 символов. URL обязан быть абсолютным HTTPS URL без
credentials, query и fragment; hostname должен быть в серверном allowlist.

### Список округов

`GET /api/v1/admin/districts?page=1&pageSize=50`

```json
{
  "items": [
    {
      "districtId": "0199...",
      "code": "BISHKEK-01",
      "name": "Бишкек",
      "apiBaseUrl": "https://api.example.com/",
      "status": "Active",
      "createdAt": "2026-09-29T14:00:00+00:00",
      "updatedAt": "2026-09-29T14:00:00+00:00",
      "activatedAt": "2026-09-29T14:01:00+00:00",
      "deactivatedAt": null
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 50
}
```

Допустимые `status`: `Inactive`, `Active`. Значения пагинации по умолчанию: `page=1`,
`pageSize=50`; максимальный `pageSize=200`.

### Получить округ

`GET /api/v1/admin/districts/{districtId}` — `200 DistrictResponse` или `404`.

### Изменить округ

`PUT /api/v1/admin/districts/{districtId}`

```json
{
  "name": "Новое название",
  "apiBaseUrl": "https://new-api.example.com"
}
```

Успех — `204 No Content`.

### Активировать / деактивировать

- `POST /api/v1/admin/districts/{districtId}/activate`
- `POST /api/v1/admin/districts/{districtId}/deactivate`

Тело не требуется. Успех — `204 No Content`.

## Директор округа

Все маршруты требуют Bearer token. Control вызывает защищённый internal API выбранного
округа; frontend не обращается к окружной базе или internal API напрямую.

### Создать первого директора

`POST /api/v1/admin/districts/{districtId}/directors`

```json
{
  "fullName": "Иван Иванов",
  "email": "director@example.com",
  "password": "DirectorPass123!"
}
```

Успех — `201 Created`:

```json
{
  "operationId": "0199...",
  "directorId": "district-director-id"
}
```

Первый директор создаётся только один раз. Повторная успешная операция даёт `409` с
кодом `director.already_exists`.

### Сбросить пароль директора

`POST /api/v1/admin/districts/{districtId}/directors/reset-password`

```json
{
  "email": "director@example.com",
  "newPassword": "NewDirectorPass123!"
}

Успех — `202 Accepted`:

```json
{ "operationId": "0199..." }
```

Пароль передаётся только в теле защищённого запроса и не сохраняется Control.

### Статус операции

`GET /api/v1/admin/operations/{operationId}`

```json
{
  "operationId": "0199...",
  "districtId": "0199...",
  "operationType": "DirectorCreation",
  "status": "Completed",
  "attemptCount": 1,
  "createdAt": "2026-09-29T14:00:00+00:00",
  "startedAt": "2026-09-29T14:00:01+00:00",
  "completedAt": "2026-09-29T14:00:02+00:00",
  "failedAt": null,
  "lastErrorCode": null
}
```

`operationType`: `DirectorCreation`, `PasswordReset`.
`status`: `Pending`, `InProgress`, `Completed`, `Failed`.

## Аудит

`GET /api/v1/admin/audit?page=1&pageSize=50` — Bearer обязателен.

```json
{
  "items": [
    {
      "id": "0199...",
      "administratorId": "0199...",
      "action": "district.created",
      "entityType": "District",
      "entityId": "0199...",
      "beforeData": null,
      "afterData": "{\"code\":\"BISHKEK-01\"}",
      "correlationId": "...",
      "ipAddress": "...",
      "userAgent": "...",
      "createdAt": "2026-09-29T14:00:00+00:00"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 50
}
```

`beforeData` и `afterData` — JSON, сериализованный как строка. Frontend может разобрать
его через `JSON.parse`, предварительно проверив значение на `null`.

## Ошибки

Ошибки бизнес-логики имеют единый формат:

```json
{
  "code": "district.not_found",
  "message": "...",
  "traceId": "..."
}
```

Frontend должен принимать решения по стабильному полю `code`, а не по `message`.
Используемые статусы: `400`, `401`, `404`, `409`, `429`, `500`, `502`, `503`.
Ответ `401` от JWT middleware может не иметь JSON body.

Основные error codes:

- `district.invalid_code`
- `district.invalid_url`
- `district.invalid_name`
- `district.host_not_allowed`
- `district.not_found`
- `district.inactive`
- `district.code_conflict`
- `district.invalid_status_transition`
- `administrator.invalid_credentials`
- `administrator.invalid_refresh_token`
- `validation.failed`
- `provisioning.operation_not_found`
- `director.not_found`
- `director.already_exists`
- `provisioning.concurrent_conflict`
- `district.unavailable`
- `service.unauthorized`
- `rate_limit.exceeded`

При `429` следует учитывать заголовок `Retry-After`. Login ограничен 20 запросами в
минуту на IP, resolve — 30 запросами в минуту на IP.

## Минимальная конфигурация frontend

```env
VITE_CONTROL_API_URL=https://ecobilling-control-339153005822.us-east1.run.app
```

```ts
export const CONTROL_API_URL =
  import.meta.env.VITE_CONTROL_API_URL.replace(/\/$/, "");

export async function controlFetch(path: string, init: RequestInit = {}) {
  const token = sessionStorage.getItem("accessToken");
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");

  if (init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  return fetch(`${CONTROL_API_URL}${path}`, { ...init, headers });
}
```

Не логировать токены, пароли и тела login/refresh запросов.

## Что ещё требуется до полноценного подключения браузерного frontend

1. Сообщить точный origin frontend, например `https://admin.example.com`, и добавить
   его в CORS allowlist Control API. Сейчас CORS middleware не настроен.
2. Создать первого системного администратора через provisioning CLI. Публичной
   регистрации администраторов намеренно нет.
3. Добавить hostname каждого окружного API в `Districts:AllowedHosts`. Сейчас production
   allowlist пуст, поэтому создание округа вернёт `district.host_not_allowed`.
4. Заменить ранее опубликованный пароль Neon и создать новые версии двух database
   secrets в Google Secret Manager.

