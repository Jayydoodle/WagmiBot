using WagmiBot;
using JConsole;
using JConsole.Console;
using Spectre.Console;
using System.Configuration;

class Program
{
    private const string ApplicationName = "Wagmi Bot";
    private const string VersionNumber = "1.0";

    static void Main(string[] args)
    {
        WTelegram.Helpers.Log = (s, a) => RedirectLogs();

        ConsoleProgram program = new ConsoleProgram();
        program.ApplicationName = ApplicationName;
        program.VersionNumber = VersionNumber;
        program.MenuOptions = GetMenuOptions();
        program.Run();
    }

    private static void RedirectLogs()
    {
    }

    private static List<MenuOption> GetMenuOptions()
    {
        List<MenuOption> menuOptions = new List<MenuOption>();

        menuOptions.Add(WagmiClientManager.Instance);
        menuOptions.Add(UserClientManager.Instance);
        menuOptions.Add(ConsoleFunction.GetHelpOption());
        menuOptions.Add(new MenuOption(GlobalConstants.SelectionOptions.Exit, null));

        return menuOptions;
    }
}