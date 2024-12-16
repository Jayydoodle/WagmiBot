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
using Solnet.Wallet;
using System.Collections.Concurrent;
using SharpCompress;

namespace CopyTradeBot
{
    public class TelegramManager : ManagerBase<TelegramManager>
    {
        #region Properties

        public override string DisplayName => "Telegram Manager";

        #endregion

        #region Properties: Telegram Client

        private WTelegram.Client TelegramClient { get; set; }
        private UpdateManager UpdateManager { get; set; }
        private User CurrentUser { get; set; }
        private TelegramChat SourceChat { get; set; }
        private TelegramChat DestinationChat { get; set; }
        private ForumTopic SelectedTopic { get; set; }

        #endregion

        #region Properties: Concurrency

        private static ConcurrentQueue<string> ContractAddressQueue = new ConcurrentQueue<string>();
        private static SemaphoreSlim SourceSemaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region Life Cycle

        [Documentation(Documentation)]
        protected override bool Initialize()
        {
            if (TelegramClient == null) 
                TelegramClient = new WTelegram.Client(Config);

            LoginUser();
            return true;
        }

        #endregion

        #region Private API: Menu Options

        protected override List<MenuOption> GetMenuOptions()
        {
            List<MenuOption> menuOptions = new List<MenuOption>
            {
                new MenuOption(nameof(ChangeUser).SplitByCase(), ChangeUser),
                new MenuOption("Start/Stop Listener", StartStopListener),
                new MenuOption(nameof(ShowChannels).SplitByCase(), ShowChannels),
                new MenuOption(nameof(SelectSourceChannel).SplitByCase(), SelectSourceChannel),
                new MenuOption(nameof(SelectDestinationChannel).SplitByCase(), SelectDestinationChannel),
                new MenuOption(nameof(SendMessageToChat).SplitByCase(), SendMessageToChat),
                new MenuOption(nameof(ViewQueuedAddresses).SplitByCase(), ViewQueuedAddresses)
            };

            return menuOptions;
        }

        [Documentation(ChangeUserDocumentation)]
        private void ChangeUser()
        {
            XMLSettings.Update(Settings.TelegramPhoneNumber, string.Empty);

            if (TelegramClient != null)
                TelegramClient.Reset();

            SourceSemaphore.Wait();
            SourceChat = null;
            SourceSemaphore.Release();

            CurrentUser = null;

            LoginUser();
        }

        [Documentation(SelectSourceChannelDocumentation)]
        private void SelectSourceChannel()
        {
            SourceSemaphore.Wait();
            SourceChat = SelectChat(Settings.TelegramSourceChannelId);
            SourceSemaphore.Release();
        }

        [Documentation(SelectDestinationChannelDocumentation)]
        private void SelectDestinationChannel()
        {
            DestinationChat = SelectChat(Settings.TelegramDestinationChannelId);
        }

        [Documentation(SendMessageToChatDocumentation)]
        private void SendMessageToChat()
        {
            InputPeer target = SourceChat.Instance;
            InputReplyToMessage replyTo = null;

            if (SourceChat.Topics.Any())
            {
                SelectionPrompt<ForumTopic> prompt = new SelectionPrompt<ForumTopic>();
                prompt.Title = "Select the topic you want to send the message in";
                prompt.Converter = x => x.title;
                prompt.AddChoices(SourceChat.Topics);

                ForumTopic choice = AnsiConsole.Prompt(prompt);

                if (choice.id != 1)
                    replyTo = new InputReplyToMessage() { top_msg_id = choice.id, reply_to_msg_id = choice.id, flags = InputReplyToMessage.Flags.has_top_msg_id };
            }

            while (true)
            {
                string text = ConsoleUtil.GetInput("Enter the text you want to sent to the channel.  Enter [red]CANCEL[/] to stop");
                Task.Run<UpdatesBase>(async () => await TelegramClient.Messages_SendMessage(target, text, Helpers.RandomLong(), replyTo));
            }
        }

        [Documentation(ShowChannelsDocumentation)]
        private void ShowChannels()
        {
            foreach (var chat in TelegramChat.GetAll(TelegramClient))
                AnsiConsole.MarkupLine("[blue]{0}[/]\t{1}", chat.ID, chat.Title);
        }

        [Documentation(StartStopListenerDocumentation)]
        private void StartStopListener()
        {
            if (UpdateManager == null)
            {
                UpdateManager = TelegramClient.WithUpdateManager(Client_OnUpdate);
            }
            else
            {
                UpdateManager.StopResync();
                UpdateManager = null;
            }

            WriteHeaderToConsole();
        }

