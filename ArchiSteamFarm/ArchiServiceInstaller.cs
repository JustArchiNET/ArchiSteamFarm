/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
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

			serviceInstaller.Installers.Clear();

			EventLogInstaller logInstaller = new EventLogInstaller {
				Log = SharedInfo.EventLog,
				Source = SharedInfo.EventLogSource
			};

			Installers.Add(serviceInstaller);
			Installers.Add(serviceProcessInstaller);
			Installers.Add(logInstaller);
		}
	}
}
