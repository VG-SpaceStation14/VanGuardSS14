<p align="center">
  <img alt="VanGuard SS14" width="350" src="Resources/Textures/_VanGuard/Logo/logo.png" />
</p>

<div align="center">

  [![Discord](https://img.shields.io/badge/Discord-Наш%20сервер-E05A33?logo=discord&logoColor=white&style=for-the-badge&labelColor=1C1C1C)](https://discord.gg/nW73CdBYkg)
  [![Wiki](https://img.shields.io/badge/Wiki-Наша%20вики-E05A33?style=for-the-badge&labelColor=1C1C1C)](https://vanguardproject.ru/)
  [![Steam](https://img.shields.io/badge/Steam-В%20Steam-E05A33?logo=steam&logoColor=white&style=for-the-badge&labelColor=1C1C1C)](https://store.steampowered.com/app/1255460/Space_Station_14/)
  [![Client](https://img.shields.io/badge/Client-Скачать%20клиент-E05A33?style=for-the-badge&labelColor=1C1C1C)](https://spacestation14.io/about/nightlies/)
  [![GitHub](https://img.shields.io/github/stars/VG-SpaceStation14/VanGuardSS14?style=for-the-badge&logo=github&logoColor=white&labelColor=1C1C1C&color=E05A33)](https://github.com/VG-SpaceStation14/VanGuardSS14)

</div>

---

## 🚀 О проекте

**VanGuard** — это русскоязычный сервер и форк [space-syndicate/space-station-14](https://github.com/space-syndicate/space-station-14), основанный на движке [Robust Toolbox](https://github.com/space-wizards/RobustToolbox).

Space Station 14 — это ролевая игра, вдохновлённая культовой Space Station 13. Мы стремимся не просто поддерживать актуальный перевод и тянуть изменения из апстрима, но и развивать собственные уникальные механики.

### 🌟 Наши ключевые особенности

Наше главное отличие — **подход к разработке**. Мы сознательно разрабатываем популярные механики с нуля (clean-room), чтобы весь наш код оставался под лицензией MIT. 

* **Экономика:** Динамическая система с биржей, зарплатами, счетами на ID-картах и переводами. Реализована независимо, на основе описания поведения, без использования чужого кода.
* **Коммуникации:** Собственная реализация «НаноЧата» для КПК и полноценная система языков (включая написание текстов на разных языках).
* **Наука и Инженерия:** Переработанная консоль исследований с ветками технологий, сканеры экспериментов и Быстрое Трубопроводное Устройство (RPD) для атмосферного отдела.
* **Визуал:** Мы переняли глобальный респрайт и новый стиль игры из официального апстрима, а также добавили свои анимации (моргание, сварка, искры) и объемный свет.

## 🛠 Сборка

**Требования:** [.NET SDK 10.0+](https://dotnet.microsoft.com/download/dotnet/10.0) и [Python 3](https://www.python.org/downloads/) (для запуска скриптов).

1. Склонируйте этот репозиторий:
```shell
git clone https://github.com/VG-SpaceStation14/VanGuardSS14.git
```
2. Запустите `RUN_THIS.py` для инициализации подмодулей и скачивания движка:
```shell
cd VanGuardSS14
python RUN_THIS.py
```
3. Скомпилируйте проект, используя `dotnet build`.

[Более подробная инструкция по сборке и запуску сервера](https://docs.spacestation14.com/en/general-development/setup/server-hosting-tutorial.html)

## 🤝 Контрибьют

Мы рады принять вклад от любого человека. Заходите в [наш Discord](https://discord.gg/nW73CdBYkg), если хотите помочь. У нас есть [список задач](https://github.com/VG-SpaceStation14/VanGuardSS14/issues), любой может за них взяться. 

Пожалуйста, убедитесь, что ваши изменения и PRы соответствуют [стандартным правилам SS14](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html). 

**Использование ИИ:** Мы не запрещаем использовать нейросети как инструменты для написания кода. Однако, отправляя PR, вы берёте на себя полную ответственность за результат. Код должен проходить тесты, быть осмысленным и **не нарушать лицензионную чистоту проекта (MIT)**. Автоматические проверки (CodeRabbit, CI) не заменяют вашу личную проверку на плагиат. *За исключением этого пункта, во всём остальном действуют стандартные правила SS14.*

## ⚖️ Лицензия

**Код — MIT.** 
Весь исходный код в этом репозитории, включая все наши собственные механики, распространяется под лицензией [MIT](https://github.com/VG-SpaceStation14/VanGuardSS14/blob/master/LICENSE.TXT). Мы сознательно разрабатываем механики с нуля, чтобы весь код оставался под MIT и был свободен для использования в других проектах.

**Унаследованный код.**
VanGuard является форком space-syndicate/space-station-14, код которого, согласно его README и файлу лицензии, распространяется под MIT. Унаследованный код сохраняет исходные уведомления об авторских правах. Ассеты лицензированы отдельно (см. ниже).

**Ассеты — CC-BY-SA 3.0.** 
Большинство ресурсов (спрайты, звуки) лицензированы по [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), если не указано иное. У ресурсов есть собственная лицензия и информация об авторском праве в метаданных файла (`meta.json`). [Пример](https://github.com/VG-SpaceStation14/VanGuardSS14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

Обратите внимание, что некоторые ассеты лицензированы на некоммерческой основе [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/), и их необходимо удалить, если вы хотите использовать этот проект в коммерческих целях.

---
*VanGuard является форком space-syndicate/space-station-14, который в свою очередь базируется на space-wizards/space-station-14. Мы благодарим сообщества обоих проектов за проделанную работу.*
