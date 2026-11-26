using System.Security.Principal;

namespace NotepadApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var identity = WindowsIdentity.GetCurrent();
        using var instanceMutex = new Mutex(true, $@"Global\NotepadApp-{identity.User!.Value}", out bool ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("NotepadApp is already running.", "NotepadApp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotepadApp");
            using var form = new NotesForm(new NoteStore(Path.Combine(folder, "notes.json")));
            Application.Run(form);
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }
}
