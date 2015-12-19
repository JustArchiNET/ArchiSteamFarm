using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace $rootnamespace$
{
    public static class ILBundle
    {
        public static List<String> ResourceNames = new List<string>();
        public static Assembly ExecutingAssembly;

        static ILBundle()
        {
            ExecutingAssembly = Assembly.GetExecutingAssembly();
            ResourceNames = ExecutingAssembly.GetManifestResourceNames().Where(n => n.EndsWith(".dll") || n.EndsWith(".exe")).ToList();
        }

        public static void RegisterAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, assembly) =>
            {
                var assemblyName = new AssemblyName(assembly.Name);
                var paths = new List<string>{
                	string.Format("{0}.dll", assemblyName.Name),
                	string.Format("{0}.exe", assemblyName.Name)
                	};

				foreach(var path in paths)
				{
	                if (ResourceNames.Contains(path))
	                {
	                    using (var stream = ExecutingAssembly.GetManifestResourceStream(path))
	                    {
	                        if (stream == null)
	                            return null;

	                        var bytes = new byte[stream.Length];
	                        stream.Read(bytes, 0, bytes.Length);
	                        try
	                        {
	                            return Assembly.Load(bytes);
	                        }
	                        catch (Exception ex)
	                        {
	                            System.Diagnostics.Debug.Print("Failed to load: {0}, Exception: {1}", path, ex.Message);
	                        }
	                    }
	                }
	            }

                return null;
            };
        }
    }
}
