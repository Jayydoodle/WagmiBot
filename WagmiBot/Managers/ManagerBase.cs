using JConsole;
using Spectre.Console;

namespace WagmiBot
{
    public abstract class ManagerBase : ConsoleFunction
    {
        #region Properties

        public abstract override string DisplayName { get; }

        #endregion
    }

    public abstract class ManagerBase<T> : ConsoleFunction
    where T : class, new()
    {
        #region Properties

        public override string DisplayName => typeof(T).Name.SplitByCase();

        private static readonly Lazy<T> _instance = new Lazy<T>(() => new T());
        public static T Instance => _instance.Value;

        #endregion
    }
}
