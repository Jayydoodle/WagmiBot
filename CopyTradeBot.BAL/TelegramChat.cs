using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace CopyTradeBot.BAL
{
    public class TelegramChat
    {
        #region Properties

        public long ID { get; set; }
        public string Title { get; set; }
        public Type ChatType { get; set; }

        #endregion

        #region Life Cycle

        public TelegramChat(ChatBase chat)
        {
            ID = chat.ID;
            Title = chat.Title;
            ChatType = chat.IsGroup ? Type.Group : Type.Channel;
        }

        #endregion

        public static IEnumerable<TelegramChat> GetAll(Client client)
        {

            var query = Task.Run<Messages_Chats>(async () => await client.Messages_GetAllChats()).Result;
            var chats = query.chats.Values
                .Where(x => x.IsChannel || x.IsGroup)
                .Where(x => x.IsActive)
                .OrderBy(x => x.Title)
                .Select(x => new TelegramChat(x));

            return chats;
        }

        #region Supporting Classes

        public enum Type
        {
            Channel,
            Group
        }

        #endregion
    }
}
