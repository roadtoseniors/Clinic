# Clinic.API — список эндпоинтов

Все адреса, кроме `/auth/*`, требуют заголовок `Authorization: Bearer <token>`.
Ошибки всегда приходят в одном формате: `{ "status": 400, "title": "текст для пользователя" }`.
Время в JSON: `"10:30"` (принимается и `"10:30:00"`), дата: `"2026-10-06"`.
Списки с `page`/`pageSize` возвращают `{ items, total, page, pageSize }`.

## Вход и профиль
| Метод | Адрес | Право | Что делает |
|---|---|---|---|
| POST | /auth/login | — | вход, возвращает токен, аккаунт и права |
| POST | /auth/register | — | регистрация пациента: пациент + полис ОМС + аккаунт |
| GET | /api/me | вход | текущий аккаунт и права |
| PUT | /api/me/theme | вход | тема `light` / `dark` |
| PUT | /api/me/password | вход | смена своего пароля |

## Кабинет пациента (право own.view)
| Метод | Адрес | Что делает |
|---|---|---|
| GET | /api/my/profile | личные данные, полисы, прикрепление |
| GET | /api/my/appointments | мои записи |
| GET | /api/my/medcard | история приёмов, диагнозы, назначения, направления |
| GET | /api/my/referrals | направления и результаты |
| GET | /api/my/referral-files/{id} | скачать файл результата |
| GET | /api/my/invoices | мои счета |
| GET | /api/my/documents | согласия, договоры, справки |
| GET | /api/my/notifications | уведомления |
| POST | /api/my/notifications/{id}/read | отметить прочитанным |
| POST | /api/my/notifications/read-all | прочитать все |

## Справочники (читать может любой вошедший, менять — reference.manage)
`/api/posts`, `/api/specialties`, `/api/servicegroups`, `/api/drugs`, `/api/documenttypes`,
`/api/rooms`, `/api/services`, `/api/diagnoses` (ключ — код), `/api/paymethods`,
`/api/insurancecompanies`, `/api/districts` — у каждого GET, POST, PUT /{id}, DELETE /{id}.
Услуги и страховые при DELETE выключаются, а не удаляются.

## Сотрудники
| Метод | Адрес | Право |
|---|---|---|
| GET | /api/employees, /api/employees/{id} | любой сотрудник |
| POST, PUT /{id}, DELETE /{id} | /api/employees | reference.manage или account.manage |
| GET | /api/doctors?specialtyId= | любой вошедший (для записи) |

## Пациенты
| Метод | Адрес | Право |
|---|---|---|
| GET | /api/patients?q=&birthDate=&page= | patient.view |
| GET | /api/patients/{id} | patient.view, medcard.view |
| POST / PUT /{id} / DELETE /{id} | /api/patients | patient.manage |
| POST | /api/patients/{id}/restore | patient.manage |
| POST | /api/patients/{id}/account | patient.manage, account.manage |
| GET / POST | /api/patients/{id}/policies | patient.view / patient.manage |
| PUT | /api/policies/{id} | patient.manage |
| POST | /api/policies/{id}/close | patient.manage |
| GET / POST | /api/patients/{id}/attachments | patient.view / patient.manage |
| GET | /api/patients/{id}/documents, /api/documents/{id} | patient.view, medcard.view |
| POST | /api/patients/{id}/documents | document.manage, visit.manage |

## Расписание и запись
| Метод | Адрес | Право |
|---|---|---|
| GET | /api/schedules?from=&to=&employeeId=, /api/schedules/{id} | любой сотрудник |
| POST / PUT /{id} / DELETE /{id} | /api/schedules | schedule.manage |
| GET | /api/slots?date=&employeeId=&specialtyId= | любой вошедший |
| GET | /api/appointments?date=&from=&to=&employeeId=&patientId=&status=&page= | appointment.manage |
| GET | /api/appointments/{id} | appointment.manage, own.view, doctor.schedule |
| POST | /api/appointments | own.book, appointment.manage |
| POST | /api/appointments/{id}/reschedule | own.book, appointment.manage |
| POST | /api/appointments/{id}/cancel | own.book, appointment.manage |
| POST | /api/appointments/{id}/no-show | appointment.manage, visit.manage |

## Приём у врача
| Метод | Адрес | Право |
|---|---|---|
| GET | /api/doctor/schedule?from=&to= | doctor.schedule |
| GET | /api/patients/{id}/medcard, /api/visits/{id} | medcard.view |
| POST | /api/visits `{ appointmentId }` | visit.manage |
| PUT | /api/visits/{id} | visit.manage |
| POST | /api/visits/{id}/close | visit.manage |
| POST | /api/visits/{id}/prescriptions | visit.manage |
| DELETE | /api/prescriptions/{id} | visit.manage |
| POST | /api/visits/{id}/referrals | referral.issue |
| POST | /api/referrals/{id}/cancel | referral.issue |

## Исследования
| Метод | Адрес | Право |
|---|---|---|
| GET | /api/referrals?status=&serviceId=&patientId=&page= | referral.view |
| GET | /api/referrals/{id} | referral.view, medcard.view |
| PUT | /api/referrals/{id}/result | referral.result |
| GET / POST | /api/referrals/{id}/files | referral.view / referral.result (multipart, поле `file`) |
| GET / DELETE | /api/referral-files/{id} | referral.view / referral.result |

## Услуги и счета
| Метод | Адрес | Право |
|---|---|---|
| GET / POST | /api/visits/{id}/services | service.render (GET ещё invoice.manage) |
| DELETE | /api/visit-services/{id} | service.render |
| GET | /api/patients/{id}/unbilled-services | invoice.manage |
| GET | /api/invoices?status=&patientId=&from=&to=&page=, /api/invoices/{id} | invoice.manage |
| POST | /api/invoices `{ patientId, visitServiceIds? }` | invoice.manage |
| POST | /api/invoices/{id}/pay `{ methodId, policyId? }` | invoice.manage |
| POST | /api/invoices/{id}/cancel | invoice.manage |

## Администрирование
| Метод | Адрес | Право |
|---|---|---|
| GET / POST | /api/accounts | account.manage |
| PUT / DELETE | /api/accounts/{id} | account.manage |
| POST | /api/accounts/{id}/reset-password | account.manage |
| GET | /api/roles | role.manage, account.manage |
| GET | /api/permissions | role.manage |
| PUT | /api/roles/{id}/permissions `{ codes: [...] }` | role.manage |
| GET | /api/audits?from=&to=&accountId=&patientId=&action=&table=&page= | audit.view |
| GET | /api/reports/attendance, /load, /morbidity, /revenue (?from=&to=) | report.view |
| GET / POST | /api/backups | backup.manage |
| POST | /api/backups/restore `{ fileName }` | backup.manage |
| GET | /api/backups/log | backup.manage |

## Настройка резервного копирования (appsettings.json)
```json
"Backup": {
  "Folder": "Backups",
  "PgDumpPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_dump.exe",
  "PgRestorePath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_restore.exe"
}
```
