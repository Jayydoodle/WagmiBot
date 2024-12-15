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
using static System.Runtime.InteropServices.JavaScript.JSType;
using CopyTradeBot.BAL;

namespace CopyTradeBot
{
    public class TelegramManager : ManagerBase<TelegramManager>
    {
        #region Properties

        public WTelegram.Client TelegramClient { get; set; }
        public User CurrentUser { get; set; }
        public TelegramChat SelectedChat { get; set; }
        public override string DisplayName => "Telegram Manager";

        #endregion

        #region Life Cycle

        [Documentation(Documentation)]
        protected override bool Initialize()
        {
            LoginUser();
            return true;
        }

        #endregion

        #region Private API

        protected override List<MenuOption> GetMenuOptions()
        {
            List<MenuOption> menuOptions = new List<MenuOption>
            {
                new MenuOption(nameof(ChangeUser).SplitByCase(), ChangeUser),
                new MenuOption(nameof(ShowChats).SplitByCase(), ShowChats),
                new MenuOption(nameof(SelectChat).SplitByCase(), SelectChat),
                new MenuOption(nameof(SendMessageToChat).SplitByCase(), SendMessageToChat)
            };

            return menuOptions;
        }

        protected override void WriteHeaderToConsole()
        {
            base.WriteHeaderToConsole();

            if (CurrentUser != null)
                AnsiConsole.MarkupLine("You are logged in as as [blue]" + CurrentUser + "[/] (id " + CurrentUser.id + ")\n");
            else
                AnsiConsole.MarkupLine("\nAn error occurred while attempting to login.  Please select 'change user' and try again.\n");

            if (SelectedChat != null)
                AnsiConsole.MarkupLine("You are connected to the chat [blue]{0}[/]\n", SelectedChat.Title);
        }

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

        private bool Validate(Settings node, string value)
        {
            bool validated = true;

            if (node.Name == Settings.TelegramPhoneNumber.Name)
            {
                validated = value.IsNumeric();

                if (!validated)
                    AnsiConsole.WriteLine("Invalid phone number.");
            }

            if (node.Name == Settings.TelegramChatId.Name)
            {
                validated = value.IsNumeric();

                if (!validated)
                    AnsiConsole.WriteLine("Invalid chat Id.");
            }

            return validated;
        }

        private void LoginUser()
        {
            if (TelegramClient == null)
                TelegramClient = new WTelegram.Client(Config);

            try
            {
                if (CurrentUser == null)
                    CurrentUser = Task.Run<User>(async () => await TelegramClient.LoginUserIfNeeded()).Result;

                LoadChat();
            }
            catch (Exception e)
            {
                e.LogException();
            }
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

            SelectedChat = null;
            CurrentUser = null;

            LoginUser();
        }

        private void SelectChat()
        {
            XMLSettings.Update(Settings.TelegramChatId, string.Empty);
            string chatId = XMLSettings.GetSetting(Settings.TelegramChatId, Validate);
            LoadChat(chatId);

            if (SelectedChat == null)
                Logger.LogWarning(string.Format("A chat with the ID: [red]{0}[/] was not found", chatId));
            else
                WriteHeaderToConsole();
        }

        private void LoadChat(string chatId = null)
        {
            if (chatId == null)
                chatId = XMLSettings.GetSetting(Settings.TelegramChatId);

            if (!string.IsNullOrEmpty(chatId))
                SelectedChat = TelegramChat.GetAll(TelegramClient).FirstOrDefault(x => x.ID == long.Parse(chatId));
        }

        private void SendMessageToChat()
        {
            InputPeer target = SelectedChat.Instance;
            InputReplyToMessage replyTo = null;

            if (SelectedChat.Topics.Any())
            {
                SelectionPrompt<ForumTopic> prompt = new SelectionPrompt<ForumTopic>();
                prompt.Title = "Select the topic you want to send the message in";
                prompt.Converter = x => x.title;
                prompt.AddChoices(SelectedChat.Topics);

                ForumTopic choice = AnsiConsole.Prompt(prompt);

                if (choice.id != 1)
                    replyTo = new InputReplyToMessage() { top_msg_id = choice.id, reply_to_msg_id = choice.id, flags = InputReplyToMessage.Flags.has_top_msg_id };
            }

            while (true)
            {
                string text = ConsoleUtil.GetInput("Enter the text you want to sent to the channel");
                Task.Run<UpdatesBase>(async () => await TelegramClient.Messages_SendMessage(target, text, Helpers.RandomLong(), replyTo));
            }
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
