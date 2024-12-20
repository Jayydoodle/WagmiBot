using Solnet.Wallet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace WagmiBot
{
    public class WagmiUser
    {
        #region Properties

        public long ChatId { get; private set; }
        public AuthState CurrentState { get; set; } = AuthState.Initial;
        public string SessionPath { get; set; }
        public Auth_SentCode SentCode { get; set; }
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
        public string VerificationCode { get; set; }
        public bool NeedsPasswordVerification { get; set; }
        public TelegramChannel SelectedChannel { get; set; } // ToDo: Everywhere 'state' is called probably needs concurrency locking
        private Client Client { get; set; }
        private UpdateManager UpdateManager { get; set; }

        #endregion

        #region Events

        public EventHandler<AddressFoundEventArgs> AddressFoundEvent { get; set; }

        #endregion

        #region Life Cycle

        public WagmiUser(long chatId)
        {
            ChatId = chatId;
        }

        public void InitClient(Client client)
        {
            Client = client;
            TryAuthenticate();
        }

        #endregion

        #region Public API

        public bool ListenerRunning()
        {
            return UpdateManager != null;
        }

        public async Task StartStopListener()
        {
            if (UpdateManager == null)
            {
                UpdateManager = Client.WithUpdateManager(OnMessageReceived);
            }
            else
            {
                await UpdateManager.StopResync();
                UpdateManager = null;
            }
        }

        public async Task<List<TelegramChannel>> GetChannels()
        {
            var channels = await TelegramChannel.GetAll(Client);
            return channels;
        }

        public async Task<List<ForumTopic>> GetTopics()
        {
            if (SelectedChannel == null)
                return new List<ForumTopic>();

            var topics = await SelectedChannel.GetTopics(Client);
            return topics;
        }

        public async Task<List<User>> GetUsers()
        {
            if (SelectedChannel == null)
                return new List<User>();

            var topics = await SelectedChannel.GetUsers(Client);
            return topics;
        }

        #endregion

        #region Public API: Authentication

        public bool TryAuthenticate()
        {
            bool authenticated = Client != null && Client.UserId > 0;

            if (authenticated)
                CurrentState = AuthState.Authenticated;

            return authenticated;
        }

        public async Task<bool> TryLogin()
        {
            bool authenticated = false;
            Auth_AuthorizationBase authorization = null;

            try
            {
                authorization = await Client.Auth_SignIn(PhoneNumber, SentCode.phone_code_hash, VerificationCode);
            }
            catch (RpcException e) when (e.Code == 400 && e.Message == "PHONE_CODE_INVALID")
            {
                throw;
            }
            catch (RpcException e) when (e.Code == 401 && e.Message == "SESSION_PASSWORD_NEEDED")
            {
                if (!string.IsNullOrEmpty(Password))
                {
                    try
                    {
                        var accountPassword = await Client.Account_GetPassword();
                        var checkPasswordSRP = await Client.InputCheckPassword(accountPassword, Password);
                        authorization = await Client.Auth_CheckPassword(checkPasswordSRP);
                    }
                    catch (RpcException pe) when (pe.Code == 400 && pe.Message == "PASSWORD_HASH_INVALID")
                    {
                        throw;
                    }
                }
                else
                {
                    NeedsPasswordVerification = true;
                }
            }

            try
            {

                Client.LoginAlreadyDone(authorization);
                CurrentState = AuthState.Authenticated;

                if (Client.UserId != 0)
                    authenticated = true;
            }
            catch (System.Exception e)
            {
                throw;
            }

            return authenticated;
        }

        public void AdvanceAuthState()
        {
            AuthState nextState = CurrentState;

            switch (CurrentState)
            {
                case AuthState.Initial:
                    nextState = AuthState.AwaitingPhoneNumber;
                    break;
                case AuthState.AwaitingPhoneNumber:
                    nextState = AuthState.AwaitingVerificationCode;
                    break;
                case AuthState.AwaitingVerificationCode:
                    nextState = NeedsPasswordVerification ? AuthState.AwaitingPassword : AuthState.Authenticated;
                    break;
                case AuthState.AwaitingPassword:
                    nextState = AuthState.Authenticated;
                    break;
            }

            CurrentState = nextState;
        }

        public void RevertAuthState()
        {
            AuthState previousState = CurrentState;

            Action resetVerificationState = () =>
            {
                VerificationCode = null;
                Password = null;
            };

            Action resetPasswordState = () =>
            {
                Password = null;
            };

            switch (CurrentState)
            {
                case AuthState.AwaitingPhoneNumber:
                    previousState = AuthState.Initial;
                    break;
                case AuthState.AwaitingVerificationCode:
                    previousState = AuthState.AwaitingPhoneNumber;
                    resetVerificationState();
                    break;
                case AuthState.AwaitingPassword:
                    previousState = AuthState.AwaitingVerificationCode;
                    resetPasswordState();
                    break;
                case AuthState.Authenticated:
                    previousState = NeedsPasswordVerification ? AuthState.AwaitingPassword : AuthState.AwaitingVerificationCode;

                    if (NeedsPasswordVerification)
                        resetPasswordState();
                    else
                        resetVerificationState();

                    break;
            }

            CurrentState = previousState;
        }

        #endregion

        #region Private API

        private static ConcurrentQueue<string> ContractAddressQueue = new ConcurrentQueue<string>();
        private static ConcurrentBag<string> SeenAddresses = new ConcurrentBag<string>();

        private async Task OnMessageReceived(Update update)
        {
            switch (update)
            {
                case UpdateNewMessage unm: await HandleMessage(unm.message); break;
            }

            await Task.CompletedTask;
        }

        private Task HandleMessage(MessageBase messageBase, bool edit = false)
        {
            // ToDo
            //ChannelSemaphore.Wait();

            if (SelectedChannel == null || SelectedChannel.Invalidate(messageBase))
            {
                //ChannelSemaphore.Release();
                return Task.CompletedTask;
            }

            //ChannelSemaphore.Release();

            if (!(messageBase is Message message && !string.IsNullOrEmpty(message.message)))
                return Task.CompletedTask;

            bool foundSolanaAddress = false;
            PublicKey publicKey = null;

            foreach (string piece in message.message.Split(new char[] { '\n', '\t', '\r' }))
            {
                try
                {
                    publicKey = new PublicKey(piece);
                }
                catch (Exception)
                {
                    publicKey = null;
                }

                foundSolanaAddress = publicKey != null && publicKey.IsValid();

                if (foundSolanaAddress)
                    break;
            }

            if (foundSolanaAddress && !SeenAddresses.Contains(publicKey.Key))
            {
                SeenAddresses.Add(publicKey.Key);
                ContractAddressQueue.Enqueue(publicKey.Key);
                AddressFoundEvent?.Invoke(this, new AddressFoundEventArgs(ChatId, message.from_id, publicKey.Key));
            }

            return Task.CompletedTask;
        }

        #endregion
    }

    #region Supporting Classes

    public enum AuthState
    {
        Initial,
        AwaitingPhoneNumber,
        AwaitingPassword,
        AwaitingVerificationCode,
        Authenticated
    }

    public class AddressFoundEventArgs : EventArgs
    {
        public long ChatId { get; set; }
        public long SourceChatId { get; set; }
        public string ContractAddress { get; set; }

        public AddressFoundEventArgs(long chatId, long sourceChatId, string contractAddress)
        {
            ChatId = chatId;
            SourceChatId = sourceChatId;
            ContractAddress = contractAddress;
        }
    }

    #endregion
}
