using System.ComponentModel;
using ConfigGenerator.Localization;

namespace ConfigGenerator {
	internal sealed class LocalizedCategoryAttribute : CategoryAttribute {
		internal LocalizedCategoryAttribute(string key) : base(key) { }

		protected override string GetLocalizedString(string value) {
			switch (value) {
				case "Access":
					return CGStrings.CategoryAccess;
				case "Advanced":
					return CGStrings.CategoryAdvanced;
				case "Core":
					return '\t' + CGStrings.CategoryCore;
				case "Debugging":
					return CGStrings.CategoryDebugging;
				case "Performance":
					return CGStrings.CategoryPerformance;
				case "Updates":
					return CGStrings.CategoryUpdates;
				default:
					Logging.LogGenericWarning("Unknown value: " + value);
					return value;
			}
		}
	}
}
