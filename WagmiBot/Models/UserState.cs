//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using TL;

//namespace WagmiBot
//{
//    public class UserState
//    {
//        public State CurrentState { get; set; } = State.Initial;
//        public string SessionPath { get; set; }
//        public Auth_SentCode SentCode { get; set; }
//        public string PhoneNumber { get; set; }
//        public string Password { get; set; }
//        public string VerificationCode { get; set; }
//        public bool NeedsPasswordVerification { get; set; }
//        public TL.User CurrentUser { get; set; }
//        public TelegramChannel SelectedChannel { get; set; } // ToDo: Everywhere 'state' is called probably needs concurrency locking

//        public void MoveToNextState()
//        {
//            State nextState = CurrentState;

//            switch (CurrentState)
//            {
//                case State.Initial:
//                    nextState = State.AwaitingPhoneNumber;
//                    break;
//                case State.AwaitingPhoneNumber:
//                    nextState = State.AwaitingVerificationCode;
//                    break;
//                case State.AwaitingVerificationCode:
//                    nextState = NeedsPasswordVerification ? State.AwaitingPassword : State.Authenticated;
//                    break;
//                case State.AwaitingPassword:
//                    nextState = State.Authenticated;
//                    break;
//            }

//            CurrentState = nextState;
//        }

//        public void MoveToPreviousState()
//        {
//            State previousState = CurrentState;

//            Action resetVerificationState = () =>
//            {
//                VerificationCode = null;
//                Password = null;
//            };

//            Action resetPasswordState = () =>
//            {
//                Password = null;
//            };

//            switch (CurrentState)
//            {
//                case State.AwaitingPhoneNumber:
//                    previousState = State.Initial;
//                    break;
//                case State.AwaitingVerificationCode:
//                    previousState = State.AwaitingPhoneNumber;
//                    resetVerificationState();
//                    break;
//                case State.AwaitingPassword:
//                    previousState = State.AwaitingVerificationCode;
//                    resetPasswordState();
//                    break;
//                case State.Authenticated:
//                    previousState = NeedsPasswordVerification ? State.AwaitingPassword : State.AwaitingVerificationCode;

//                    if (NeedsPasswordVerification)
//                        resetPasswordState();
//                    else
//                        resetVerificationState();

//                    break;
//            }

//            CurrentState = previousState;
//        }
//    }

//    public enum State
//    {
//        Initial,
//        AwaitingPhoneNumber,
//        AwaitingPassword,
//        AwaitingVerificationCode,
//        Authenticated
//    }
//}
