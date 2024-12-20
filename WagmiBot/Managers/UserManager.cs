using JConsole;
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
    public class UserManager : ManagerBase<UserManager>
    {
        #region Properties

        public override string Documentation => Docs;
        private readonly ConcurrentDictionary<long, UserState> UserStates = new ConcurrentDictionary<long, UserState>();
        private readonly ConcurrentDictionary<long, WTelegram.Client> UserClients = new ConcurrentDictionary<long, WTelegram.Client>();

        #endregion

        #region Life Cycle

        protected override List<MenuOption> GetMenuOptions()
        {
            return new List<MenuOption>();
        }

        protected override bool Initialize()
        {
            return true;
        }

        #endregion

        #region Public API

        public async Task AddClient(long chatId, Client client)
        {
            UserClients.TryAdd(chatId, client);
        }

        public async Task<Client> GetClient(long chatId)
        {
            UserClients.TryGetValue(chatId, out Client client);
            return client;
        }

        public async Task RemoveClient(long chatId, Client client = null)
        {
            if(client != null)
            {
                KeyValuePair<long, Client> pair = new KeyValuePair<long, Client>(chatId, client);
                UserClients.TryRemove(pair);
            }
            else
            {
                UserClients.TryRemove(chatId, out client);
            }

            if (client != null)
                await client.DisposeAsync();
        }

        public async Task<UserState> GetState(long chatId)
        {
            var state = UserStates.GetOrAdd(chatId, _ => new UserState());
            await Task.CompletedTask;

            return state;
        }

        public async Task UpdateState(UserState state)
        {
            await Task.CompletedTask;
        }

        #endregion

        #region Documentation 

        private const string Docs = "An interface for managing user instances of the WagmiClient";

        #endregion
    }
}
