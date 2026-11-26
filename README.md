# NotepadApp

A Windows Forms app for writing and organizing local notes.

## Run from source

Install the .NET 10 SDK on Windows. From the repository root, run:

```powershell
dotnet run --project NotepadApp.csproj
```

The project uses C#, Windows Forms, `System.IO`, and `System.Text.Json`. It has no external NuGet packages.

## Use the app

- Select **+ New note** to open a note in its own window. Select a card to reopen a note or focus its open window. Closing a note keeps it in the list.
- Search by title or text. **Ctrl+F** focuses the search box, and **Esc** clears it while the box has focus. Matching text is highlighted in the cards.
- Use the editor toolbar to apply bold, italic, underline, strikethrough, or bullets. Select **Image** to insert a picture at the cursor.
- Use **Options** in the editor to choose the note's accent color or delete the note. Deleting asks for confirmation.
- Notes save automatically shortly after a change. Use **Save all** to save immediately or retry after an error.
- Select **Export .txt** to save the selected note's title and plain text as UTF-8.

## Local storage

Notes are stored in `%LOCALAPPDATA%\NotepadApp\notes.json` and loaded at startup. Their colors, formatting, embedded images, and individual window positions and sizes are saved locally. The app shows the creation and last edit times in each editor.

A missing file opens an empty list. If the file cannot be read, the app displays an error and disables saving. Repair the file or move it to a backup location, then select **Reload**.
