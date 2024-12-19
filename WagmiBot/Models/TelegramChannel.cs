using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace WagmiBot
{
    public class TelegramChannel
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
                if(_topics == null)
                {
                    _topics = new List<ForumTopic>();

                    try
                    {
                        var query = Task.Run<Messages_ForumTopics>(async () => await Client.Channels_GetAllForumTopics(Instance as Channel)).Result;

                        _topics = query.topics.Where(x => x is ForumTopic)
                                              .Where(x => x.ID != 1) // exclude general topic
                                              .Select(x => x as ForumTopic)
                                              .ToList();
                    }
                    catch (Exception)
                    {
                    }
                }

                return _topics;
            }
        }

        private List<User> _users;
        public List<User> Users
        {
            get
            {
                if (_users == null)
                {
                    _users = new List<User>();

                    try
                    {
                        var query = Task.Run<Channels_ChannelParticipants>(async () => await Client.Channels_GetAllParticipants(Instance as Channel)).Result;
                        _users = query.users.Values.ToList();
                    }
                    catch (Exception)
                    {
                    }
                }

                return _users;
            }
        }

        public ForumTopic SelectedTopic { get; set; }
        public User SelectedUser { get; set; }

        private Client Client { get; set; }

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

        public static IEnumerable<TelegramChannel> GetAll(Client client)
        {
            var query = Task.Run<Messages_Chats>(async () => await client.Messages_GetAllChats()).Result;
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
