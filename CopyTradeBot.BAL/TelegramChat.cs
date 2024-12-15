using JConsole;
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
        public ChatBase Instance { get; set; }

        private List<ForumTopic> _topics;
        public List<ForumTopic> Topics 
        { 
            get
            {
                if (_topics == null)
                {
                    _topics = new List<ForumTopic>();

                    try
                    {
                        var query = Task.Run<Messages_ForumTopics>(async () => await Client.Channels_GetAllForumTopics(Instance as Channel)).Result;
                        _topics = query.topics.Select(x => x as ForumTopic).ToList();
                    }
                    catch (Exception)
                    {
                    }
                }

                return _topics;
            }
            private set { _topics = value; }
        }
        private Client Client { get; set; }

        #endregion

        #region Life Cycle

        public TelegramChat(ChatBase chat, Client client)
        {
            ID = chat.ID;
            Title = chat.Title;
            ChatType = chat.IsGroup ? Type.Group : Type.Channel;
            Instance = chat;
            Client = client;
        }

        #endregion

        public static IEnumerable<TelegramChat> GetAll(Client client)
        {
            var query = Task.Run<Messages_Chats>(async () => await client.Messages_GetAllChats()).Result;
            var chats = query.chats.Values
                .Where(x => x.IsActive)
                .OrderBy(x => x.Title)
                .ToList()
                .Select(x => new TelegramChat(x, client));

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
