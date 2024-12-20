using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TL;
using WTelegram;
using ForumTopic = TL.ForumTopic;
using User = TL.User;

namespace WagmiBot
{
    public class TelegramChannel
    {
        #region Properties

        public long ID { get; set; }
        public string Title { get; set; }
        public Type ChatType { get; set; }
        public ChatBase Instance { get; set; }
        public ForumTopic SelectedTopic { get; set; }
        public User SelectedUser { get; set; }
        private Client Client { get; set; }
        private List<ForumTopic> Topics { get; set; }
        private List<User> Users { get; set; }

        #endregion

        #region Life Cycle

        public TelegramChannel(ChatBase chat, Client client)
        {
            ID = chat.ID;
            Title = chat.Title;
            ChatType = chat.IsGroup ? Type.Group : Type.Channel;
            Instance = chat;
            Client = client;
        }

        #endregion

        #region Public API

        public bool Invalidate(MessageBase message)
        {
            return SelectedTopic == null && message.Peer.ID != ID
             || SelectedTopic != null && (message.ReplyHeader == null || (message.ReplyHeader != null && message.ReplyHeader.TopicID != SelectedTopic.ID))
             || SelectedUser != null && (message.From == null || (message.From != null && message.From.ID != SelectedUser.ID));
        }

        public async Task<IEnumerable<ForumTopic>> GetTopics(Client client)
        {
            if (Topics == null)
            {
                var query = Task.Run<Messages_ForumTopics>(async () => await Client.Channels_GetAllForumTopics(Instance as Channel)).Result;

                Topics = query.topics.Where(x => x is ForumTopic)
                                     .Where(x => x.ID != 1) // exclude general topic
                                     .Select(x => (ForumTopic)x)
                                     .OrderBy(x => x.title)
                                     .ToList();
            }

            return Topics;
        }

        public async Task<IEnumerable<User>> GetUsers(Client client)
        {
            if (Users == null)
            {
                var query = await client.Channels_GetAllParticipants(Instance as Channel);
                Users = query.users.Values.OrderBy(x => x.MainUsername ?? x.first_name).ToList();
            }

            return Users;
        }

        public static async Task<IEnumerable<TelegramChannel>> GetAll(Client client)
        {
            var query = await client.Messages_GetAllChats();

            var chats = query.chats.Values
                .Where(x => x.IsActive)
                .OrderBy(x => x.Title)
                .Select(x => new TelegramChannel(x, client));

            return chats;
        }

        #endregion

        #region Supporting Classes

        public enum Type
        {
            Channel,
            Group
        }

        #endregion
    }
}
