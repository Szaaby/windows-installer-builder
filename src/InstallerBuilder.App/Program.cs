using System.Windows.Forms;

namespace InstallerBuilder.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // CI-hez: felépíti a teljes felületet egy mintaprofillal, megjeleníti, majd bezárja. Hibánál 1-es kóddal lép ki.
        if (args.Contains("--selftest"))
        {
            try
            {
                using var form = new MainForm();
                form.LoadSampleForSelfTest();
                form.Show();
                Application.DoEvents();
                foreach (var page in form.AllPagesForSelfTest())
                {
                    form.ShowPageForSelfTest(page);
                    Application.DoEvents();
                }
                form.Close();
                Console.WriteLine("selftest ok");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        Application.Run(new MainForm(args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))));
        return 0;
    }
}
