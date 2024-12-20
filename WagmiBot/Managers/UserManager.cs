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
        private readonly ConcurrentDictionary<long, WagmiUser> Users = new ConcurrentDictionary<long, WagmiUser>();

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

        public async Task<WagmiUser> GetUser(long chatId, Func<WagmiUser, Task> onNewUserCreated)
        {
            Users.TryGetValue(chatId, out WagmiUser user);

            if(user == null)
            {
                user = new WagmiUser(chatId);
                await onNewUserCreated(user);
                Users.TryAdd(chatId, user);
            }

            return user;
        }

        #endregion

        #region Documentation 

        private const string Docs = "An interface for managing user instances of the WagmiClient";

        #endregion
    }
}
