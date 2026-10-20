# ИС «Поликлиника»

Курсовой проект: информационная система поликлиники. Клиент на Avalonia (Desktop и Browser), сервер на ASP.NET Core, база PostgreSQL.

## Что умеет

- Пациент: запись к врачу, перенос и отмена записи, медкарта, назначения, результаты исследований, документы, счета, уведомления.
- Регистратор: картотека пациентов, полисы и прикрепление к участку, расписание врачей, запись пациентов, документы, услуги и оплата.
- Врач: своё расписание, карты пациентов, протокол приёма, диагнозы, назначения и направления.
- Администратор: пользователи и права ролей, сотрудники, справочники, прайс, отчёты, журнал аудита, резервные копии.

## Структура

- `Clinic`: общий код клиента (окна, ViewModel, работа с API, выгрузка в Word и Excel), а также сущности и `MyDbContext`.
- `Clinic.API`: сервер (minimal API, JWT, EF Core).
- `Clinic.Desktop`: запуск клиента на компьютере.
- `Clinic.Browser`: запуск клиента в браузере.
- `database/clinic.sql`: скрипт базы данных.
- `scripts`: скрипты запуска.

## Запуск

Нужны .NET SDK 10 и PostgreSQL.

1. Скопировать `Clinic.API/api.local.example.json` в `Clinic.API/api.local.json`, указать строку подключения и `Auth:SigningKey` (не короче 32 символов).
2. Создать базу `clinic_local` с тестовыми данными (пароль для всех тестовых пользователей задаётся в `CLINIC_INITIAL_PASSWORD`):

```sh
CLINIC_INITIAL_PASSWORD='пароль' dotnet run --project Clinic.API -- --prepare-local
```

3. Запустить сервер и клиент:

```sh
./scripts/run-project.sh
```

Можно и по отдельности: `./scripts/run-api.sh` и `./scripts/run-desktop.sh`. Если Desktop запускать из Rider, он сам поднимет локальный API.

Для Browser адрес сервера лежит в `Clinic.Browser/wwwroot/clinic.config.json`, для сборки нужен `dotnet workload install wasm-tools`.

## Тестовые пользователи

| Логин | Роль | Пароль |
| --- | --- | --- |
| `m.smirnova` | Пациент | Test1234 |
| `e.volkova` | Регистратор | Test1234 |
| `a.ivanova`, `d.sokolov`, `e.morozova` | Врач | Test1234 |
| `n.belov` | Администратор | Test1234 |

## [Открыть readme с эндпоинтами](ENDPOINTS.md)
