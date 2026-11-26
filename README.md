# NotepadApp

NotepadApp is a Windows desktop app for keeping sticky notes. Write in independent post-it windows, find notes from a searchable list, and keep everything on your device.

## Screenshot

![NotepadApp showing the notes list alongside an active and an inactive post-it](docs/screenshots/notepadapp.png)

## Download for Windows

Download [NotepadApp v0.0.1 for Windows x64](https://github.com/Ohualtex/NotepadApp/releases/download/v0.0.1/NotepadApp-v0.0.1-win-x64.zip), extract the entire ZIP, and open `NotepadApp.exe`. The package includes the .NET runtime.

The `main` branch includes updates made after v0.0.1. To try the latest changes, run from source.

## Run from source

Install the .NET 10 SDK on Windows. From the repository root, run

```powershell
dotnet run --project NotepadApp.csproj
```

The app uses C#, Windows Forms, and the .NET standard library.

## Use the app

- Select **+** in the list or a post-it to create a note. The new note opens ready to type. To add an optional title, click the top bar or press **F2**.
- Drag a post-it by its top bar and resize it from its edges. Each note remembers its own position and size. The top bar and formatting toolbar appear when the note has focus.
- Select a card to open its note or bring it forward. Closing the list leaves open notes on your desktop. Use **⋯ → Notes list** from a post-it to bring back the list.
- Search by title or text with **Ctrl+F**. Matches are highlighted in the cards, including rich-text previews. Press **Esc** while the search box has focus to clear it.
- Use the formatting toolbar for bold, italic, underline, strikethrough, and bullets. Use the image icon to insert a picture at the cursor.
- Open **⋯** for note colors, **Save all**, **Export .txt**, and **Delete note**. The export contains a title line and the note's plain text in UTF-8.
- Open the gear in the list to choose **Light**, **Dark**, or **System** and control whether deleting a note asks for confirmation. **Local storage** contains **Save all** and **Reload**.

## Local storage

Changes save automatically after a short pause. Use **Save all** to write changes immediately or retry after an error.

| File | Contents |
| --- | --- |
| `%LOCALAPPDATA%\NotepadApp\notes.json` | Note text, formatting, embedded images, colors, timestamps, and window positions and sizes |
| `%LOCALAPPDATA%\NotepadApp\settings.json` | Theme and delete confirmation preferences |

If the notes file cannot be read, NotepadApp reports the error and disables saving. Repair the file or move it to a backup location, then choose **Settings → Local storage → Reload**.

## Project structure

| Files | Role |
| --- | --- |
| `Program.cs`, `NotepadApp.csproj` | Application entry point and WinForms project settings |
| `TaskbarAppearance.cs`, `Assets/Taskbar.ico` | Windows taskbar icon and shell integration |
| `NotesForm.cs`, `BufferedNoteList.cs`, `NoteSearch.cs` | Note list, buffered card drawing, search, and coordination of open editors |
| `NotePreviewRenderer.cs`, `NoteRichContent.cs` | Cached previews that preserve formatting, images, and search highlights |
| `NoteEditorForm.cs`, `NoteOptionsPanel.cs`, `ResizableNoteWindow.cs` | Post-it editor, in-note panel, and resizable window frame |
| `NotesSettingsView.cs`, `AppPreferences.cs`, `NotesTheme.cs` | Settings UI, user preferences, and light/dark palettes |
| `Note.cs`, `NoteWindowPlacement.cs`, `NoteStore.cs` | Note model, saved window geometry, JSON persistence, and text export |
| `NoteButtons.cs`, `NotePalette.cs`, `RichTextFormatting.cs`, `NoteImage.cs` | Shared controls, note colors, text formatting, and image embedding |
