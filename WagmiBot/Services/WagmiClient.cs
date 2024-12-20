using Telegram.Bot;
using WTelegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TL;
using JConsole;
using ReplyKeyboardMarkup = Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup;
using KeyboardButton = Telegram.Bot.Types.ReplyMarkups.KeyboardButton;
using Message = Telegram.Bot.Types.Message;
using Update = Telegram.Bot.Types.Update;
using BotCommand = Telegram.Bot.Types.BotCommand;
using Spectre.Console;
using SharpCompress;
using System.Threading.Channels;
using Telegram.Bot.Types.Enums;
using WTelegram.Types;
using Newtonsoft.Json;
using Solnet.TokenInfo.Services;

namespace WagmiBot
{
    public class WagmiClient
    {
        #region Properties

        private int APIKey { get; set; }
        private string APIHash { get; set; }

        private TelegramBotClient BotClient;
        private UserManager UserManager => UserManager.Instance;


        #endregion

        #region Life Cycle

        public WagmiClient(string botToken, string apiHash, int apiKey)
        {
            APIHash = apiHash;
            APIKey = apiKey;
            BotClient = new TelegramBotClient(botToken);
        }

        public async Task StartBotAsync(CancellationToken cancellationToken = default)
        {
            var me = await BotClient.GetMe(cancellationToken);

            await BotClient.SetMyCommands(
            commands: new List<BotCommand>()
            {
                new BotCommand(){ Command = Command.Start, Description="Main Menu" },
                // new BotCommand(){ Command = Command.Help, Description="Help" },
                // new BotCommand(){ Command = Command.Settings, Description="Settings" }
            },
            cancellationToken: cancellationToken);

            BotClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: null,
                cancellationToken: cancellationToken
            );
        }

        #endregion

        #region Private API: Update Handlers

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                switch (update.Type)
                {
                    case Telegram.Bot.Types.Enums.UpdateType.Message:
                        await HandleMessageUpdate(botClient, update.Message, cancellationToken);
                        break;
                    case Telegram.Bot.Types.Enums.UpdateType.CallbackQuery:
                        await HandleCallbackUpdate(botClient, update.CallbackQuery, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling update: {ex}");
            }
        }

