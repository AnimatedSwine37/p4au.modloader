using p4au.modloader.Configuration;
using Reloaded.Mod.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p4au.modloader.Utilities
{
    public static class Utils
    {
        private static ILogger logger;
        private static Config config;

        /// <summary>
        /// Initiailse the Utils class with necessary stuff
        /// Only call this once at the start of the program
        /// </summary>
        public static void Initialise(ILogger logger, Config config)
        {
            Utils.logger = logger;
            Utils.config = config;
        }

        public static void LogVerbose(string message)
        {
            if(config.Verbose)
            {
                logger.WriteLine(message);
            }
        }
    }
}
