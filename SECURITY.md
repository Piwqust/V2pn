# Безопасность / Security

## Как сообщить об уязвимости

Не открывайте публичное обращение. Напишите закрыто: вкладка [Security → Report a vulnerability](https://github.com/Piwqust/V2pn/security/advisories/new) в этом репозитории. Сообщение увидят только сопровождающие.

Напишите, какая версия V2pn и система, что именно происходит и как это повторить. Ссылки подписок и адреса серверов не присылайте: в них ваш ключ доступа.

Что считается уязвимостью V2pn:

- установка обновления, которое не подписано ключом релизов, или обход проверки пакета;
- выполнение чужого кода или повышение прав через V2pn или его установщик (AmazTool);
- утечка ключей серверов, ссылок подписок или трафика мимо выбранного режима;
- доступ к локальному прокси или API ядра из сети или от чужой программы, когда пользователь этого не включал.

Проблемы самих ядер (Xray, sing-box, mihomo) и протоколов сообщайте их авторам; если V2pn усугубляет такую проблему, напишите и сюда.

Исправления выходят в новой версии. Поддерживается только последняя: V2pn обновляется сам.

## Reporting a vulnerability

Please do not open a public issue. Report privately through [Security → Report a vulnerability](https://github.com/Piwqust/V2pn/security/advisories/new); only the maintainers see it.

Include the V2pn version, the operating system, what happens and how to reproduce it. Do not send subscription links or server addresses: they carry your access key.

In scope: installing an update that is not signed with the release key, code execution or privilege escalation through V2pn or its updater (AmazTool), leaks of server keys, subscription links or traffic outside the chosen mode, and access to the local proxy or the core's API from the network or another program when the user did not enable it. Flaws in the cores themselves (Xray, sing-box, mihomo) belong to their authors.

Fixes ship in a new version. Only the latest version is supported; V2pn updates itself.
