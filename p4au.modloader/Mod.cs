using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using System.Linq;

namespace p4au.modloader
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod
    {
        public Mod(IReloadedHooks hooks, ILogger logger, ModGenericTuple<IModConfigV1>[] activeMods)
        {
            // TODO: Implement some mod logic    
            foreach(var mod in activeMods.Where(mod => mod.Generic.ModDependencies.Contains("reloaded.universal.redirector")))
            {
                logger.WriteLine(mod.Generic.ModName);
            }
        }
    }
}