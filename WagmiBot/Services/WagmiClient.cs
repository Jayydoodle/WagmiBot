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
            UserState userState = await UserManager.GetState(chatId);

            switch (userState.CurrentState)
            {
                case State.Initial:
                    await HandleInitialState(message, userState);
                    break;
                case State.AwaitingPhoneNumber:
                    await HandlePhoneNumber(message, userState);
                    break;
                case State.AwaitingPassword:
                    await HandlePassword(message, userState);
                    break;
                case State.AwaitingVerificationCode:
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Please use the provided keyboard to continue"
                    );
                    break;
                case State.Authenticated:
                    await HandleUserRequest(message, userState);
                    break;
            }
        }

        private async Task HandleCallbackUpdate(ITelegramBotClient botClient, CallbackQuery query, CancellationToken cancellationToken)
        {
            var chatId = query.From.Id;
            UserState userState = await UserManager.GetState(chatId);

            switch (userState.CurrentState)
            {
                case State.AwaitingVerificationCode:
                    await HandleVerificationCode(query, userState);
                    break;
                case State.Authenticated:
                    await HandleUserRequest(query, userState);
                    break;
            }
        }

        #endregion

        #region Private API: Authentication

        private async Task HandleInitialState(Message message, UserState state)
        {
            Client client = await UserManager.GetClient(message.Chat.Id);

            // If we already have a client loaded for this user, just return
            if (client != null && client.UserId != 0)
            {
                state.CurrentState = State.Authenticated;
                return;
            }
            else if (client != null)
            {
                // Otherwise remove 
                await UserManager.RemoveClient(message.Chat.Id, client);
            }

            DirectoryInfo dir = FileUtil.GetOrCreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "users", message.From.Id.ToString()));
            state.SessionPath = Path.Combine(dir.FullName, "session");

            // Otherwise try to login via the stored session info
            client = new WTelegram.Client(what => Config(what, state));
            await client.ConnectAsync();

            // If the client was loaded successfully without asking the user for input, mark as authenticated
            if (client != null && client.UserId != 0)
            {
                await UserManager.AddClient(message.Chat.Id, client);
                state.CurrentState = State.Authenticated;
                await HandleUserRequest(message, state);
                return;
            }
            else
            {
                if (client != null)
                    await client.DisposeAsync();

                state.MoveToNextState();
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Please enter your phone number in international format (e.g., +1234567890):"
                );
            }
        }

        private async Task HandlePhoneNumber(Message message, UserState state)
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

            state.PhoneNumber = phoneNumber;
            WTelegram.Client client = null;
            state.MoveToNextState();

            try
            {
                client = new WTelegram.Client(what => Config(what, state));
                await client.ConnectAsync();

                Auth_SentCode sentCode = await client.Auth_SendCode(state.PhoneNumber, APIKey, APIHash, new CodeSettings()) as Auth_SentCode;
                state.SentCode = sentCode;

                await UserManager.AddClient(message.Chat.Id, client);
            }
            catch (System.Exception)
            {
                state.MoveToPreviousState();
                throw;
            }

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Please enter the verification code sent to your Telegram app:",
                replyMarkup: UIElement.GetInlineNumericKeyboard()
            );
        }

        private async Task HandlePassword(Message message, UserState state)
        {
            state.Password = message.Text;
            state.MoveToNextState();

            Client client = await UserManager.GetClient(message.Chat.Id);

            bool authenticated = await TryLoginUser(client, state);

            if (!authenticated) 
            {
                state.MoveToPreviousState();

                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Login failed, please try again."
                );
            }
            else
            {
                await ShowMainMenu(message.Chat.Id, state);
            }
        }

        private async Task HandleVerificationCode(CallbackQuery query, UserState state)
        {
            List<string> pieces = query.Message.Text.Split("\n").ToList();

            if (pieces.Count() == 1)
                pieces.Add(string.Empty);

            string originalMessage = pieces[0];
            string verificationCode = pieces[1];
            string message = null;
            bool reset = false;

            Client client = await UserManager.GetClient(query.From.Id);

            switch (query.Data)
            {
                case var value when value == Command.Submit:

                    reset = true;
                    message = verificationCode;
                    state.VerificationCode = verificationCode.Replace("-", string.Empty);
                    state.CurrentState = State.AwaitingPassword;

                    bool authenticated = await TryLoginUser(client, state);

                    if(authenticated)
                    {
                        await ShowMainMenu(query.From.Id, state);
                    }
                    else if(!authenticated && state.NeedsPasswordVerification)
                    {
                        // ToDo: Only move to the next state if we're not authenticated due to 2FA, NOT if it was a different exception
                        // the above may already work?
                        state.MoveToNextState();

                        await BotClient.SendMessage(
                            chatId: query.From.Id,
                            text: "Enter your password:"
                        );
                    }
                    else if (!authenticated)
                    {
                        state.MoveToPreviousState();

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

        private string Config(string what, UserState state)
        {
            switch (what)
            {
                case "api_id": return APIKey.ToString(); // Replace with actual API ID
                case "api_hash": return APIHash; // Replace with actual API Hash
                case "phone_number": return state.PhoneNumber;
                case "password": return state.Password;
                case "verification_code": return state.VerificationCode;
                case "session_pathname": return state.SessionPath;
                default: return null;
            }
        }

        #endregion

        #region Private API: User Requests

        private async Task HandleUserRequest(Message message, UserState state)
        {
            Client client = await UserManager.GetClient(message.Chat.Id);

            // ToDo: Error logging / handling
            if (client == null)
                return;

            switch (message.Text)
            {
                case var value when value == Command.Start:
                    await ShowMainMenu(message.Chat.Id, state);
                    break;
            }
        }

        private async Task HandleUserRequest(CallbackQuery query, UserState state)
        {
            Client client = await UserManager.GetClient(query.From.Id);

            // ToDo: Error logging / handling
            if (client == null)
                return;

            if (query.Message?.Text == Command.SelectChannelPrompt)
            {
                await HandleSelectChannel(query, client, state);
                return;
            }

            if (query.Message?.Text == Command.SelectTopicPrompt)
            {
                await HandleSelectChannelTopic(query, client, state);
                return;
            }

            if (query.Message?.Text == Command.SelectUserPrompt)
            {
                await HandleSelectChannelUser(query, client, state);
                return;
            }

            switch (query.Data)
            {
                case var value when value == Command.SelectChannel:
                    await PromptSelectChannel(query, client);
                    break;
                case var value when value == Command.SelectTopic:
                    await PromptSelectChannelTopic(query, client, state);
                    break;
                case var value when value == Command.SelectUser:
                    await PromptSelectChannelUser(query, client, state);
                    break;
                case var value when value == Command.ViewChannels:
                    await HandleViewChannels(query, client);
                    break;
            }
        }

        private async Task PromptSelectChannel(CallbackQuery query, WTelegram.Client client)
        {
            var channels = await TelegramChannel.GetAll(client);
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

        private async Task HandleSelectChannel(CallbackQuery query, WTelegram.Client client, UserState state)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                // ToDo: Probably need concurrency locking around this
                var channels = await TelegramChannel.GetAll(client);
                state.SelectedChannel = null;
            }
            else
            {
                long channelId = long.Parse(selection);
                var channels = await TelegramChannel.GetAll(client);
                state.SelectedChannel = channels.FirstOrDefault(x => x.ID == channelId);
            }

            await ShowMainMenu(query.From.Id, state);
        }

        private async Task PromptSelectChannelUser(CallbackQuery query, WTelegram.Client client, UserState state)
        {
            if (state.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: query.From.Id,
                    text: "Please select a channel first."
                );

                return;
            }

            var users = await state.SelectedChannel.GetUsers(client);
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

        private async Task HandleSelectChannelUser(CallbackQuery query, WTelegram.Client client, UserState state)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                // ToDo: Probably need concurrency locking around this
                state.SelectedChannel.SelectedUser = null;
            }
            else
            {
                long userId = long.Parse(selection);
                var users = await state.SelectedChannel.GetUsers(client);
                state.SelectedChannel.SelectedUser = users.FirstOrDefault(x => x.ID == userId);
            }

            await ShowMainMenu(query.From.Id, state);
        }

        private async Task PromptSelectChannelTopic(CallbackQuery query, WTelegram.Client client, UserState state)
        {
            if (state.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: query.From.Id,
                    text: "Please select a channel first."
                );

                return;
            }

            var topics = await state.SelectedChannel.GetTopics(client);
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

        private async Task HandleSelectChannelTopic(CallbackQuery query, WTelegram.Client client, UserState state)
        {
            string selection = query.Data.Split(':').Select(x => x.Trim()).ToList().Last();

            if (string.IsNullOrEmpty(selection) || selection == Command.Remove)
            {
                // ToDo: Probably need concurrency locking around this
                state.SelectedChannel.SelectedTopic = null;
            }
            else
            {
                long topicId = long.Parse(selection);
                var topics = await state.SelectedChannel.GetTopics(client);
                state.SelectedChannel.SelectedTopic = topics.FirstOrDefault(x => x.ID == topicId);
            }

            await ShowMainMenu(query.From.Id, state);
        }

        private async Task HandleViewChannels(CallbackQuery query, WTelegram.Client client)
        {
            var channels = await TelegramChannel.GetAll(client);
            var channelList = new System.Text.StringBuilder();

            foreach (var channel in channels)
            {
                channelList.AppendLine($"<b>{channel.Title}</b> (ID: {channel.ID})");
            }

            await BotClient.SendMessage(
                chatId: query.From.Id,
                text: channelList.ToString(),
                parseMode: ParseMode.Html
            );
        }

        private async Task ShowMainMenu(long chatId, UserState state)
        {
            var mainMenuKeyboard = new InlineKeyboardMarkup()
            .AddNewRow(Command.ViewChannels)
            .AddNewRow(Command.SelectChannel, Command.SelectTopic, Command.SelectUser);

            string message = string.Format("<b>========== WAGMI BOT ==========</b>\n" +
            "Wagmi allows you to copy automatically copy trade calls from your favorite Telegram groups/users\n\n" +
            "<b>{0}</b> - View the channels you're a member of\n" +
            "<b>{1}</b> - Select the channel to copy trades from\n" +
            "<b>{2}</b> - Select the topic in the channel to copy trades from (must select a channel first)\n" +
            "<b>{3}</b> - Select a user in the channel to copy trades from (must select a channel first)"
            , Command.ViewChannels, Command.SelectChannel, Command.SelectTopic, Command.SelectUser);

            if (state.SelectedChannel != null)
            {
                message += string.Format("\n\nSelected Channel: <b>{0}</b>", state.SelectedChannel.Title);

                if(state.SelectedChannel.SelectedTopic != null)
                    message += string.Format("\nSelected Topic: <b>{0}</b>", state.SelectedChannel.SelectedTopic.title);

                if (state.SelectedChannel.SelectedUser is TL.User user)
                    message += string.Format("\nSelected User: <b>{0}</b>", user.MainUsername ?? user.first_name);
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

        private static async Task<bool> TryLoginUser(Client client, UserState state)
        {
            bool authenticated = false;
            Auth_AuthorizationBase authorization = null;

            try
            {
                authorization = await client.Auth_SignIn(state.PhoneNumber, state.SentCode.phone_code_hash, state.VerificationCode);
            }
            catch (RpcException e) when (e.Code == 400 && e.Message == "PHONE_CODE_INVALID")
            {
                throw;
            }
            catch (RpcException e) when (e.Code == 401 && e.Message == "SESSION_PASSWORD_NEEDED")
            {
                if (!string.IsNullOrEmpty(state.Password))
                {
                        try
                        {
                            var accountPassword = await client.Account_GetPassword();
                            var checkPasswordSRP = await Client.InputCheckPassword(accountPassword, state.Password);
                            authorization = await client.Auth_CheckPassword(checkPasswordSRP);
                        }
                        catch (RpcException pe) when (pe.Code == 400 && pe.Message == "PASSWORD_HASH_INVALID")
                        {
                            throw;
                        }
                }
                else
                {
                    state.NeedsPasswordVerification = true;
                }
            }

            try
            {

                state.CurrentUser = client.LoginAlreadyDone(authorization);
                state.CurrentState = State.Authenticated;

                if (client.UserId != 0)
                    authenticated = true;
            }
            catch (System.Exception e)
            {
                throw;
            }

            return authenticated;
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
            public static string ViewChannels = "View Channels " + Spectre.Console.Emoji.Known.Eyes;
        }

        #endregion
    }
}
