# Notepad

A Windows 11 Notepad look-alike in two versions: a single-file web app and a WinForms desktop app.

The feature set was mapped from Notepad's `NotepadXamlUI.winmd` metadata (analyzed with [REA](https://github.com/morluto/rea)), and the colors and layout were measured from a screenshot of the real app. No Microsoft code or assets are included.

## Features

- Tabs with a `+` button, unsaved-change dot, and caption buttons (minimize, maximize, close)
- Menus: File, Edit, View
- Formatting toolbar: headings, lists, bold, italic, strikethrough, link, table, clear formatting. Formatting is stored as Markdown and shown in a preview (Ctrl+Shift+M).
- Find and replace (match case, wrap around, replace all), go to line, time/date (F5)
- Zoom, word wrap, status bar, light and dark themes
- Status bar: line/column, character count, file type, zoom, line ending (CRLF/LF/CR), encoding (UTF-8, UTF-8 BOM, UTF-16, ANSI)
- Recent files

## Web (`web/index.html`)

Open `web/index.html` in a browser. It needs no build step or dependencies.

- In Chrome or Edge, opening and saving writes straight to the original file (File System Access API). Other browsers download the file instead.
- Open tabs are kept in the browser's local storage.
- The browser reserves Ctrl+N and Ctrl+W, so new tab is **Alt+N** and close tab is **Alt+W**.

## Desktop (`desktop/`)

The app is built with the C# compiler included in Windows (.NET Framework 4.x), so nothing needs to be installed.

```powershell
cd desktop
& C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001 /nowarn:108 /out:Notepad.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll Notepad.cs
.\Notepad.exe
```

`shot.ps1` builds the app, opens it at 1000×600 and saves a screenshot to `%TEMP%\clone_np.png`. If the execution policy blocks it:

```powershell
powershell -ExecutionPolicy Bypass -File .\desktop\shot.ps1
```

The desktop app uses the Segoe UI Variable and Segoe Fluent Icons fonts that ship with Windows 11.

## Not included

- Copilot / AI writing features
- Live (in-editor) rendering of formatting
- Spell check
