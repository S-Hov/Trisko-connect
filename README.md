# Trisko connect

Программа корпоративного доступа для Windows 10/11: файлы компании, удалённые рабочие столы, серверы и устройства ЭЦП — в одном интерфейсе.

<p align="left"><img src="docs/assets/trisko-mark.svg" alt="Trisko connect" width="88" height="88" /></p>

- Файлы и сетевые диски SMB, рабочие столы RDP, серверы SSH и VNC.
- Удалённая помощь через AnyDesk и доступ к ЭЦП/USB-токенам через VirtualHere.
- Избранное, комплекты ресурсов, импорт и экспорт подключений, автоматизация рабочих действий.
- VPN как способ доступа к закрытой сети: независимые подписки и одиночные профили.
- Русский и английский интерфейс, спокойная синяя, тёмная и светлая темы.

У каждой подписки есть своё название, серверы, статистика и действия «Изменить», «Обновить», «Удалить». Одиночные VPN-профили не требуют подписки и сохраняются при её обновлении или удалении. Каскадных ролей, zapret2, WARP и раздела разблокировки сервисов в этом проекте нет.

## Использование

Добавьте рабочие ресурсы: сетевую папку SMB, рабочий стол RDP или сервер SSH. Сохраните нужные подключения в избранном. Для поддержки коллег используйте AnyDesk или VNC; для работы с удалёнными USB-токенами — раздел ЭЦП. Комплекты ресурсов можно переносить между компьютерами.

Если ресурс доступен из вашей сети, подключайтесь напрямую. Если он находится в закрытой сети компании, сначала настройте VPN-доступ:

В настройках выберите «Добавить VPN-ссылку» для одного профиля либо «Добавить подписку» для списка серверов. Затем на главной странице выберите сервер и подключитесь. По умолчанию интернет идёт через VPN, локальные сети — напрямую. Режим меняется в настройках маршрутизации.

Язык: **Настройки → Язык интерфейса → Русский / English**. Перезапустите приложение после изменения. VirtualHere запускается вручную; автоматический запуск можно включить в настройках.

RDP, SMB и SSH добавляются отдельно. Файл `.gac3` служит для переноса комплектов ресурсов и не обязателен. VPN-подписки в него не входят. VNC использует адрес компьютера и порт; AnyDesk запускается напрямую.

## Сборка

Нужны Windows 10/11 и .NET 10 SDK. Исходники GlobalHotKeys включены в репозиторий; подмодули не требуются.

```powershell
git clone https://github.com/S-Hov/Trisko-connect.git
Set-Location Trisko-connect
dotnet build .\v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj
dotnet test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj
```

Для подготовки приложения с закреплёнными VPN-ядрами:

```powershell
.\scripts\run-from-source.ps1 -PrepareOnly
```

Для запуска откройте PowerShell от имени администратора и выполните `scripts/run-from-source.ps1`. Скрипт использует отдельные данные в `artifacts/dev-user-data`. Штатный запуск запрашивает права администратора для TUN.

Пользовательские данные установленного приложения: `%LocalAppData%\Trisko connect`. Для разработки можно задать `TRISKO_CONNECT_DATA_DIRECTORY`. Рабочие конфигурации, журналы и `artifacts` не входят в Git.

Пакеты Windows и исходников собирает [build-release.ps1](scripts/build-release.ps1). Подробнее: [сборка и обновления](docs/release-and-update.md).

## English

Trisko connect brings corporate resources into one Windows application: shared files and SMB drives, RDP desktops, SSH servers, VNC, remote assistance through AnyDesk, and digital signature devices through VirtualHere. VPN is an optional way to reach a private company network, with support for multiple subscriptions and standalone profiles.

Choose **Settings → Interface language → English**, then restart. Add a standalone link or a named subscription in Settings and choose a server on the Connection page. Subscription updates and removal preserve standalone profiles. By default, Internet traffic uses the VPN while local networks remain direct.

Build with .NET 10 using the commands above. Windows distributions include pinned Xray and sing-box cores.

## Лицензия / License

Приложение — GPL-3.0, см. [LICENSE](LICENSE). Сохранены уведомления об исходной базе v2rayN и Geksa-A Connection. Сторонние компоненты распространяются по своим лицензиям: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Название и логотип Trisko принадлежат автору проекта.
