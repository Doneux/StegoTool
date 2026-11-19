namespace StegoTool;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System); // follow Windows light/dark setting
        Application.Run(new MainForm());
    }
}
