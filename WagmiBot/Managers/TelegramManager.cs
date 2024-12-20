//using JConsole;
//using Newtonsoft.Json;
//using OfficeOpenXml;
//using Spectre.Console;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;
//using System.Linq;
//using System.Net;
//using System.Text;
//using System.Threading.Tasks;
//using TL;
//using WTelegram;
//using static System.Runtime.InteropServices.JavaScript.JSType;
//using Solnet.Wallet;
//using System.Collections.Concurrent;
//using SharpCompress;

//namespace WagmiBot
//{
//    public class TelegramManager : ManagerBase<TelegramManager>
//    {
//        #region Properties: Telegram Client

//        private WTelegram.Client TelegramClient { get; set; }
//        private UpdateManager UpdateManager { get; set; }
//        private User CurrentUser { get; set; }
//        private TelegramChannel SourceChannel { get; set; }

//        #endregion

//        #region Properties: Concurrency

//        private static ConcurrentQueue<string> ContractAddressQueue = new ConcurrentQueue<string>();
//        private static ConcurrentBag<string> SeenAddresses = new ConcurrentBag<string>();
//        private static SemaphoreSlim ChannelSemaphore = new SemaphoreSlim(1, 1);

//        #endregion

//        #region Life Cycle

//        [Documentation(Documentation)]
//        protected override bool Initialize()
//        {
//            if (TelegramClient == null)
//                TelegramClient = new WTelegram.Client(Config);

//            LoginUser();
//            return true;
//        }

//        #endregion

//        #region Private API: Menu Options

//        protected override List<MenuOption> GetMenuOptions()
//        {
//            List<MenuOption> menuOptions = new List<MenuOption>();
//            menuOptions.Add(new MenuOption(nameof(ChangeUserAccount).SplitByCase(), ChangeUserAccount));
//            menuOptions.Add(new MenuOption(nameof(SelectChannel).SplitByCase(), SelectChannel));
//            menuOptions.Add(new MenuOption(nameof(ViewChannels).SplitByCase(), ViewChannels));

//            ChannelSemaphore.Wait();

//            if (SourceChannel != null)
//            {
//                menuOptions.Add(new MenuOption("Start/Stop Listener", StartStopListener));
//                menuOptions.Add(new MenuOption(nameof(SelectChannelTopic).SplitByCase(), () => SelectChannelTopic(true)));
//                menuOptions.Add(new MenuOption(nameof(SelectChannelUser).SplitByCase(), () => SelectChannelUser(true)));
//                menuOptions.Add(new MenuOption(nameof(SendMessageToChannel).SplitByCase(), SendMessageToChannel));
//                menuOptions.Add(new MenuOption(nameof(ViewQueuedAddresses).SplitByCase(), ViewQueuedAddresses));
//            }

//            ChannelSemaphore.Release();

//            return menuOptions.OrderBy(x => x.DisplayName).ToList();
//        }

//        [Documentation(ChangeUserAccountDocumentation)]
//        private void ChangeUserAccount()
//        {
//            XMLSettings.Update(Settings.TelegramPhoneNumber, string.Empty);

//            if (TelegramClient != null)
//                TelegramClient.Reset();

//            ChannelSemaphore.Wait();

//            SourceChannel = null;
//            XMLSettings.Update(Settings.TelegramChannelId, string.Empty);
//            XMLSettings.Update(Settings.TelegramTopicId, string.Empty);
//            XMLSettings.Update(Settings.TelegramTargetUserId, string.Empty);

//            ChannelSemaphore.Release();

//            CurrentUser = null;

//             LoginUser();
//        }

//        [Documentation(SelectChannelDocumentation)]
//        private void SelectChannel()
//        {
//            ChannelSemaphore.Wait();

//            try
//            {
//                SourceChannel = SelectChat(Settings.TelegramChannelId);
//                SelectChannelTopic(false);

//                if (SourceChannel != null)
//                {
//                    ChannelSemaphore.Release();
//                    WriteHeaderToConsole();
//                }
//                else
//                {
//                    ChannelSemaphore.Release();
//                }
//            }
//            catch (Exception)
//            {
//                ChannelSemaphore.Release();
//                throw;
//            }
//        }

