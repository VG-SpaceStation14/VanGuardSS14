ent-ExperimentScanner = Сканер экспериментов
    .desc = Портативный сканер для полевых исследовательских заказов. Выдаёт задания от научного отдела и отслеживает их выполнение.
ent-ExperimentFloorScanner = Напольный сканер экспериментов
    .desc = Стационарный сканер, который считывает все предметы, существа и лужи на своей клетке, выполняя исследовательские заказы.
ent-ExperimentFloorScannerMachineCircuitboard = Плата напольного сканера экспериментов
    .desc = Печатная плата для напольного сканера экспериментов.

experiment-scanner-title = Сканер экспериментов
experiment-scanner-order-name = [bold]{ $name }[/bold]
experiment-scanner-select-server = Сервер
experiment-scanner-tab-available = Доступные
experiment-scanner-tab-active = Выполняется
experiment-scanner-tab-available-count = Доступные ({ $count })
experiment-scanner-hint = Возьмите сканер в руки и наведите его на цель, чтобы выполнить активный заказ.
experiment-scanner-subtitle = Полевые исследовательские заказы научного отдела
experiment-scanner-subtitle-floor = Сканирует все предметы на своей клетке
experiment-scanner-no-active = Нет активного заказа.
experiment-scanner-abandon = Отказаться
experiment-scanner-skip = Пропустить
experiment-scanner-take = Выбрать
experiment-scanner-progress = Прогресс: { $current }/{ $target }
experiment-scanner-reward = Награда: [color=#c27cff]{ $points }[/color] очков исследований
experiment-scanner-wait-rescan = Повторный скан через: { $time }
experiment-scanner-server-selected = [color=limegreen]Связь с сервером: { $server }[/color]
experiment-scanner-server-not-selected = Сервер исследований не выбран
experiment-scanner-progress-popup = Прогресс заказа: { $current }/{ $target }
experiment-scanner-complete-popup = Эксперимент завершен.
experiment-scanner-popup-selected = Заказ принят.
experiment-scanner-popup-abandoned = Заказ возвращен в доступные.
experiment-scanner-popup-skipped = Заказ пропущен и заменен.
experiment-scanner-popup-already-active = Уже есть активный заказ.
experiment-scanner-popup-no-active = Нет активного заказа.
experiment-scanner-popup-no-available = Нет доступных заказов для замены.
experiment-scanner-popup-skip-cooldown = Пропуск доступен раз в 10 минут.
experiment-scanner-popup-no-server = Сначала выберите сервер исследований.
experiment-scanner-popup-no-station = Сканер не привязан к станции. Подключите его на станции один раз.
experiment-scanner-disk-fallback-popup = Связь с сервером отсутствует. Создан диск исследований на { $points } очков.
experiment-scanner-complete-radio-unknown = Неизвестный
experiment-scanner-complete-radio-broadcast = [bold]{ $performer }[/bold] успешно завершил эксперимент: [bold]{ $order }[/bold]. Начислено очков: [bold]{ $points }[/bold].
experiment-order-species = раса
experiment-order-reagent = реагент
experiment-order-department = отдел

experiment-order-ame-name = Телеметрия перегруженного ДАМ
experiment-order-ame-desc = Отдел энергетики корпорации просит замеры с ДАМ, работающего на впрыске выше безопасного значения. Снимите показания с перегруженного контроллера.

experiment-order-species-safe-name = Проверка совместимости реагентов
experiment-order-species-safe-desc = Нужно просканировать представителя расы { $species }, у которого в крови находится { $reagent }. Данные помогут уточнить безопасные дозировки для этой расы.

experiment-order-species-unsafe-name = Полевые испытания опасных реагентов
experiment-order-species-unsafe-desc = Для исследовательского гранта требуется скан представителя расы { $species } с опасным веществом { $reagent } в кровотоке. Не забудьте оформить отказ от ответственности.

experiment-order-vomit-name = Анализ биологической жидкости
experiment-order-vomit-desc = Служба уборки запросила скан состава лужи рвоты, чтобы подобрать более эффективные чистящие средства.

experiment-order-ripley-name = Образец полностью оснащённого Рипли
experiment-order-ripley-desc = Снабжение ждёт меха Рипли со всеми тремя слотами оборудования. Сканируйте полностью укомплектованного меха в качестве подтверждения.

experiment-order-pet-name = Учёт станционных животных
experiment-order-pet-desc = Корпорация ведёт реестр питомцев на станциях. Сегодня нужно просканировать: { $target }.

experiment-order-xeno-name = Полевое наблюдение за фауной
experiment-order-xeno-desc = Для биологического отдела нужен скан особи вида { $target }. Постарайтесь не пострадать при сборе данных.

experiment-order-vending-name = Статистика торгового автомата
experiment-order-vending-desc = Отдел маркетинга собирает данные о продажах. Просканируйте автомат из отдела { $department } ({ $target }), а затем повторите скан через 10 минут.

experiment-order-meat-name = Дегустационная выборка (мясо)
experiment-order-meat-desc = Поварскому цеху нужны сканы пяти мясных блюд, чтобы улучшить рецептуру. Подойдут любые блюда, кроме стандартного жареного стейка.

experiment-order-baton-name = Заправка дубинки-шокера плазмой
experiment-order-baton-desc = Лаборатория безопасности изучает влияние плазмы на энергоносители. Требуется скан дубинки-шокера, в аккумуляторе которой не менее 5u плазмы.

experiment-order-seed-name = Геномный анализ растений
experiment-order-seed-desc = Ботаники запросили скан генома мутировавшего растения: { $target }.

experiment-order-radcollector-name = Замер радиационного коллектора
experiment-order-radcollector-desc = Энергоотдел собирает данные о работе коллекторов радиации. Требуется скан коллектора под нагрузкой не менее 6 рад.

experiment-order-canister-gas-name = Анализ газовой пробы
experiment-order-canister-gas-desc = Для проверки термодинамической модели нужен скан канистры с содержанием не менее 500 молей газа { $gas }.

experiment-order-captain-id-name = Инвентаризация капитанских карт
experiment-order-captain-id-desc = Участились случаи порчи капитанских ID-карт. Просканируйте карту капитана, чтобы подтвердить её подлинность.

experiment-order-weapon-name = Поиск бракованного оружия
experiment-order-weapon-desc = Поступила информация о бракованной партии вооружения. Просканируйте один из образцов: { $target }, чтобы проверить, не затронула ли она станцию.

experiment-order-paper-signatures-name = Обновление базы подписей
experiment-order-paper-signatures-desc = Кадровая служба обновляет базу сотрудников. Передайте скан документа с подписями минимум пяти разных лиц.

experiment-order-bible-name = Скан священного писания
experiment-order-bible-desc = Для архива отдела требуется скан любой святой книги.

experiment-order-fruit-dishes-name = Дегустационная выборка (фрукты)
experiment-order-fruit-dishes-desc = Кулинарный цех ищет самое витаминное фруктовое блюдо. Подготовьте и просканируйте пять фруктовых блюд.

experiment-order-soups-name = Дегустационная выборка (супы)
experiment-order-soups-desc = Поварскому цеху нужны сканы пяти супов, чтобы обновить меню. Предоставьте выборку из пяти блюд.

experiment-order-gravity-name = Контроль гравитационного поля
experiment-order-gravity-desc = Для отчёта требуется скан исправного и активного генератора гравитации.

experiment-order-combat-mech-name = Образец боевого меха
experiment-order-combat-mech-desc = СБ запросила подтверждение сборки боевого меха { $target } со всеми слотами оборудования. Сканируйте полностью оснащённого меха.

experiment-order-material-name = Контрольный скан материала
experiment-order-material-desc = Производственный отдел запросил эталонный скан материала категории: { $target }.

experiment-scanner-perform = Провести сканирование
experiment-scanner-processing = Обработка...

