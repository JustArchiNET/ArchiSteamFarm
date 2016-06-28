using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics.CodeAnalysis;
using System.ServiceProcess;

namespace ArchiSteamFarm {
	[RunInstaller(true)]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public sealed class ArchiServiceInstaller : Installer {
		public ArchiServiceInstaller() {
			ServiceInstaller serviceInstaller = new ServiceInstaller();
			ServiceProcessInstaller serviceProcessInstaller = new ServiceProcessInstaller();

			serviceInstaller.ServiceName = SharedInfo.ServiceName;
			serviceInstaller.DisplayName = SharedInfo.ServiceName;
			serviceInstaller.Description = SharedInfo.ServiceDescription;

			// Defaulting to only starting when a user starts it, can be easily changed after install
			serviceInstaller.StartType = ServiceStartMode.Manual;

			// System account, requires admin privilege to install
			serviceProcessInstaller.Account = ServiceAccount.LocalSystem;

			Installers.Add(serviceInstaller);
			Installers.Add(serviceProcessInstaller);
		}
	}
}
