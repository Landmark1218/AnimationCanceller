using System.Configuration;
using System.Data;
using System.Windows;

namespace AnimCancelPatcher
{
    public partial class App : Application
    {
        //多重起動防止入れるかも
        //bool createdNew;
        //var mutex = new System.Threading.Mutex(true, "AnimCancelPatcherMutex", out createdNew);
    }
}