# Claude usage widget

Небольшой виджет для Windows: окошко поверх остальных окон, которое показывает, сколько израсходовано из лимитов Claude — 5‑часового и недельного, со шкалой и временем до сброса. Окошко видно только пока открыто приложение Claude для рабочего стола.

```
5 ч     ━━━━━━━━──────  66%  сброс через 3ч 19м
Неделя  ━━━━━─────────  40%  сброс через 4д 22ч
обновлено только что
```

Шкала оформлена как на странице «Использование» в настройках Claude: тонкая синяя полоска, от 80% жёлтая, от 95% красная.

## Как это работает

Лимиты Claude сообщает только вместе с ответами. Поэтому в комплекте два компонента:

1. **Плагин для Claude Code** `usage-limits-writer` (папка `claude-code-plugin`). Он ничего не показывает: после каждого ответа в Claude Code записывает проценты и время сброса в `%USERPROFILE%\.claude\usage-limits.json`.
2. **Виджет** (`usage-widget.ps1`) каждые 5 секунд перечитывает этот файл и показывает цифры.

Цифры обновляются при ответах в Claude Code. Сообщения в обычном чате расходуют тот же лимит, но виджет увидит это со следующим ответом в Claude Code.

## Установка

### 1. Плагин для Claude Code

В терминале с Claude Code:

```
/plugin install usage-limits-writer --marketplace ChetverukhinGS/claude-usage-widget
```

Или из командной строки:

```
claude plugin marketplace add ChetverukhinGS/claude-usage-widget
claude plugin install usage-limits-writer@claude-usage-widget
```

В приложении Claude для рабочего стола: «+» рядом с полем ввода → Plugins → Add marketplace → `ChetverukhinGS/claude-usage-widget`, затем добавьте плагин `usage-limits-writer`.

### 2. Виджет

Требуется Windows 10 или 11 и Windows PowerShell 5.1 (есть в системе).

1. Скачайте репозиторий (Code → Download ZIP) и распакуйте в любую папку.
2. Дважды щёлкните `start-widget.vbs`. Запускайте именно его, а не `.ps1` напрямую: он читает скрипт в UTF‑8, иначе русские надписи могут сломаться.

Окошко можно перетащить мышкой, позиция запоминается. Правый клик → «Закрыть виджет». Одновременно работает только одна копия. Окошко держится поверх всех окон и панели задач; меню «Пуск» Windows всё равно открывается поверх него — это системный слой, обычным программам туда хода нет. Без файла с данными виджет пишет «Нет данных».

Для автозапуска положите ярлык на `start-widget.vbs` в папку `shell:startup` (Win+R → `shell:startup`).

## Формат данных

`%USERPROFILE%\.claude\usage-limits.json`:

```json
{
  "limits": [
    { "kind": "five_hour", "percentUsed": 66, "resetsAt": "2026-11-06T01:10:00.000Z" },
    { "kind": "seven_day", "percentUsed": 40, "resetsAt": "2026-11-10T20:00:00.000Z" }
  ],
  "seenAt": 1793914351227
}
```

- `kind`: `five_hour` или `seven_day`.
- `percentUsed`: от 0 до 100.
- `resetsAt`: время сброса в формате ISO 8601.
- `seenAt`: когда получены цифры, в миллисекундах Unix.

## Файлы

- `usage-widget.ps1` — сам виджет (WPF).
- `start-widget.vbs` — запуск без окна консоли.
- `claude-code-plugin/` — плагин `usage-limits-writer` для Claude Code.
- `.claude-plugin/marketplace.json` — описание репозитория как источника плагинов.
- Позиция окна хранится в `%USERPROFILE%\.claude\usage-widget-pos.json`.

## Лицензия

MIT, см. [LICENSE](LICENSE).