        private async Task HandleMessageUpdate(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            WagmiUser user = await UserManager.GetUser(chatId, OnNewUserCreated);

            switch (user.CurrentState)
            {
                case AuthState.Initial:
                    await HandleInitialState(message, user);
                    break;
                case AuthState.AwaitingPhoneNumber:
                    await HandlePhoneNumber(message, user);
                    break;
                case AuthState.AwaitingPassword:
                    await HandlePassword(message, user);
                    break;
                case AuthState.AwaitingVerificationCode:
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Please use the provided keyboard to continue"
                    );
                    break;
                case AuthState.Authenticated:
                    await HandleUserRequest(message, user);
                    break;
            }
        }

        private async Task HandleCallbackUpdate(ITelegramBotClient botClient, CallbackQuery query, CancellationToken cancellationToken)
        {
            var chatId = query.From.Id;
            WagmiUser user = await UserManager.GetUser(chatId, OnNewUserCreated);

            switch (user.CurrentState)
            {
                case AuthState.AwaitingVerificationCode:
                    await HandleVerificationCode(query, user);
                    break;
                case AuthState.Authenticated:
                    await HandleUserRequest(query, user);
                    break;
            }
        }

        #endregion

        #region Private API: Authentication

        private async Task HandleInitialState(Message message, WagmiUser user)
        {
            // If we already have a client loaded for this user, just return
            if (user.TryAuthenticate())
                return;

            DirectoryInfo dir = FileUtil.GetOrCreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "users", message.From.Id.ToString()));
            user.SessionPath = Path.Combine(dir.FullName, "session");

            // Otherwise try to login via the stored session info
            Client client = new WTelegram.Client(what => Config(what, user));
            await client.ConnectAsync();

            // If the client was loaded successfully without asking the user for input, mark as authenticated
            if (client != null && client.UserId != 0)
            {
                user.InitClient(client);
                await HandleUserRequest(message, user);
                return;
            }
            else
            {
                if (client != null)
                    await client.DisposeAsync();

                user.AdvanceAuthState();

                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Please enter your phone number in international format (e.g., +1234567890):"
                );
            }
        }

        private async Task HandlePhoneNumber(Message message, WagmiUser user)
        {
            var phoneNumber = message.Text?.Trim()?.Replace("+", string.Empty);

            if (string.IsNullOrEmpty(phoneNumber) || !phoneNumber.IsNumeric())
            {
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Invalid phone number format. Please enter your phone number:"
                );

                return;
            }

            user.PhoneNumber = phoneNumber;
            WTelegram.Client client = null;
            user.AdvanceAuthState();

            try
            {
                client = new WTelegram.Client(what => Config(what, user));
                await client.ConnectAsync();

                Auth_SentCode sentCode = await client.Auth_SendCode(user.PhoneNumber, APIKey, APIHash, new CodeSettings()) as Auth_SentCode;
                user.SentCode = sentCode;
                user.InitClient(client);
            }
            catch (System.Exception)
            {
                user.RevertAuthState();
                throw;
            }

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Please enter the verification code sent to your Telegram app:",
                replyMarkup: UIElement.GetInlineNumericKeyboard()
            );
        }

        private async Task HandlePassword(Message message, WagmiUser user)
        {
            user.Password = message.Text;
            user.AdvanceAuthState();

            bool authenticated = await user.TryLogin();

            if (!authenticated) 
            {
                user.RevertAuthState();

                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Login failed, please try again."
                );
            }
            else
            {
                await ShowMainMenu(message.Chat.Id, user);
            }
        }

        private async Task HandleVerificationCode(CallbackQuery query, WagmiUser user)
        {
            List<string> pieces = query.Message.Text.Split("\n").ToList();

            if (pieces.Count() == 1)
                pieces.Add(string.Empty);

            string originalMessage = pieces[0];
            string verificationCode = pieces[1];
            string message = null;
            bool reset = false;

            switch (query.Data)
            {
                case var value when value == Command.Submit:

                    reset = true;
                    message = verificationCode;
                    user.VerificationCode = verificationCode.Replace("-", string.Empty);
                    user.CurrentState = AuthState.AwaitingPassword;

                    bool authenticated = await user.TryLogin();

                    if(authenticated)
                    {
                        await ShowMainMenu(query.From.Id, user);
                    }
                    else if(!authenticated && user.NeedsPasswordVerification)
                    {
                        // ToDo: Only move to the next state if we're not authenticated due to 2FA, NOT if it was a different exception
                        // the above may already work?
                        user.AdvanceAuthState();

                        await BotClient.SendMessage(
                            chatId: query.From.Id,
                            text: "Enter your password:"
                        );
                    }
                    else if (!authenticated)
                    {
                        user.RevertAuthState();

                        await BotClient.SendMessage(
                            chatId: query.From.Id,
                            text: "An error occured, please try again:"
                        );
                    }

                    break;

                case var value when value == Command.Reset:

                    message = originalMessage;

                    break;

                default:

                    verificationCode = !string.IsNullOrEmpty(verificationCode)
                                            ? string.Format("{0}-{1}", verificationCode, query.Data)
                                            : string.Format("{0}", query.Data);

                    message = string.Format("{0}\n{1}", originalMessage, verificationCode);

                    break;
            }

            await BotClient.EditMessageText(
                chatId: query.From.Id,
                messageId: query.Message.Id,
                text: message,
                replyMarkup: reset ? null : UIElement.GetInlineNumericKeyboard()
            );
        }

        private string Config(string what, WagmiUser user)
        {
            switch (what)
            {
                case "api_id": return APIKey.ToString(); // Replace with actual API ID
                case "api_hash": return APIHash; // Replace with actual API Hash
                case "phone_number": return user.PhoneNumber;
                case "password": return user.Password;
                case "verification_code": return user.VerificationCode;
                case "session_pathname": return user.SessionPath;
                default: return null;
            }
        }

        #endregion

        #region Private API: User Requests

        private async Task HandleUserRequest(Message message, WagmiUser user)
        {
            switch (message.Text)
            {
                case var value when value == Command.Start:
                    await ShowMainMenu(message.Chat.Id, user);
                    break;
            }
        }

        private async Task HandleUserRequest(CallbackQuery query, WagmiUser user)
        {
            if (query.Message?.Text == Command.SelectChannelPrompt)
            {
                await HandleSelectChannel(query, user);
                return;
            }

            if (query.Message?.Text == Command.SelectTopicPrompt)
            {
                await HandleSelectChannelTopic(query, user);
                return;
            }

            if (query.Message?.Text == Command.SelectUserPrompt)
            {
                await HandleSelectChannelUser(query, user);
                return;
            }

            switch (query.Data)
            {
                case var value when value == Command.SelectChannel:
                    await PromptSelectChannel(query, user);
                    break;
                case var value when value == Command.SelectTopic:
                    await PromptSelectChannelTopic(query, user);
                    break;
                case var value when value == Command.SelectUser:
                    await PromptSelectChannelUser(query, user);
                    break;
                case var value when value == Command.ViewChannels:
                    await HandleViewChannels(query, user);
                    break;
                case var value when value == Command.StartListener || value == Command.StopListener:
                    await HandleStartStopListener(query, user);
                    break;
            }
        }

        private async Task PromptSelectChannel(CallbackQuery query, WagmiUser user)
        {
            var channels = await user.GetChannels();

            var keyboard = new InlineKeyboardMarkup();
            keyboard.AddNewRow(Command.Remove);
            channels.ForEach(x => keyboard.AddNewRow(string.Format(Constants.SelectionFormat, x.Title, x.ID)));

            await BotClient.SendMessage(
                chatId: query.From.Id,
                text: Command.SelectChannelPrompt,
                replyMarkup: keyboard,
                parseMode: ParseMode.Html
            );
        }

        private async Task HandleSelectChannel(CallbackQuery query, WagmiUser user)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                user.SelectedChannel = null;
            }
            else
            {
                long channelId = long.Parse(selection);
                var channels = await user.GetChannels();
                user.SelectedChannel = channels.FirstOrDefault(x => x.ID == channelId);
            }

            await ShowMainMenu(query.From.Id, user);
        }

        private async Task PromptSelectChannelUser(CallbackQuery query, WagmiUser user)
        {
            if (user.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: query.From.Id,
                    text: "Please select a channel first."
                );

                return;
            }

            var users = await user.GetUsers();

            var keyboard = new InlineKeyboardMarkup();
            keyboard.AddNewRow(Command.Remove);
            users.ForEach(x => keyboard.AddNewRow(string.Format(Constants.SelectionFormat, x.MainUsername ?? x.first_name, x.id)));

            await BotClient.SendMessage(
                chatId: query.From.Id,
                text: Command.SelectUserPrompt,
                replyMarkup: keyboard,
                parseMode: ParseMode.Html
            );
        }

        private async Task HandleSelectChannelUser(CallbackQuery query, WagmiUser user)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                // ToDo: Probably need concurrency locking around this
                user.SelectedChannel.SelectedUser = null;
            }
            else
            {
                long userId = long.Parse(selection);

                var users = await user.GetUsers();
                user.SelectedChannel.SelectedUser = users.FirstOrDefault(x => x.ID == userId);
            }

            await ShowMainMenu(query.From.Id, user);
        }

        private async Task PromptSelectChannelTopic(CallbackQuery query, WagmiUser user)
        {
            if (user.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: query.From.Id,
                    text: "Please select a channel first."
                );

                return;
            }

            var topics = await user.GetTopics();

            var keyboard = new InlineKeyboardMarkup();
            keyboard.AddNewRow(Command.Remove);
            topics.ForEach(x => keyboard.AddNewRow(string.Format(Constants.SelectionFormat, x.title, x.id)));

            await BotClient.SendMessage(
                chatId: query.From.Id,
                text: Command.SelectTopicPrompt,
                replyMarkup: keyboard,
                parseMode: ParseMode.Html
            );
        }

        private async Task HandleSelectChannelTopic(CallbackQuery query, WagmiUser user)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                // ToDo: Probably need concurrency locking around this
                user.SelectedChannel.SelectedTopic = null;
            }
            else
            {
                long topicId = long.Parse(selection);
                var topics = await user.GetTopics();
                user.SelectedChannel.SelectedTopic = topics.FirstOrDefault(x => x.ID == topicId);
            }

            await ShowMainMenu(query.From.Id, user);
        }

        private async Task HandleViewChannels(CallbackQuery query, WagmiUser user)
        {
            var channels = await user.GetChannels();
            var channelList = new System.Text.StringBuilder();

            foreach (var channel in channels)
                channelList.AppendLine($"<b>{channel.Title}</b> (ID: {channel.ID})");

            await BotClient.SendMessage(
                chatId: query.From.Id,
                text: channelList.ToString(),
                parseMode: ParseMode.Html
            );
        }

        private async Task HandleStartStopListener(CallbackQuery query, WagmiUser user)
        {
            await user.StartStopListener();
            await ShowMainMenu(query.From.Id, user);
        }

        private async Task ShowMainMenu(long chatId, WagmiUser user)
        {
            bool running = user.ListenerRunning();

            var mainMenuKeyboard = new InlineKeyboardMarkup()
            .AddNewRow(running ? Command.StopListener : Command.StartListener, Command.ViewChannels)
            .AddNewRow(Command.SelectChannel, Command.SelectTopic, Command.SelectUser);

            string message = string.Format("<b>========== WAGMI BOT ==========</b>\n" +
            "Wagmi allows you to copy automatically copy trade calls from your favorite Telegram groups/users\n\n" +
            "<b>{0}</b> - View the channels you're a member of\n" +
            "<b>{1}</b> - Select the channel to copy trades from\n" +
            "<b>{2}</b> - Select the topic in the channel to copy trades from (must select a channel first)\n" +
            "<b>{3}</b> - Select a user in the channel to copy trades from (must select a channel first)\n" +
            "<b>{4}</b> - Start the copy trade bot\n" +
            "<b>{5}</b> - Stop the copy trade bot\n"
            , Command.ViewChannels, Command.SelectChannel, Command.SelectTopic, Command.SelectUser, Command.StartListener, Command.StopListener);

            if (user.SelectedChannel != null)
            {
                // ToDo: Change all += strings to StringBuilder to save memory
                message += string.Format("\n\nSelected Channel: <b>{0}</b>", user.SelectedChannel.Title);

                if(user.SelectedChannel.SelectedTopic != null)
                    message += string.Format("\nSelected Topic: <b>{0}</b>", user.SelectedChannel.SelectedTopic.title);

                if (user.SelectedChannel.SelectedUser is TL.User selectedUser)
                    message += string.Format("\nSelected User: <b>{0}</b>", selectedUser.MainUsername ?? selectedUser.first_name);
            }

            await BotClient.SendMessage(
                chatId: chatId,
                text: message,
                replyMarkup: mainMenuKeyboard,
                parseMode: ParseMode.Html
            );
        }

        #endregion

        #region Private API

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Polling error: {exception}");
            return Task.CompletedTask;
        }

        private async Task OnNewUserCreated(WagmiUser user)
        {
            user.AddressFoundEvent += async (sender, args) => await OnAddressFound(args);
        }

        private async Task OnAddressFound(AddressFoundEventArgs args)
        {
            var tokenInfo = await SolanaTokenInfoService.Instance.GetTokenInfoAsync(args.ContractAddress);

            if (tokenInfo == null)
                return;

            string message = string.Format(
                "<b>From ChatId:</b> {0}\n<b>CA:</b> {1}\n<b>Ticker:</b> ${2}\n<b>Name:</b> {3}\n<b>Market Cap:</b> {4}\n<b>Price:</b> {5}\n<b>Volume (24h):</b> {6}\n<b>Holders:</b> {7}\n",
                args.SourceChatId, tokenInfo.ContractAddress, tokenInfo.Symbol,
                tokenInfo.Name, tokenInfo.MarketCapFormatted, tokenInfo.PriceInUsd,
                tokenInfo.Volume24H, tokenInfo.NumberOfHolders);

            await BotClient.SendMessage(
                chatId: args.ChatId,
                text: message,
                parseMode: ParseMode.Html
            );
        }

        #endregion

        #region Supporting Classes

        private static class UIElement
        {
            public static InlineKeyboardMarkup GetInlineNumericKeyboard()
            {
                return new InlineKeyboardMarkup()
                                .AddNewRow("1", "2", "3")
                                .AddNewRow("4", "5", "6")
                                .AddNewRow("7", "8", "9")
                                .AddNewRow("0")
                                .AddNewRow(Command.Reset, Command.Submit);
            }
        }

        private class Constants
        {
            public static string SelectionFormat = "{0} : {1}";
        }

        private class Command
        {
            public static string Help = "/help";
            public static string Settings = "/settings";
            public static string Start = "/start";
            public static string Reset = "Reset";
            public static string Submit = "Submit";
            public static string Remove = "--- REMOVE ---";
            public static string SelectChannel = "Select Channel " + Spectre.Console.Emoji.Known.Television;
            public static string SelectChannelPrompt = "Select a channel:";
            public static string SelectUser = "Select User " + Spectre.Console.Emoji.Known.PersonLiftingWeights;
            public static string SelectUserPrompt = "Select a user:";
            public static string SelectTopic = "Select Topic " + Spectre.Console.Emoji.Known.Notebook;
            public static string SelectTopicPrompt = "Select a topic:";
            public static string StartListener = "Enable Trading " + Spectre.Console.Emoji.Known.ChartIncreasing;
            public static string StopListener = "Disable Trading " + Spectre.Console.Emoji.Known.ChartDecreasing;
            public static string ViewChannels = "View Channels " + Spectre.Console.Emoji.Known.Eyes;
        }

        #endregion
    }
}
