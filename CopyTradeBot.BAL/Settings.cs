using CustomSpectreConsole;

namespace CopyTradeBot.BAL
{
    public class Settings : SettingsNode<Settings>
    {

        #region Constants

        public const int MinSyncInterval = 1;
        public const int DefaultSyncInterval = 10;

        public const int MinPurgeInterval = 1;
        public const int DefaultPurgeInterval = 60;

        #endregion


        #region Properties

        public override SettingsNodeType Type => SettingsNodeType.Application;

        #endregion

        #region Instances

        public static Settings TelegramAPIHash => new Settings()
        {
            Name = nameof(TelegramAPIHash),
        };

        public static Settings TelegramAPIKey => new Settings()
        {
            Name = nameof(TelegramAPIKey),
        };

        public static Settings TelegramPhoneNumber => new Settings()
        {
            Name = nameof(TelegramPhoneNumber),
        };

        #endregion
    }
}
