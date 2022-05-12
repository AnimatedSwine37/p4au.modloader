using p4au.modloader.Configuration;
using p4au.modloader.Configuration.Implementation;
using p4au.modloader.Utilities;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using Reloaded.Universal.Redirector.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if DEBUG
using System.Diagnostics;
using System.Linq;
#endif

namespace p4au.modloader
{
    public class Program : IMod
    {
        /// <summary>
        /// Used for writing text to the Reloaded log.
        /// </summary>
        private ILogger _logger = null!;

        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private IModLoader _modLoader = null!;

        /// <summary>
        /// Stores the contents of your mod's configuration. Automatically updated by template.
        /// </summary>
        private Config _configuration = null!;

        /// <summary>
        /// An interface to Reloaded's the function hooks/detours library.
        /// See: https://github.com/Reloaded-Project/Reloaded.Hooks
        ///      for documentation and samples. 
        /// </summary>
        private IReloadedHooks _hooks = null!;

        /// <summary>
        /// Configuration of the current mod.
        /// </summary>
        private IModConfig _modConfig = null!;

        /// <summary>
        /// Encapsulates your mod logic.
        /// </summary>
        private Mod _mod = null!;

        /// <summary>
        /// Provides access to the universal file redirector api
        /// </summary>
        private WeakReference<IRedirectorController> _redirectorController = null!;

        /// <summary>
        /// Stores file cache information to speed up merging
        /// </summary>
        private CacheConfig _cacheConfig = null!;

        private string _modLoaderPath = null!;

        /// <summary>
        /// Entry point for your mod.
        /// </summary>
        /// 

        public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
        {
#if DEBUG
        // Attaches debugger in debug mode; ignored in release.
        Debugger.Launch();
#endif

            _modLoader = (IModLoader)loaderApi;
            _modConfig = (IModConfig)modConfig;
            _logger = (ILogger)_modLoader.GetLogger();
            _modLoader.GetController<IReloadedHooks>().TryGetTarget(out _hooks!);

            // Your config file is in Config.json.
            // Need a different name, format or more configurations? Modify the `Configurator`.
            // If you do not want a config, remove Configuration folder and Config class.
            var configurator = new Configurator(_modLoader.GetModConfigDirectory(_modConfig.ModId));
            _configuration = configurator.GetConfiguration<Config>(0);
            _configuration.ConfigurationUpdated += OnConfigurationUpdated;
            _cacheConfig = configurator.GetConfiguration<CacheConfig>(1);

            /*
                Your mod code starts below.
                Visit https://github.com/Reloaded-Project for additional optional libraries.
            */

            // Disable the file redirector so it doesn't redirect files whilst trying to merge them
            _redirectorController = _modLoader.GetController<IRedirectorController>();
            if (_redirectorController != null &&
                _redirectorController.TryGetTarget(out var target))
            {
                target.Disable();
            }

            Utils.Initialise(_logger, _configuration);

            List<string> activeModPaths = GetActiveModPaths();
            _modLoaderPath = _modLoader.GetDirectoryForModId("p4au.modloader");
            _mod = new Mod(activeModPaths, _modLoaderPath, _cacheConfig);
            _modLoader.ModLoaded += ModLoaded;
            
            // Re enable the file redirector now that everything's set up
            if (_redirectorController != null &&
                _redirectorController.TryGetTarget(out target))
            {
                target.Enable();
                RemoveDuplicateRedirects(activeModPaths);
            }
        }

        private void ModLoaded(IModV1 mod, IModConfigV1 modConfig)
        {
            if(modConfig.ModId != "p4au.modloader" && modConfig.ModDependencies.Contains("reloaded.universal.redirector"))
            {
                RemoveDuplicateRedirects(_modLoader.GetDirectoryForModId(modConfig.ModId));
            }
        }

        /// <summary>
        /// Removes any files that might be redirected instead of those in the mod loader's folder from a list of mod paths (so higher priority mods don't break it)
        /// </summary>
        /// <param name="activeModPaths">A List of all of the active mod's paths</param>
        private void RemoveDuplicateRedirects(List<string> activeModPaths)
        {
            foreach (var modDir in activeModPaths)
            {
                RemoveDuplicateRedirects(modDir);
            }
        }

        /// <summary>
        ///  Removes any files that might be redirected instead of those in the mod loader's folder (so higher priority mods don't break it)
        /// </summary>
        /// <param name="modPath">The path to the mod dir that will be checked for duplicate redirects</param>
        private void RemoveDuplicateRedirects(string modPath)
        {
            string modName = Path.GetFileName(modPath);
            Utils.LogVerbose($"Removing duplicate redirects from {modName}");
            _redirectorController.TryGetTarget(out var target);
            if (target == null)
                return;
            string assetPath = Path.Combine(modPath, "Redirector", "asset");
            if (!Directory.Exists(assetPath))
                return;
            string modLoaderAssets = Path.Combine(_modLoaderPath, "Redirector", "asset");
            foreach (var file in Directory.GetFiles(assetPath))
            {
                if (File.Exists(Path.Combine(modLoaderAssets, Path.GetFileName(file))))
                {
                    target.RemoveRedirect(file);
                }
            }
            Utils.LogVerbose($"Done removing duplicate redirects from {modName}");
        }

        private List<string> GetActiveModPaths()
        {
            List<string> mods = new List<string>();
            foreach (var mod in _modLoader.GetActiveMods().Where(m => m.Generic.ModDependencies.Contains("reloaded.universal.redirector") && m.Generic.ModId != "p4au.modloader"))
            {
                mods.Add(_modLoader.GetDirectoryForModId(mod.Generic.ModId));
            }
            return mods;
        }

        private void OnConfigurationUpdated(IConfigurable obj)
        {
            /*
                This is executed when the configuration file gets 
                updated by the user at runtime.
            */

            // Replace configuration with new.
            _configuration = (Config)obj;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");

            // Apply settings from configuration.
            // ... your code here.
        }

        /* Mod loader actions. */
        public void Suspend()
        {
            /*  Some tips if you wish to support this (CanSuspend == true)

                A. Undo memory modifications.
                B. Deactivate hooks. (Reloaded.Hooks Supports This!)
            */
        }

        public void Resume()
        {
            /*  Some tips if you wish to support this (CanSuspend == true)

                A. Redo memory modifications.
                B. Re-activate hooks. (Reloaded.Hooks Supports This!)
            */
        }

        public void Unload()
        {
            /*  Some tips if you wish to support this (CanUnload == true).

                A. Execute Suspend(). [Suspend should be reusable in this method]
                B. Release any unmanaged resources, e.g. Native memory.
            */
        }

        /*  If CanSuspend == false, suspend and resume button are disabled in Launcher and Suspend()/Resume() will never be called.
            If CanUnload == false, unload button is disabled in Launcher and Unload() will never be called.
        */
        public bool CanUnload() => false;
        public bool CanSuspend() => false;

        /* Automatically called by the mod loader when the mod is about to be unloaded. */
        public Action Disposing { get; } = null!;
    }
}