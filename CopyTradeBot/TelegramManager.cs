using JConsole;
using Newtonsoft.Json;
using OfficeOpenXml;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegram;
using JConsole.Settings;
using static System.Runtime.InteropServices.JavaScript.JSType;
using CopyTradeBot.BAL;

namespace CopyTradeBot
{
    public class TelegramManager : ManagerBase<TelegramManager>
    {
        #region Properties

        public WTelegram.Client TelegramClient { get; set; }
        public User CurrentUser { get; set; }
        public override string DisplayName => "Telegram Manager";

        #endregion

        #region Life Cycle

        [Documentation(Documentation)]
        public override void Run()
        {
            LoginUser();
            RunProgramLoop();
        }

        #endregion

        #region Private API

        private string Config(string what)
        {
            switch (what)
            {
                case "api_id": return XMLSettings.GetSetting(Settings.TelegramAPIKey, Validate);
                case "api_hash": return XMLSettings.GetSetting(Settings.TelegramAPIHash, Validate);
                case "phone_number": return XMLSettings.GetSetting(Settings.TelegramPhoneNumber, Validate);
                case "verification_code": Console.Write("Enter the verification code sent to your Telegram app: "); return Console.ReadLine();
                default: return null; // let WTelegramClient decide the default config
            }
        }

        private void LoginUser()
        {
            if (TelegramClient == null)
                TelegramClient = new WTelegram.Client(Config);

            try
            {
                if (CurrentUser == null)
                    CurrentUser = Task.Run<User>(async () => await TelegramClient.LoginUserIfNeeded()).Result;
            }
            catch (Exception e)
            {
                e.LogException();
            }

            if (CurrentUser != null)
                AnsiConsole.MarkupLine("You are now logged in as as [blue]" + CurrentUser + "[/] (id " + CurrentUser.id + ")\n");
            else
                AnsiConsole.MarkupLine("\nAn error occurred while attempting to login.  Please select 'change user' and try again.\n");
        }

        private bool Validate(Settings node, string value)
        {
            bool validated = true;

            if (node.Name == Settings.TelegramPhoneNumber.Name)
            {
                validated = value.All(x => char.IsDigit(x));

                if (!validated)
                    Console.WriteLine("Invalid phone number.");
            }

            return validated;
        }

        protected override List<MenuOption> GetMenuOptions()
        {
            List<MenuOption> menuOptions = new List<MenuOption>();

            menuOptions.Add(new MenuOption(nameof(ChangeUser).SplitByCase(), ChangeUser));
            menuOptions.Add(new MenuOption(nameof(ShowChats).SplitByCase(), ShowChats));

            return menuOptions;
        }

        private void ShowChats()
        {
            foreach (var chat in TelegramChat.GetAll(TelegramClient))
                AnsiConsole.MarkupLine("[blue]{0}[/]\t{1}", chat.ID, chat.Title);
        }

        private void ChangeUser()
        {
            XMLSettings.Update(Settings.TelegramPhoneNumber, string.Empty);

            if (TelegramClient != null)
                TelegramClient.Reset();

            CurrentUser = null;

            LoginUser();
        }

        #endregion

        #region Documentation 

        private const string Documentation = "api_id: your application api ID from my.telegram.org/apps\n" +
            "api_hash: your api hash from my.telegram.org/apps\n" +
            "phone_number: the phone number of your telegram account\n" +
            "verification_code: the verification code sent to your telegram app";

        #endregion
    }
}
