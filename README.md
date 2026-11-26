# NotepadApp

A Windows Forms app for writing and organizing local notes.

## Run from source

Install the .NET 10 SDK on Windows. From the repository root, run:

```powershell
dotnet run --project NotepadApp.csproj
```

The project uses C#, Windows Forms, `System.IO`, and `System.Text.Json`. It has no external NuGet packages.

## Use the app

- Select **New note**, enter a title and content, and select **Save**. Select any note in the list to edit it.
- Select **Delete** and confirm to remove the selected note. Select **Save** to persist the deletion.
- Select **Export .txt** to save a UTF-8 copy of the selected note. The exported file contains the title and body.
- The app shows each note’s creation and last edit time. **Reload** reads the saved notes again. Closing or reloading prompts you to save pending changes.

## Local storage

Notes are stored in `%LOCALAPPDATA%\NotepadApp\notes.json` and loaded at startup. A missing file opens an empty workspace. If the file cannot be read, the app displays the error and disables saving. Repair the file or move it to a backup location, then select **Reload**.
