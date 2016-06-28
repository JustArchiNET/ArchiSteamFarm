using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ArchiSteamFarm
{
    [RunInstaller(true)]
    public sealed class ArchiServiceInstaller : System.Configuration.Install.Installer
    {
        public ArchiServiceInstaller()
        {
            ServiceInstaller serviceInstaller = new ServiceInstaller();
            ServiceProcessInstaller serviceProcessInstaller = new ServiceProcessInstaller();

            serviceInstaller.ServiceName = SharedInfo.ServiceName;
            serviceInstaller.DisplayName = SharedInfo.ServiceDescription;

            //defaulting to only starting when a user starts it, can be easily changed after install
            serviceInstaller.StartType = ServiceStartMode.Manual;

            //system account, requires admin privilege to install
            serviceProcessInstaller.Account = ServiceAccount.LocalSystem;

            Installers.Add(serviceInstaller);
            Installers.Add(serviceProcessInstaller);
        }
    }
}
