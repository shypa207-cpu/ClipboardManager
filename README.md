# Clipboard Manager (Windows 10/11, .NET 8 + WPF)

Менеджер буфера обміну: історія тексту й зображень, спливаюче вікно біля каретки/курсора, закріплення, автовставка.

## Збірка та запуск
Потрібен Windows 10/11 та .NET 8 SDK.

    dotnet run -c Release
    # або самодостатній exe:
    dotnet publish -c Release -r win-x64 --self-contained false -o publish

## Використання
- Фонова робота, іконка в треї (ЛКМ — відкрити, ПКМ — меню: розмір історії, автозапуск, вихід).
- Гаряча клавіша за замовчуванням: **Ctrl+Shift+V**.
- Клік / Enter — вставка (елемент у буфер → вікно ховається → імітація Ctrl+V).
- Esc або клік поза вікном — сховати. Delete — видалити виділений елемент.
- Кнопки на елементі: закріпити / видалити. «Очистити все» видаляє лише незакріплені.

## Налаштування
`%LOCALAPPDATA%\ClipboardManager\settings.json`:
- `HistoryLimit` — ліміт історії (за замовчуванням 50);
- `HotkeyModifiers` — Alt=1, Ctrl=2, Shift=4, Win=8 (сумуються);
- `HotkeyKey` — віртуальний код клавіші (V = 86).
Змінюйте при закритій програмі.

## Дані
SQLite `history.db`, повні зображення в `images\*.png`, мініатюри в `thumbs\*.png` (в БД — лише шляхи). Лог помилок — `app.log`.

## Архітектура
- `Services/ClipboardMonitor` — AddClipboardFormatListener, retry при блокуванні буфера, ігнор власних записів та вмісту, позначеного ExcludeClipboardContentFromMonitorProcessing.
- `Services/HotkeyService`, `CaretLocator` (GetGUIThreadInfo → фолбек на курсор миші), `PasteService` (SendInput), `Storage`, `ImageStore`, `Autostart` (HKCU\...\Run).
- UI: Acrylic через DwmSetWindowAttribute (Win11 22H2+; на Win10 — суцільний фон), світла/темна тема за системною.

## Обмеження
- Каретку віддають не всі програми (Chrome/Electron/UWP часто ні) — тоді вікно з'являється біля миші.
- Вікно не вставить у програми, запущені від адміністратора, якщо менеджер запущено без прав адміністратора.
