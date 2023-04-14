using Newtonsoft.Json;

namespace ArchiSteamFarm.Plugins.Interfaces; 

public interface IWebInterface : IPlugin {
	string PhysicalPath { get; }

	[JsonProperty]
	string WebPath { get; }
}
