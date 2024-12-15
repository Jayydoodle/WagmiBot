using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JConsole.Interfaces
{
    public interface ISettingsNode
    {
        public static abstract List<T> GetAll<T>() where T : class;
    }
}
