using Telegram.Bot;
using WTelegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TL;
using ReplyKeyboardMarkup = Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup;
using KeyboardButton = Telegram.Bot.Types.ReplyMarkups.KeyboardButton;
using Message = Telegram.Bot.Types.Message;
using Update = Telegram.Bot.Types.Update;
using JConsole;
using Spectre.Console;
using Solnet.JupiterSwap;
using Solnet.JupiterSwap.Models;

namespace WagmiBot
{
    public class WagmiClientManager : ManagerBase<WagmiClientManager>
    {
        #region Properties

        private WagmiClient WagmiClient;
        private Task RunTask { get; set; }
        public override string Documentation => Docs;

        #endregion

        #region Life Cycle

        protected override bool Initialize()
        {
            string apiKeyString = XMLSettings.GetSetting(Settings.TelegramAPIKey);
            string apiHash = XMLSettings.GetSetting(Settings.TelegramAPIHash);
            string botToken = XMLSettings.GetSetting(Settings.WagmiBotToken);

            if (string.IsNullOrEmpty(apiKeyString))
                throw new ArgumentNullException(apiKeyString);
            if (string.IsNullOrEmpty(apiHash))
                throw new ArgumentNullException(apiHash);
            if (string.IsNullOrEmpty(botToken))
                throw new ArgumentNullException(botToken);

            int apiKey = int.Parse(apiKeyString);

            if (WagmiClient == null)
                WagmiClient = new WagmiClient(botToken, apiHash, apiKey);

            return true;
        }

        protected override List<MenuOption> GetMenuOptions()
        {
            List<MenuOption> menuOptions = new List<MenuOption>();

            if (RunTask == null)
                menuOptions.Add(new MenuOption(nameof(StartBot).SplitByCase(), StartBot));

            menuOptions.Add(new MenuOption(nameof(GetToken).SplitByCase(), GetToken));
            //menuOptions.Add(new MenuOption(nameof(GetAllTokens).SplitByCase(), GetAllTokens));

            return menuOptions;
        }

        [Documentation(StartBotDocumentation)]
        private void StartBot()
        {
            if (RunTask == null)
                RunTask = Task.Run(() => WagmiClient.StartBotAsync());

            WriteHeaderToConsole();
        }

        private void GetToken()
        {
            string tokenAddress = ConsoleUtil.GetInput("Enter the token address or symbol");

            TokenData token = null;
            
            if (tokenAddress.StartsWith("$"))
                token = Task.Run(() => JupiterDexAg.Instance.GetTokenBySymbol(tokenAddress)).Result;
            else 
                token = Task.Run(() => JupiterDexAg.Instance.GetTokenByMint(tokenAddress)).Result;

            if (token != null)
                AnsiConsole.MarkupLine("\n[blue]Token:[/] {0}\n[blue]Token:[/] {1}\n[blue]CA:[/] {1}\n[blue]Symbol:[/] {2}", token.Name, token.Mint, token.Symbol);
            else
                AnsiConsole.MarkupLine("[red]Token was not found[/]");
        }

        private void GetAllTokens()
        {
            var token = Task.Run(() => JupiterDexAg.Instance.GetTokens()).Result;
        }

        protected override void WriteHeaderToConsole()
        {
            base.WriteHeaderToConsole();

            if (RunTask != null)
                AnsiConsole.MarkupLine("[green]{0} is running[/]\n", nameof(WagmiClient));
            else
                AnsiConsole.MarkupLine("[orange1]{0} is not running[/]\n", nameof(WagmiClient));
        }

        #endregion

        #region Documentation 

        private const string Docs = "An interface for configuring and monitoring the WagmiBot Telegram bot";
        private const string StartBotDocumentation = "Starts the instance of the bot and starts responding to user input from Telegram";

        #endregion
    }
}
