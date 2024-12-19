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

namespace WagmiBot
{
    public class WagmiClient
    {
        #region Properties

        private int APIKey { get; set; }
        private string APIHash { get; set; }

        private TelegramBotClient BotClient;
        private UserClientManager UserManager => UserClientManager.Instance;


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
            var userState = UserManager.UserStates.GetOrAdd(chatId, _ => new UserState());

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
            UserManager.UserStates.TryGetValue(chatId, out UserState userState);

            switch (userState.CurrentState)
            {
                case State.AwaitingVerificationCode:
                    HandleVerificationCode(query, userState);
                    break;
            }
        }

        #endregion

        #region Private API: Authentication

        private async Task HandleInitialState(Message message, UserState state)
        {
            UserManager.UserClients.TryGetValue(message.Chat.Id, out Client client);

            // If we already have a client loaded for this user, just return
            if (client != null && client.UserId != 0)
            {
                state.CurrentState = State.Authenticated;
                return;
            }
            else if (client != null)
            {
                // Otherwise remove 
                await client.DisposeAsync();
                UserManager.UserClients.TryRemove(message.Chat.Id, out client);
            }

            DirectoryInfo dir = FileUtil.GetOrCreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "sessions"));
            state.SessionPath = Path.Combine(dir.FullName, message.From.Id.ToString());

            // Otherwise try to login via the stored session info
            client = new WTelegram.Client(what => Config(what, state));
            await client.ConnectAsync();

            // If the client was loaded successfully without asking the user for input, mark as authenticated
            if (client != null && client.UserId != 0)
            {
                UserManager.UserClients.TryAdd(message.Chat.Id, client);
                state.CurrentState = State.Authenticated;
                await HandleUserRequest(message, state);
                return;
            }
            else
            {
                if (client != null)
                    await client.DisposeAsync();

                state.CurrentState = State.AwaitingPhoneNumber;
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Please enter your phone number in international format (e.g., +1234567890):"
                );
            }
        }

        private async Task HandlePhoneNumber(Message message, UserState state)
        {
            var phoneNumber = message.Text?.Trim();

            if (string.IsNullOrEmpty(phoneNumber))
            {
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Invalid phone number format. Please enter your phone number starting with + symbol:"
                );
                return;
            }

            state.PhoneNumber = phoneNumber;
            state.CurrentState = State.AwaitingVerificationCode;

            var client = new WTelegram.Client(what => Config(what, state));
            await client.ConnectAsync();

            Auth_SentCode sentCode = await client.Auth_SendCode(state.PhoneNumber, APIKey, APIHash, new CodeSettings()) as Auth_SentCode;
            state.SentCode = sentCode;

            UserManager.UserClients.TryAdd(message.Chat.Id, client);

            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Please enter the verification code sent to your Telegram app:",
                replyMarkup: UIElement.GetInlineKeyboard()
            );
        }

        private async Task HandlePassword(Message message, UserState state)
        {
            state.Password = message.Text;
            state.CurrentState = State.AwaitingVerificationCode;

            UserManager.UserClients.TryGetValue(message.Chat.Id, out Client client);

            try
            {
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
                    authorization = null;
                }

                state.CurrentUser = client.LoginAlreadyDone(authorization);
                state.CurrentState = State.Authenticated;

                await ShowMainMenu(message.Chat.Id);
            }
            catch (Exception ex)
            {
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Failed to send verification code: {ex.Message}\nPlease start over."
                );
                state.CurrentState = State.Initial;

                if (UserManager.UserClients.TryRemove(message.Chat.Id, out var oldClient))
                    await oldClient.DisposeAsync();
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

            switch (query.Data)
            {
                case Command.Submit:

                    reset = true;
                    message = verificationCode;
                    state.VerificationCode = verificationCode.Replace("-", string.Empty);
                    state.CurrentState = State.AwaitingPassword;

                    await BotClient.SendMessage(
                        chatId: query.From.Id,
                        text: "Enter your password:"
                    );

                    break;

                case Command.Reset:

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
                replyMarkup: reset ? null : UIElement.GetInlineKeyboard()
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
            if (UserManager.UserClients.TryGetValue(message.Chat.Id, out var client))
            {
                switch (message.Text)
                {
                    case Command.Start:
                        ShowMainMenu(message.Chat.Id);
                        break;
                    case Command.SelectChannel:
                        await HandleSelectChannel(message.Chat.Id, client);
                        break;
                    case Command.SelectUser:
                        await HandleSelectChannelUser(message.Chat.Id, client, state);
                        break;
                    case Command.SelectTopic:
                        await HandleSelectTopic(message.Chat.Id, client, state);
                        break;
                    case Command.ViewChannels:
                        await HandleViewChannels(message.Chat.Id, client);
                        break;
                }
            }
        }

        private async Task HandleSelectChannel(long chatId, WTelegram.Client client)
        {
            var channels = TelegramChannel.GetAll(client);
            var keyboard = new List<List<KeyboardButton>>();

            foreach (var channel in channels)
            {
                keyboard.Add(new List<KeyboardButton>
                {
                    new KeyboardButton(channel.Title)
                });
            }

            var replyMarkup = new ReplyKeyboardMarkup(keyboard)
            {
                ResizeKeyboard = true
            };

            await BotClient.SendMessage(
                chatId: chatId,
                text: "Select a channel:",
                replyMarkup: replyMarkup
            );
        }

        private async Task HandleSelectChannelUser(long chatId, WTelegram.Client client, UserState state)
        {
            if (state.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: chatId,
                    text: "Please select a channel first."
                );
                return;
            }

            var users = state.SelectedChannel.Users;
            var keyboard = new List<List<KeyboardButton>>();

            foreach (var user in users)
            {
                var username = user.MainUsername ?? user.first_name;
                keyboard.Add(new List<KeyboardButton>
                {
                    new KeyboardButton(username)
                });
            }

            var replyMarkup = new ReplyKeyboardMarkup(keyboard)
            {
                ResizeKeyboard = true
            };

            await BotClient.SendMessage(
                chatId: chatId,
                text: "Select a user:",
                replyMarkup: replyMarkup
            );
        }

        private async Task HandleSelectTopic(long chatId, WTelegram.Client client, UserState state)
        {
            if (state.SelectedChannel == null)
            {
                await BotClient.SendMessage(
                    chatId: chatId,
                    text: "Please select a channel first."
                );
                return;
            }

            var topics = state.SelectedChannel.Topics;
            var keyboard = new List<List<KeyboardButton>>();

            foreach (var topic in topics)
            {
                keyboard.Add(new List<KeyboardButton>
                {
                    new KeyboardButton(topic.title)
                });
            }

            var replyMarkup = new ReplyKeyboardMarkup(keyboard)
            {
                ResizeKeyboard = true
            };

            await BotClient.SendMessage(
                chatId: chatId,
                text: "Select a topic:",
                replyMarkup: replyMarkup
            );
        }

        private async Task HandleViewChannels(long chatId, WTelegram.Client client)
        {
            var channels = TelegramChannel.GetAll(client);
            var channelList = new System.Text.StringBuilder();

            foreach (var channel in channels)
            {
                channelList.AppendLine($"{channel.Title} (ID: {channel.ID})");
            }

            await BotClient.SendMessage(
                chatId: chatId,
                text: channelList.ToString()
            );
        }

        private async Task ShowMainMenu(long chatId)
        {
            var mainMenuKeyboard = new ReplyKeyboardMarkup()
            .AddNewRow(Command.ViewChannels)
            .AddNewRow(Command.SelectChannel, Command.SelectTopic, Command.SelectUser);

            mainMenuKeyboard.ResizeKeyboard = true;

            await BotClient.SendMessage(
                chatId: chatId,
                text: "Welcome! Please select an option:",
                replyMarkup: mainMenuKeyboard
            );
        }

        #endregion

        #region Private API

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Polling error: {exception}");
            return Task.CompletedTask;
        }

        #endregion

        #region Supporting Classes

        private static class UIElement
        {
            public static InlineKeyboardMarkup GetInlineKeyboard()
            {
                return new InlineKeyboardMarkup()
                                .AddNewRow("1", "2", "3")
                                .AddNewRow("4", "5", "6")
                                .AddNewRow("7", "8", "9")
                                .AddNewRow("0")
                                .AddNewRow(Command.Reset, Command.Submit);
            }
        }

        private class Command
        {
            public const string Start = "/start";
            public const string Reset = "Reset";
            public const string Submit = "Submit";
            public const string SelectChannel = "Select Channel";
            public const string SelectUser = "Select User";
            public const string SelectTopic = "Select Topic";
            public const string ViewChannels = "View Channels";
        }

        #endregion
    }
}
