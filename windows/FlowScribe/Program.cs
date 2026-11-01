namespace FlowScribe;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One copy at a time: two would both answer the hotkey and paste twice.
        using var single = new Mutex(true, @"Local\FlowScribe", out var isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
