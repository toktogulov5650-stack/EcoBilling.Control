# Развёртывание в Neon и Google Cloud Run

Control разворачивается отдельно от backend округов и использует собственную PostgreSQL.
Рекомендуемая схема: Cloud Run Service для API, Cloud Run Job для миграций, Neon для
PostgreSQL, Artifact Registry для образа и Secret Manager для секретов.

## Что уже поддерживает образ

- API слушает адрес `0.0.0.0` и порт из переменной `PORT` (локально — `8080`).
- TLS завершается на Cloud Run; внутри контейнера используется HTTP.
- Один образ содержит API и отдельную команду применения EF Core migrations.
- Процесс контейнера запускается не от `root`.
- Кратковременные ошибки соединения PostgreSQL повторяются EF Core до трёх раз.

Миграции намеренно не запускаются при старте API: Cloud Run может одновременно
запустить несколько экземпляров одной ревизии. Схему применяет отдельный Job до
переключения трафика на новую версию.

## 1. Neon

Создайте проект и production branch в Neon в регионе, максимально близком к выбранному
региону Cloud Run. Подготовьте две строки подключения:

- `ecobilling-control-database`: pooled endpoint (`-pooler` в имени хоста) для API;
- `ecobilling-control-database-migrations`: direct endpoint для миграций.

Npgsql использует формат `ключ=значение`, а не URI из окна Neon. Пример без реальных
учётных данных:

```text
Host=ep-example-pooler.eu-central-1.aws.neon.tech;Port=5432;Database=neondb;Username=app_user;Password=REPLACE_ME;SSL Mode=VerifyFull;Maximum Pool Size=10;Timeout=15;Command Timeout=30;Application Name=EcoBilling.Control.Api
```

Для migration secret используйте direct hostname без `-pooler`, отдельного владельца
схемы и `Application Name=EcoBilling.Control.Migrations`. В production runtime-пользователь
не должен иметь права изменять схему. Значение `Maximum Pool Size` выбирайте вместе с
`--max-instances`: верхняя оценка числа клиентских подключений равна произведению этих
двух значений.

## 2. Google Cloud и образ

Ниже команды для PowerShell. Замените значения в угловых скобках.

```powershell
$PROJECT_ID = "<gcp-project-id>"
$REGION = "<cloud-run-region>"
$REPOSITORY = "ecobilling"
$SERVICE = "ecobilling-control"
$SERVICE_ACCOUNT = "ecobilling-control-runtime@$PROJECT_ID.iam.gserviceaccount.com"
$TAG = (Get-Date -Format "yyyyMMdd-HHmmss")
$IMAGE = "$REGION-docker.pkg.dev/$PROJECT_ID/$REPOSITORY/control:$TAG"

gcloud config set project $PROJECT_ID
gcloud services enable run.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com
gcloud artifacts repositories create $REPOSITORY --repository-format=docker --location=$REGION
gcloud iam service-accounts create ecobilling-control-runtime --display-name="EcoBilling Control runtime"
gcloud builds submit --config=deploy/cloudbuild.yaml --substitutions="_IMAGE=$IMAGE" .
```

Если репозиторий или service account уже существуют, соответствующие команды создания
выполнять повторно не нужно.

## 3. Secret Manager

Создайте в Secret Manager четыре секрета и добавьте первую версию каждого:

| Secret | Содержимое |
| --- | --- |
| `ecobilling-control-database` | pooled Npgsql connection string для API |
| `ecobilling-control-database-migrations` | direct Npgsql connection string владельца схемы |
| `ecobilling-control-jwt-key` | случайный длинный ключ подписи access token |
| `ecobilling-control-service-auth-pem` | приватный EC PEM для service assertions |

Не помещайте значения в репозиторий или командную строку. Удобнее добавить версии через
Google Cloud Console. Дайте runtime service account доступ только к этим секретам:

```powershell
$MEMBER = "serviceAccount:$SERVICE_ACCOUNT"

gcloud secrets add-iam-policy-binding ecobilling-control-database --member=$MEMBER --role=roles/secretmanager.secretAccessor
gcloud secrets add-iam-policy-binding ecobilling-control-database-migrations --member=$MEMBER --role=roles/secretmanager.secretAccessor
gcloud secrets add-iam-policy-binding ecobilling-control-jwt-key --member=$MEMBER --role=roles/secretmanager.secretAccessor
gcloud secrets add-iam-policy-binding ecobilling-control-service-auth-pem --member=$MEMBER --role=roles/secretmanager.secretAccessor
```