//        [Documentation(SelectChannelTopicDocumentation)]
//        private void SelectChannelTopic(bool fromMenuAction = false)
//        {
//            if (fromMenuAction)
//                ChannelSemaphore.Wait();

//            if (SourceChannel != null)
//            {
//                List<ForumTopic> topicList = SourceChannel.Topics;
//                SourceChannel.SelectedTopic = null;

//                if (topicList != null && topicList.Any())
//                {
//                    SelectionPrompt<ForumTopic> prompt = new SelectionPrompt<ForumTopic>();
//                    prompt.Title = "\nSelect the topic you want the listener to target:";
//                    prompt.Converter = x => x.title;
//                    prompt.AddChoice(new ForumTopic() { id = -1, title = "-- REMOVE CURRENT TOPIC --" });
//                    prompt.AddChoices(topicList);

//                    ForumTopic choice = AnsiConsole.Prompt(prompt);

//                    if (choice.id == -1)
//                    {
//                        SourceChannel.SelectedTopic = null;
//                        XMLSettings.Update(Settings.TelegramTopicId, string.Empty);
//                    }
//                    else
//                    {
//                        SourceChannel.SelectedTopic = choice;
//                        XMLSettings.Update(Settings.TelegramTopicId, choice.id.ToString());
//                    }
//                }
//            }

//            if (fromMenuAction)
//            {
//                ChannelSemaphore.Release();
//                WriteHeaderToConsole();
//            }
//        }

//        [Documentation(SelectChannelUserDocumentation)]
//        private void SelectChannelUser(bool fromMenuAction = false)
//        {
//            if (fromMenuAction)
//                ChannelSemaphore.Wait();

//            if (SourceChannel != null)
//            {
//                List<User> userList = SourceChannel.Users;
//                SourceChannel.SelectedUser = null;

//                if (userList != null && userList.Any())
//                {
//                    SelectionPrompt<User> prompt = new SelectionPrompt<User>();
//                    prompt.Title = "\nSelect the user you want the listener to target:";
//                    prompt.Converter = x => x.MainUsername ?? x.first_name;
//                    prompt.AddChoice(new User() { id = -1, first_name = "-- REMOVE CURRENT USER --" });
//                    prompt.AddChoices(userList);

//                    User choice = AnsiConsole.Prompt(prompt);

//                    if (choice.id == -1)
//                    {
//                        SourceChannel.SelectedUser = null;
//                        XMLSettings.Update(Settings.TelegramTargetUserId, string.Empty);
//                    }
//                    else
//                    {
//                        SourceChannel.SelectedUser = choice;
//                        XMLSettings.Update(Settings.TelegramTargetUserId, choice.ID.ToString());
//                    }
//                }
//            }

//            if (fromMenuAction)
//            {
//                ChannelSemaphore.Release();
//                WriteHeaderToConsole();
//            }
//        }

//        [Documentation(SendMessageToChannelDocumentation)]
//        private void SendMessageToChannel()
//        {
//            string text = ConsoleUtil.GetInput("Enter the text you want to sent to the channel.  Enter [red]CANCEL[/] to stop");
//            SendMessage(text);
//        }

//        [Documentation(StartStopListenerDocumentation)]
//        private void StartStopListener()
//        {
//            if (UpdateManager == null)
//            {
//                UpdateManager = TelegramClient.WithUpdateManager(Client_OnUpdate);
//            }
//            else
//            {
//                UpdateManager.StopResync();
//                UpdateManager = null;
//            }

//            WriteHeaderToConsole();
//        }

//        [Documentation(ViewChannelsDocumentation)]
//        private void ViewChannels()
//        {
//            foreach (var chat in TelegramChannel.GetAll(TelegramClient))
//                AnsiConsole.MarkupLine("[blue]{0}[/]\t{1}", chat.ID, chat.Title);
//        }

//        [Documentation(ViewQueuedAddressesDocumentation)]
//        private void ViewQueuedAddresses()
//        {
//            if (!ContractAddressQueue.Any())
//                AnsiConsole.MarkupLine("[green]No addresses currently in the queue.[/]");
//            else
//                AnsiConsole.MarkupLine("[yellow]----- Queued Addresses -----[/]");

