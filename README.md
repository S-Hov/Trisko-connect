# Trisko connect

Универсальный клиент VPN и удалённого доступа для Windows 10/11 на базе v2rayN.

![Trisko](docs/assets/trisko-icon.png)

- Несколько независимых именованных HTTPS-подписок и одиночные VPN-ссылки VLESS, VMess, Trojan, Shadowsocks и других поддерживаемых протоколов.
- RDP, SSH, SMB: ручное добавление, избранное, импорт и экспорт ресурсов.
- ЭЦП и USB-токены через VirtualHere; VNC по адресу компьютера и прямое подключение через AnyDesk.
- Русский и английский интерфейс; фирменная мятная, тёмная и светлая темы.
- Отдельные настройки, автозапуск и канал обновлений Trisko connect.

У каждой подписки есть своё название, серверы, статистика и действия «Изменить», «Обновить», «Удалить». Одиночные VPN-профили не требуют подписки и сохраняются при её обновлении или удалении. Каскадных ролей, zapret2, WARP и раздела разблокировки сервисов в этом проекте нет.

## Использование

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

Trisko connect is a Windows VPN and remote access client based on v2rayN. It supports multiple independent subscriptions, standalone VPN links, RDP, SMB, SSH, digital signature devices through VirtualHere, VNC, and direct AnyDesk connections.

Choose **Settings → Interface language → English**, then restart. Add a standalone link or a named subscription in Settings and choose a server on the Connection page. Subscription updates and removal preserve standalone profiles. By default, Internet traffic uses the VPN while local networks remain direct.

Build with .NET 10 using the commands above. Windows distributions include pinned Xray and sing-box cores.

## Лицензия / License

Приложение — GPL-3.0, см. [LICENSE](LICENSE). Сохранены уведомления об исходной базе v2rayN и Geksa-A Connection. Сторонние компоненты распространяются по своим лицензиям: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Название и логотип Trisko принадлежат автору проекта.