## 4. Применение миграций

Создайте или обновите Job из того же неизменяемого image tag, затем дождитесь успешного
завершения:

```powershell
gcloud run jobs deploy "$SERVICE-migrate" `
  --image=$IMAGE `
  --region=$REGION `
  --service-account=$SERVICE_ACCOUNT `
  --command=dotnet `
  --args=tools/EcoBilling.Control.Provisioning.dll,migrate `
  --set-secrets="ConnectionStrings__Database=ecobilling-control-database-migrations:1" `
  --max-retries=1 `
  --task-timeout=10m

gcloud run jobs execute "$SERVICE-migrate" --region=$REGION --wait
```

Перед production-запуском рекомендуется выполнить этот Job против отдельной Neon branch
со снимком production-схемы. Версии секретов в примерах закреплены на `1`; при ротации
создайте новую версию и явно обновите Job/Service.

## 5. Развёртывание API

```powershell
gcloud run deploy $SERVICE `
  --image=$IMAGE `
  --region=$REGION `
  --platform=managed `
  --service-account=$SERVICE_ACCOUNT `
  --allow-unauthenticated `
  --set-env-vars="ASPNETCORE_ENVIRONMENT=Production" `
  --set-secrets="ConnectionStrings__Database=ecobilling-control-database:1,Authentication__Jwt__SigningKey=ecobilling-control-jwt-key:1,ServiceAuth__Jwt__SigningKeyPem=ecobilling-control-service-auth-pem:1" `
  --max-instances=5 `
  --concurrency=40 `
  --memory=512Mi `
  --cpu=1
```

`--allow-unauthenticated` нужен публичному `ResolveDistrict`; административные endpoints
всё равно защищены JWT приложения. Если публичное разрешение кода не требуется, уберите
этот флаг и выдавайте `roles/run.invoker` только доверенным вызывающим сторонам.

Разрешённые hostname API округов задаются отдельно, по одному элементу массива:

```powershell
gcloud run services update $SERVICE --region=$REGION `
  --update-env-vars="Districts__AllowedHosts__0=api.district-1.example,Districts__AllowedHosts__1=api.district-2.example"
```

Проверьте URL, который вернул Cloud Run:

```powershell
Invoke-RestMethod "https://<cloud-run-service-url>/health"
```

Ожидаемый ответ: `{"status":"Healthy"}`. После этого проверьте login, resolve и один
защищённый административный endpoint. `/health` проверяет жизнеспособность процесса, а
не доступность Neon, поэтому не будит простаивающий Neon compute каждым probe.

Текущий rate limiter хранит состояние в памяти отдельного экземпляра API. При
горизонтальном масштабировании это не глобальный лимит, а `RemoteIpAddress` за proxy не
является надёжным идентификатором конечного клиента. Перед значимым публичным трафиком
добавьте внешний Application Load Balancer с Cloud Armor rate limiting; встроенный
limiter оставьте вторым защитным слоем. Не доверяйте первому значению произвольного
`X-Forwarded-For` прямо в приложении.

## 6. Первый системный администратор

Создание администратора остаётся интерактивной операцией и не выполняется при старте
API. Запустите её с защищённой административной машины, используя direct connection:

```powershell
$env:ConnectionStrings__Database = "<direct Npgsql connection string>"
dotnet run --project src/EcoBilling.Control.Provisioning/EcoBilling.Control.Provisioning.csproj -- "admin@example.com" "System Administrator"
Remove-Item Env:ConnectionStrings__Database
```

Пароль будет запрошен без отображения. Не передавайте его аргументом командной строки и
не сохраняйте в Secret Manager как постоянный provisioning-пароль.

## Порядок каждого релиза

1. Собрать image с новым уникальным tag.
2. Запустить тесты и миграционный Job на staging Neon branch.
3. Обновить production migration Job на тот же tag и успешно выполнить его.
4. Развернуть Cloud Run Service с тем же tag.
5. Проверить `/health` и ключевые сценарии; старую ревизию оставить доступной для быстрого отката.