        [Documentation(ViewQueuedAddressesDocumentation)]
        private void ViewQueuedAddresses()
        {
            if (!ContractAddressQueue.Any())
                AnsiConsole.MarkupLine("[green]No addresses currently in the queue.[/]");
            else
                AnsiConsole.MarkupLine("[yellow]----- Queued Addresses -----[/]");

            int position = 1;
            ContractAddressQueue.ForEach(address =>
            {
                AnsiConsole.MarkupLine("[blue]{0}[/]: {1}", position, address);
                position++;
            });
        }

        #endregion

        #region Private API

        private async Task Client_OnUpdate(Update update)
        {
            switch (update)
            {
                case UpdateNewMessage unm: await HandleMessage(unm.message); break;
            }

            await Task.CompletedTask;
        }

        private Task HandleMessage(MessageBase messageBase, bool edit = false)
        {
            SourceSemaphore.WaitAsync();

            if (SourceChat == null || messageBase.Peer.ID != SourceChat.ID)
                return Task.CompletedTask;

            SourceSemaphore.Release();

            if(!(messageBase is Message message && !string.IsNullOrEmpty(message.message)))
                return Task.CompletedTask;

            bool foundSolanaAddress = false;
            PublicKey publicKey = null;

            foreach (string piece in message.message.Split(new char[] { '\n', '\t', '\r' }))
            {
                publicKey = new PublicKey(piece);
                foundSolanaAddress = publicKey != null && publicKey.IsValid();

                if (foundSolanaAddress)
                    break;
            }

            if (foundSolanaAddress)
                ContractAddressQueue.Enqueue(publicKey.Key);

            return Task.CompletedTask;
        }

        protected override void WriteHeaderToConsole()
        {
            base.WriteHeaderToConsole();

            if (CurrentUser != null)
                AnsiConsole.MarkupLine("You are logged in as as [blue]" + CurrentUser + "[/] (id " + CurrentUser.id + ")\n");
            else
                AnsiConsole.MarkupLine("\nAn error occurred while attempting to login.  Please select 'change user' and try again.\n");

            if (SourceChat != null)
                AnsiConsole.MarkupLine("You are listening to the chat [blue]{0}[/]\n", SourceChat.Title);

            if(UpdateManager != null)
                AnsiConsole.MarkupLine("[green]Listener is running[/]\n");
            else
                AnsiConsole.MarkupLine("[orange1]Listener is stopped[/]\n");
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

            if (node.Name == Settings.TelegramSourceChannelId.Name)
            {
                validated = value.IsNumeric();

                if (!validated)
                    AnsiConsole.WriteLine("Invalid chat Id.");
            }

            return validated;
        }

        private void LoginUser()
        {
            try
            {
                if (CurrentUser == null)
                    CurrentUser = Task.Run<User>(async () => await TelegramClient.LoginUserIfNeeded()).Result;

                SourceSemaphore.Wait();
                SourceChat = LoadChat(Settings.TelegramSourceChannelId);
                SourceSemaphore.Release();
            }
            catch (Exception e)
            {
                e.LogException();
            }
        }

        private TelegramChat SelectChat(Settings setting)
        {
            TelegramChat chat = null;

            XMLSettings.Update(setting, string.Empty);
            string chatId = XMLSettings.GetSetting(setting, Validate);
            chat = LoadChat(setting, chatId);

            if (chat == null)
                Logger.LogWarning(string.Format("A chat with the ID: [red]{0}[/] was not found", chatId));
            else
                WriteHeaderToConsole();

            return chat;
        }

        private TelegramChat LoadChat(Settings setting, string chatId = null)
        {
            TelegramChat chat = null;

            if (chatId == null)
                chatId = XMLSettings.GetValue(setting);

            if (!string.IsNullOrEmpty(chatId))
                chat = TelegramChat.GetAll(TelegramClient).FirstOrDefault(x => x.ID == long.Parse(chatId));

            return chat;
        }

        #endregion

        #region Documentation 

        private const string Documentation = "api_id: your application api ID from my.telegram.org/apps\n" +
            "api_hash: your api hash from my.telegram.org/apps\n" +
            "phone_number: the phone number of your telegram account\n" +
            "verification_code: the verification code sent to your telegram app";
        private const string SelectSourceChannelDocumentation = "Select the channel that the listener will point to to read contract addresses";
        private const string SelectDestinationChannelDocumentation = "Select the channel hosting the bot you will use to send contract addresses to for buying tokens";
        private const string SendMessageToChatDocumentation = "Sends a message to your selected chat";
        private const string ShowChannelsDocumentation = "Shows the list of telegram channels that you belong to";
        private const string StartStopListenerDocumentation = "Starts or stops the listener that will read contract addresses from your source Telegram channel";
        private const string ViewQueuedAddressesDocumentation = "View the list of contract addresses currently in the queue";
        private const string ChangeUserDocumentation = "Prompts to enter in your user information to login to your Telegram account";

        #endregion
    }
}