//            int position = 1;
//            ContractAddressQueue.ForEach(address =>
//            {
//                AnsiConsole.MarkupLine("[blue]{0}[/]: {1}", position, address);
//                position++;
//            });
//        }

//        #endregion

//        #region Private API

//        private async Task Client_OnUpdate(Update update)
//        {
//            switch (update)
//            {
//                case UpdateNewMessage unm: await HandleMessage(unm.message); break;
//            }

//            await Task.CompletedTask;
//        }

//        private Task HandleMessage(MessageBase messageBase, bool edit = false)
//        {
//            ChannelSemaphore.Wait();

//            if (SourceChannel == null || SourceChannel.Invalidate(messageBase))
//            {
//                ChannelSemaphore.Release();
//                return Task.CompletedTask;
//            }

//            ChannelSemaphore.Release();

//            if (!(messageBase is Message message && !string.IsNullOrEmpty(message.message)))
//                return Task.CompletedTask;

//            bool foundSolanaAddress = false;
//            PublicKey publicKey = null;

//            foreach (string piece in message.message.Split(new char[] { '\n', '\t', '\r' }))
//            {
//                try
//                {
//                    publicKey = new PublicKey(piece);
//                }
//                catch (Exception)
//                {
//                    publicKey = null;
//                }

//                foundSolanaAddress = publicKey != null && publicKey.IsValid();

//                if (foundSolanaAddress)
//                    break;
//            }

//            if (foundSolanaAddress && !SeenAddresses.Contains(publicKey.Key))
//            {
//                SeenAddresses.Add(publicKey.Key);
//                ContractAddressQueue.Enqueue(publicKey.Key);
//            }

//            return Task.CompletedTask;
//        }

//        private void SendMessage(string text)
//        {
//            ChannelSemaphore.Wait();

//            if (SourceChannel != null)
//            {
//                InputPeer target = SourceChannel.Instance;
//                InputReplyToMessage replyTo = null;

//                if (SourceChannel.SelectedTopic != null && SourceChannel.SelectedTopic.id != 1)
//                    replyTo = new InputReplyToMessage() { top_msg_id = SourceChannel.SelectedTopic.id, reply_to_msg_id = SourceChannel.SelectedTopic.id, flags = InputReplyToMessage.Flags.has_top_msg_id };

//                if (!string.IsNullOrEmpty(text))
//                    Task.Run<UpdatesBase>(async () => await TelegramClient.Messages_SendMessage(target, text, Helpers.RandomLong(), replyTo));
//            }

//            ChannelSemaphore.Release();
//        }

//        protected override void WriteHeaderToConsole()
//        {
//            base.WriteHeaderToConsole();

//            if (CurrentUser != null)
//                AnsiConsole.MarkupLine("You are logged in as as [blue]" + CurrentUser + "[/] (id " + CurrentUser.id + ")\n");
//            else
//                AnsiConsole.MarkupLine("\nAn error occurred while attempting to login.  Please select 'change user' and try again.\n");

//            ChannelSemaphore.Wait();

//            if (SourceChannel != null)
//            {
//                string message = string.Format("You are listening to the channel [blue]{0}[/] ", SourceChannel.Title);

//                if (SourceChannel.SelectedTopic != null)
//                    message += string.Format("in the topic [blue]{0}[/] ", SourceChannel.SelectedTopic.title);

//                if (SourceChannel.SelectedUser != null)
//                    message += string.Format("to the user [blue]{0}[/] ", SourceChannel.SelectedUser.MainUsername ?? SourceChannel.SelectedUser.first_name);

//                AnsiConsole.MarkupLine(message + "\n");
//            }

//            ChannelSemaphore.Release();

//            if (UpdateManager != null)
//                AnsiConsole.MarkupLine("[green]Listener is running[/]\n");
//            else
//                AnsiConsole.MarkupLine("[orange1]Listener is stopped[/]\n");
//        }

