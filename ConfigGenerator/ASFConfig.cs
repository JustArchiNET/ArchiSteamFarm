using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ConfigGenerator {
	internal class ASFConfig {
		internal static List<ASFConfig> ASFConfigs = new List<ASFConfig>();

		internal string FilePath { get; set; }

		protected ASFConfig() {
			ASFConfigs.Add(this);
		}

		protected ASFConfig(string filePath) : base() {
			FilePath = filePath;
		}

		internal virtual void Save() {
			lock (FilePath) {
				try {
					File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
				} catch (Exception e) {
					Logging.LogGenericException(e);
				}
			}
		}
	}
}
