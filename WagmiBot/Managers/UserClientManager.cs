using JConsole;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace WagmiBot
{
    public class UserClientManager : ManagerBase<UserClientManager>
    {
        public readonly ConcurrentDictionary<long, UserState> UserStates = new ConcurrentDictionary<long, UserState>();
        public readonly ConcurrentDictionary<long, WTelegram.Client> UserClients = new ConcurrentDictionary<long, WTelegram.Client>();

        protected override List<MenuOption> GetMenuOptions()
        {
            return new List<MenuOption>();
        }

        protected override bool Initialize()
        {
            return true;
        }
    }
}
