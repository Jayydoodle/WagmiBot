using CopyTradeBot;
using JConsole;
using Spectre.Console;
using System.Configuration;

class Program
{
    private const string ApplicationName = "CopyTrade Bot";
    private const string VersionNumber = "1.0";

    static async Task Main(string[] args)
    {
        WTelegram.Helpers.Log = (s, a) => RedirectLogs();

        Console.Clear();

        SelectionPrompt<MenuOption> prompt = new SelectionPrompt<MenuOption>();
        prompt.Title = "Select an option:";
        List<MenuOption> options = CreateListOptions();
        prompt.AddChoices(options);

        bool printMenuHeading = true;

        while (true)
        {
            if (printMenuHeading)
                PrintMenuHeading();

            MenuOption option = AnsiConsole.Prompt(prompt);

            if (option.Function != null || option.IsHelpOption)
            {
                try
                {
                    printMenuHeading = true;

                    if (option.IsHelpOption)
                    {
                        printMenuHeading = false;
                        AnsiConsole.Clear();
                        ((MenuOption<List<MenuOption>, bool>)option).Function(options);
                    }
                    else
                    {
                        option.Function();
                        AnsiConsole.Clear();
                    }
                }
                catch (Exception e)
                {
                    if (e.Message == GlobalConstants.Commands.EXIT)
                        break;
                    else

                        AnsiConsole.Clear();

                    if (e.Message != GlobalConstants.Commands.MENU)
                        AnsiConsole.Write(string.Format("{0}\n\n", e.Message));
                }
            }
            else
            {
                break;
            }
        }
    }

    private static void RedirectLogs()
    {
    }

    private static void PrintMenuHeading()
    {
        Rule rule = new Rule(string.Format("[green]{0} v{1}[/]\n", ApplicationName, VersionNumber)).DoubleBorder<Rule>();
        AnsiConsole.Write(rule);
    }

    private static List<MenuOption> CreateListOptions()
    {
        List<MenuOption> menuOptions = new List<MenuOption>();

        menuOptions.Add(TelegramManager.Instance);
        menuOptions.Add(ConsoleFunction.GetHelpOption());
        menuOptions.Add(new MenuOption(GlobalConstants.SelectionOptions.Exit, null));

        return menuOptions;
    }
}