//        private string Config(string what)
//        {
//            switch (what)
//            {
//                case "api_id": return XMLSettings.GetSetting(Settings.TelegramAPIKey, Validate);
//                case "api_hash": return XMLSettings.GetSetting(Settings.TelegramAPIHash, Validate);
//                case "phone_number": return XMLSettings.GetSetting(Settings.TelegramPhoneNumber, Validate);
//                case "verification_code": Console.Write("Enter the verification code sent to your Telegram app: "); return Console.ReadLine();
//                case "password": return ConsoleUtil.GetInput(new PromptSettings() { Prompt = "Enter your password", IsSecret = true });
//                default: return null; // let WTelegramClient decide the default config
//            }
//        }

//        private bool Validate(Settings node, string value)
//        {
//            bool validated = true;

//            if (node.Name == Settings.TelegramPhoneNumber.Name)
//            {
//                validated = value.IsNumeric();

//                if (!validated)
//                    AnsiConsole.WriteLine("Invalid phone number.");
//            }

//            if (node.Name == Settings.TelegramChannelId.Name)
//            {
//                validated = value.IsNumeric();

//                if (!validated)
//                    AnsiConsole.WriteLine("Invalid channel Id.");
//            }

//            return validated;
//        }

//        private void LoginUser()
//        {
//            try
//            {
//                if (CurrentUser == null)
//                    CurrentUser = Task.Run<User>(async () => await TelegramClient.LoginUserIfNeeded()).Result;

//                ChannelSemaphore.Wait();

//                SourceChannel = LoadChat(Settings.TelegramChannelId);

//                if (SourceChannel != null)
//                {
//                    int.TryParse(XMLSettings.GetValue(Settings.TelegramTopicId), out int topicId);

//                    if (topicId > 0)
//                        SourceChannel.SelectedTopic = SourceChannel.Topics.FirstOrDefault(x => x.id == topicId);

//                    long.TryParse(XMLSettings.GetValue(Settings.TelegramTargetUserId), out long userId);

//                    if (userId > 0)
//                        SourceChannel.SelectedUser = SourceChannel.Users.FirstOrDefault(x => x.id == userId);
//                }

//                ChannelSemaphore.Release();
//            }
//            catch (Exception e)
//            {
//                e.LogException();
//            }
//        }

//        private TelegramChannel SelectChat(Settings setting)
//        {
//            TelegramChannel chat = null;

//            XMLSettings.Update(setting, string.Empty);
//            string chatId = XMLSettings.GetSetting(setting, Validate);
//            chat = LoadChat(setting, chatId);

//            if (chat == null)
//                Logger.LogWarning(string.Format("A channel with the ID: [red]{0}[/] was not found", chatId));

//            return chat;
//        }

//        private TelegramChannel LoadChat(Settings setting, string chatId = null)
//        {
//            TelegramChannel chat = null;

//            if (chatId == null)
//                chatId = XMLSettings.GetValue(setting);

//            if (!string.IsNullOrEmpty(chatId))
//                chat = TelegramChannel.GetAll(TelegramClient).FirstOrDefault(x => x.ID == long.Parse(chatId));

//            return chat;
//        }

//        #endregion

//        #region Documentation 

//        private const string ChangeUserAccountDocumentation = "Prompts to enter in your user information to login to your Telegram account";
//        private const string Documentation = "api_id: your application api ID from my.telegram.org/apps\n" +
//            "api_hash: your api hash from my.telegram.org/apps\n" +
//            "phone_number: the phone number of your telegram account\n" +
//            "verification_code: the verification code sent to your telegram app";
//        private const string SelectChannelDocumentation = "Select the channel that the listener will use to read contract addresses";
//        private const string SelectChannelTopicDocumentation = "Prompts to change the telegram group topic of the selected channel that the listener will use to read contract addresses";
//        private const string SelectChannelUserDocumentation = "Prompts to select a telegram user from the selected channel.  The listener ONLY read input from this user.";
//        private const string SendMessageToChannelDocumentation = "Sends a message to your selected channel/topic";
//        private const string StartStopListenerDocumentation = "Starts or stops the listener that will read contract addresses from your source Telegram channel";
//        private const string ViewChannelsDocumentation = "Shows the list of telegram channels that you belong to";
//        private const string ViewQueuedAddressesDocumentation = "View the list of contract addresses currently in the queue";

//        #endregion
//    }
//}
