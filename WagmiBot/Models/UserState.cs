using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace WagmiBot
{
    public class UserState
    {
        public State CurrentState { get; set; } = State.Initial;
        public string SessionPath { get; set; }
        public Auth_SentCode SentCode { get; set; }
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
        public string VerificationCode { get; set; }
        public TL.User CurrentUser { get; set; }
        public TelegramChannel SelectedChannel { get; set; }
    }

    public enum State
    {
        Initial,
        AwaitingPhoneNumber,
        AwaitingPassword,
        AwaitingVerificationCode,
        Authenticated
    }
}
