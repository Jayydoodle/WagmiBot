using CopyTradeBot.BAL;
using JConsole;
using Spectre.Console;

namespace CopyTradeBot
{
    public abstract class ManagerBase : ConsoleFunction
    {
        #region Properties

        public abstract override string DisplayName { get; }

        #endregion
    }

    public abstract class ManagerBase<T> : ManagerBase
    where T : class, new()
    {
        #region Properties

        private static readonly Lazy<T> _instance = new Lazy<T>(() => new T());
        public static T Instance => _instance.Value;

        #endregion
    }
}
